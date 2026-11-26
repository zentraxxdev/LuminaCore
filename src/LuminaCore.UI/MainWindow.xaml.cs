using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using LuminaCore.Core.Engine;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;
using LuminaCore.Core.Plugins;
using LuminaCore.UI.Services;
using LuminaCore.UI.ViewModels;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LuminaCore.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly RenderPipeline _pipeline;
    private readonly IHardwareController _hardware;
    private readonly LuaPluginManager _pluginManager;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly DispatcherTimer _liveTickTimer;
    private readonly byte[] _pixelCopyBuffer = new byte[CanvasSurface.DefaultWidth * CanvasSurface.DefaultHeight * 4];
    private readonly Dictionary<int, RgbColor[]> _zoneColorCopy = new();
    private readonly AudioSpectrumFrame _audioCopyBuffer = new();
    private readonly string[] _bannerStripScratch = new string[16];

    private double[] _latestCpuHistory = [];
    private double[] _latestGpuHistory = [];
    private bool _historyDirty;
    private bool _webViewReady;
    private Icon? _activeTrayIcon;
    private Icon? _blackoutTrayIcon;
    private bool _isExiting;

    public MainWindow(
        MainViewModel viewModel,
        RenderPipeline pipeline,
        IHardwareController hardware,
        LuaPluginManager pluginManager,
        GlobalHotkeyService hotkeyService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _pipeline = pipeline;
        _hardware = hardware;
        _pluginManager = pluginManager;
        _hotkeyService = hotkeyService;
        DataContext = _viewModel;

        _pluginManager.PluginsReloaded += () => Dispatcher.BeginInvoke(PushFullStateToWeb);

        _activeTrayIcon = TrayIconGenerator.CreateTrayIcon(isBlackout: false);
        _blackoutTrayIcon = TrayIconGenerator.CreateTrayIcon(isBlackout: true);
        TrayIcon.Icon = _activeTrayIcon;

        _viewModel.BlackoutStateChanged += isBlackout =>
        {
            UpdateTrayIconForBlackout(isBlackout);
            PushFullStateToWeb();
        };
        _viewModel.HotkeySettingChanged += OnHotkeySettingChanged;
        _viewModel.ZonesStructureChanged += () => PushFullStateToWeb();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsOpenRgbConnected)
                or nameof(MainViewModel.OpenRgbStatusText)
                or nameof(MainViewModel.ActiveProfile))
            {
                Dispatcher.BeginInvoke(PushFullStateToWeb);
            }
        };
        _viewModel.TemperatureHistoryUpdated += (cpu, gpu) =>
        {
            _latestCpuHistory = cpu.ToArray();
            _latestGpuHistory = gpu.ToArray();
            _historyDirty = true;
        };

        _hotkeyService.BlackoutHotkeyPressed += () =>
        {
            Dispatcher.BeginInvoke(() => _viewModel.IsBlackout = !_viewModel.IsBlackout);
        };

        // Throttled 25 FPS (40ms) UI bridge — decoupled from locked 60 FPS hardware render loop
        _liveTickTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _liveTickTimer.Tick += LiveTickTimer_Tick;

        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        Closing += MainWindow_Closing;
    }

    public void InitializeHiddenInTray()
    {
        _ = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        RegisterGlobalHotkeyIfEnabled();
        _pipeline.UiPreviewEnabled = false;
        Hide();
        TrimWorkingSetForTray();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, nint min, nint max);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WmNcLButtonDown = 0x00A1;
    private static readonly IntPtr HtCaption = new(2);

    private static void TrimWorkingSetForTray()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
            using var proc = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(proc.Handle, -1, -1);
        }
        catch { }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RegisterGlobalHotkeyIfEnabled();
        _pipeline.UiPreviewEnabled = true;

        try
        {
            string? wv2Ver = null;
            try { wv2Ver = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { }
            if (string.IsNullOrWhiteSpace(wv2Ver))
            {
                var res = MessageBox.Show(
                    "Microsoft Edge WebView2 Runtime is required to display LuminaCore.\nOpen Microsoft's official download page now?",
                    "LuminaCore — Missing WebView2 Runtime",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo("https://developer.microsoft.com/en-us/microsoft-edge/webview2/") { UseShellExecute = true });
                return;
            }

            string userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LuminaCore", "WebView2");
            Directory.CreateDirectory(userDataDir);

            var wvOpts = new CoreWebView2EnvironmentOptions("--renderer-process-limit=1 --disable-features=Translate,BackForwardCache --js-flags=--max-old-space-size=64");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataDir, wvOpts);
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            string htmlPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            WebView.Source = new Uri(htmlPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "LuminaCore", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _liveTickTimer.Start();
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = JObject.Parse(e.WebMessageAsJson);
            string type = msg["type"]?.ToString() ?? string.Empty;

            switch (type)
            {
                case "ready":
                    _webViewReady = true;
                    PushFullStateToWeb();
                    break;

                case "windowDrag":
                    try
                    {
                        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                        ReleaseCapture();
                        SendMessage(hwnd, WmNcLButtonDown, HtCaption, IntPtr.Zero);
                    }
                    catch { }
                    break;
                case "windowMinimize":
                    WindowState = WindowState.Minimized;
                    break;
                case "windowMaximize":
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    break;
                case "windowClose":
                    Close();
                    break;

                case "selectEffect":
                    if (Enum.TryParse<EffectType>(msg["effect"]?.ToString(), true, out var fx))
                    {
                        _pipeline.ActivePluginId = null;
                        _viewModel.ActiveEffect = fx;
                        PushFullStateToWeb();
                    }
                    break;

                case "selectPlugin":
                    _pipeline.ActivePluginId = msg["pluginId"]?.ToString();
                    PushFullStateToWeb();
                    break;

                case "togglePause":
                    _pipeline.IsPaused = !_pipeline.IsPaused;
                    PushFullStateToWeb();
                    break;

                case "stepEffect":
                {
                    int delta = msg["delta"]?.Value<int>() ?? 1;
                    var all = Enum.GetValues<EffectType>();
                    int idx = Array.IndexOf(all, _viewModel.ActiveEffect);
                    int next = (idx + delta + all.Length) % all.Length;
                    _pipeline.ActivePluginId = null;
                    _viewModel.ActiveEffect = all[next];
                    PushFullStateToWeb();
                    break;
                }

                case "applyThemeHardware":
                {
                    if (Enum.TryParse<EffectType>(msg["effect"]?.ToString(), true, out var tfx))
                    {
                        _pipeline.ActivePluginId = null;
                        _viewModel.ActiveEffect = tfx;
                    }
                    string? hex = msg["primaryHex"]?.ToString();
                    string? hex2 = msg["secondaryHex"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(hex)) _viewModel.PrimaryColorHex = hex;
                    if (!string.IsNullOrWhiteSpace(hex) && !string.IsNullOrWhiteSpace(hex2))
                    {
                        _pipeline.UpdateParameters(p =>
                        {
                            p.PrimaryColorHex = hex;
                            p.SecondaryColorHex = hex2;
                            p.GradientStops =
                            [
                                new GradientStopConfig(0.0, hex),
                                new GradientStopConfig(0.5, hex2),
                                new GradientStopConfig(1.0, hex)
                            ];
                        });
                    }
                    PushFullStateToWeb();
                    break;
                }

                case "setChannelColorOrder":
                {
                    int chIdx = msg["channelIndex"]?.Value<int>() ?? 0;
                    if (Enum.TryParse<ChannelColorOrder>(msg["order"]?.ToString(), true, out var order))
                    {
                        _hardware.SetChannelColorOrder(chIdx, order);
                        var z = _viewModel.Zones.FirstOrDefault(x => x.ZoneIndex == chIdx);
                        if (z is not null) _viewModel.SaveAllState();
                    }
                    break;
                }

                case "setCalibration":
                {
                    int chIdx = msg["channelIndex"]?.Value<int>() ?? 0;
                    int lit = msg["litCount"]?.Value<int>() ?? 0;
                    _hardware.SetCalibrationPreview(chIdx, lit);
                    break;
                }

                case "stopCalibration":
                    _hardware.StopCalibrationPreview();
                    break;

                case "copyHidReport":
                {
                    var diag = _hardware.GetHidDiagnostics();
                    try { Clipboard.SetText(diag.DiagnosticReport); } catch { }
                    break;
                }

                case "reloadPlugins":
                    _pluginManager.ReloadAll();
                    break;

                case "openPluginsFolder":
                    try { Process.Start(new ProcessStartInfo("explorer.exe", _pluginManager.PluginsDirectory) { UseShellExecute = true }); } catch { }
                    break;

                case "fetchCommunityCatalog":
                    _ = FetchCommunityCatalogAsync();
                    break;

                case "installCommunityPlugin":
                {
                    string? url = msg["url"]?.ToString();
                    string? file = msg["file"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(file))
                        _ = InstallCommunityPluginAsync(url, file);
                    break;
                }

                case "setBrightness":
                    _viewModel.MasterBrightness = msg["value"]?.Value<double>() ?? _viewModel.MasterBrightness;
                    break;

                case "setParam":
                    ApplyParamFromWeb(msg["key"]?.ToString(), msg["value"]);
                    break;

                case "applyQuickPalette":
                    _viewModel.ApplyQuickPaletteCommand.Execute(msg["palette"]?.ToString());
                    PushFullStateToWeb();
                    break;

                case "toggleBlackout":
                    _viewModel.IsBlackout = !_viewModel.IsBlackout;
                    break;

                case "reconnect":
                    _viewModel.ReconnectOpenRgbCommand.Execute(null);
                    break;

                case "syncHardware":
                    _viewModel.SyncZonesFromHardwareCommand.Execute(null);
                    PushFullStateToWeb();
                    break;

                case "addZone":
                    _viewModel.AddZoneCommand.Execute(null);
                    PushFullStateToWeb();
                    break;

                case "syncAllChannels":
                    _viewModel.SyncAllChannelsCommand.Execute(null);
                    PushFullStateToWeb();
                    break;

                case "cycleZoneDevice":
                {
                    int zIdx = msg["zoneIndex"]?.Value<int>() ?? -1;
                    var zone = _viewModel.Zones.FirstOrDefault(z => z.ZoneIndex == zIdx);
                    zone?.CycleDeviceTypeCommand.Execute(null);
                    PushFullStateToWeb();
                    break;
                }

                case "renameZone":
                {
                    int zIdx = msg["zoneIndex"]?.Value<int>() ?? -1;
                    var zone = _viewModel.Zones.FirstOrDefault(z => z.ZoneIndex == zIdx);
                    if (zone is not null)
                    {
                        zone.Name = msg["name"]?.ToString() ?? zone.Name;
                        _viewModel.SaveAllState();
                    }
                    break;
                }

                case "toggleZone":
                {
                    int zIdx = msg["zoneIndex"]?.Value<int>() ?? -1;
                    var zone = _viewModel.Zones.FirstOrDefault(z => z.ZoneIndex == zIdx);
                    if (zone is not null) zone.IsEnabled = msg["enabled"]?.Value<bool>() ?? true;
                    break;
                }

                case "toggleZoneSync":
                {
                    int zIdx = msg["zoneIndex"]?.Value<int>() ?? -1;
                    var zone = _viewModel.Zones.FirstOrDefault(z => z.ZoneIndex == zIdx);
                    zone?.ToggleSyncCommand.Execute(null);
                    PushFullStateToWeb();
                    break;
                }

                case "setZoneSoloEffect":
                {
                    int zIdx = msg["zoneIndex"]?.Value<int>() ?? -1;
                    var zone = _viewModel.Zones.FirstOrDefault(z => z.ZoneIndex == zIdx);
                    if (zone is not null && Enum.TryParse<EffectType>(msg["effect"]?.ToString(), true, out var soloFx))
                    {
                        zone.IndependentEffect = soloFx;
                    }
                    break;
                }

                case "applyProfile":
                {
                    string id = msg["id"]?.ToString() ?? string.Empty;
                    var prof = _viewModel.Profiles.FirstOrDefault(p => p.Id == id);
                    if (prof is not null)
                    {
                        _viewModel.ActivateProfile(prof, isAutoSwitch: false);
                        _viewModel.SelectedProfile = prof;
                        PushFullStateToWeb();
                    }
                    break;
                }

                case "selectProfileEdit":
                {
                    string id = msg["id"]?.ToString() ?? string.Empty;
                    var prof = _viewModel.Profiles.FirstOrDefault(p => p.Id == id);
                    if (prof is not null) _viewModel.SelectedProfile = prof;
                    break;
                }

                case "createProfile":
                    _viewModel.CreateProfileCommand.Execute(null);
                    PushFullStateToWeb();
                    break;

                case "duplicateProfile":
                    _viewModel.DuplicateProfileCommand.Execute(null);
                    PushFullStateToWeb();
                    break;

                case "deleteProfile":
                    _viewModel.DeleteProfileCommand.Execute(null);
                    PushFullStateToWeb();
                    break;

                case "renameSelectedProfile":
                    if (_viewModel.SelectedProfile is not null)
                    {
                        _viewModel.SelectedProfile.Name = msg["name"]?.ToString() ?? _viewModel.SelectedProfile.Name;
                        _viewModel.SaveAllState();
                        PushFullStateToWeb();
                    }
                    break;

                case "addProcess":
                    _viewModel.NewProcessExecutable = msg["exe"]?.ToString() ?? string.Empty;
                    _viewModel.AddProcessBindingCommand.Execute(null);
                    PushFullStateToWeb();
                    break;

                case "removeProcess":
                    _viewModel.RemoveProcessBindingCommand.Execute(msg["exe"]?.ToString());
                    PushFullStateToWeb();
                    break;

                case "saveProfiles":
                    _viewModel.SaveAllState();
                    break;

                case "updateSetting":
                    ApplySettingFromWeb(msg["key"]?.ToString(), msg["value"]);
                    break;

                case "openFolder":
                    _viewModel.OpenDataFolderCommand.Execute(null);
                    break;

                case "exportProfiles":
                    _viewModel.ExportProfilesCommand.Execute(null);
                    break;

                case "checkUpdates":
                    _ = CheckGitHubUpdatesAsync();
                    break;
            }
        }
        catch { }
    }

    private async Task FetchCommunityCatalogAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LuminaCore/2.2");
            string json = await http.GetStringAsync("https://raw.githubusercontent.com/zentraxxdev/LuminaCore/main/community/catalog.json");
            await Dispatcher.InvokeAsync(() =>
            {
                if (_webViewReady && WebView.CoreWebView2 is not null)
                    WebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(new { type = "communityCatalog", items = JArray.Parse(json) }));
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_webViewReady && WebView.CoreWebView2 is not null)
                    WebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(new { type = "toast", message = $"Community fetch failed: {ex.Message}" }));
            });
        }
    }

    private async Task InstallCommunityPluginAsync(string url, string fileName)
    {
        try
        {
            string safeName = Path.GetFileName(fileName);
            if (!safeName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) return;
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LuminaCore/2.2");
            string code = await http.GetStringAsync(url);
            string target = Path.Combine(_pluginManager.PluginsDirectory, safeName);
            await File.WriteAllTextAsync(target, code);
            await Dispatcher.InvokeAsync(() =>
            {
                _pluginManager.ReloadAll();
                PushFullStateToWeb();
                if (_webViewReady && WebView.CoreWebView2 is not null)
                    WebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(new { type = "toast", message = $"Installed {safeName}" }));
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_webViewReady && WebView.CoreWebView2 is not null)
                    WebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(new { type = "toast", message = $"Install failed: {ex.Message}" }));
            });
        }
    }

    private async Task CheckGitHubUpdatesAsync()
    {
        string message = "You are on the latest version (v2.2.0)";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LuminaCore-Updater/2.2");
            string json = await http.GetStringAsync("https://api.github.com/repos/zentraxxdev/LuminaCore/releases/latest");
            var obj = JObject.Parse(json);
            string? tag = obj["tag_name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(tag) && !tag.Contains("2.2"))
                message = $"New release available: {tag}";
        }
        catch
        {
            message = "Running LuminaCore v2.2.0 (Offline / No newer release found)";
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (_webViewReady && WebView.CoreWebView2 is not null)
                WebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(new { type = "toast", message }));
        });
    }

    private void ApplyParamFromWeb(string? key, JToken? val)
    {
        if (string.IsNullOrEmpty(key) || val is null) return;
        switch (key)
        {
            case "PrimaryColorHex": _viewModel.PrimaryColorHex = val.ToString(); break;
            case "Speed": _viewModel.Speed = val.Value<double>(); break;
            case "Scale": _viewModel.Scale = val.Value<double>(); break;
            case "ReverseDirection": _viewModel.ReverseDirection = val.Value<bool>(); break;
            case "AnimateGradient": _viewModel.AnimateGradient = val.Value<bool>(); break;
            case "GradientAngleDegrees": _viewModel.GradientAngleDegrees = val.Value<double>(); break;
            case "AudioMode":
                if (Enum.TryParse<AudioVisualizerMode>(val.ToString(), true, out var am)) _viewModel.AudioMode = am;
                break;
            case "AudioSensitivity": _viewModel.AudioSensitivity = val.Value<double>(); break;
            case "CaptureMonitorIndex": _viewModel.CaptureMonitorIndex = val.Value<int>(); break;
            case "AmbilightSmoothing": _viewModel.AmbilightSmoothing = val.Value<double>(); break;
            case "AmbilightSaturation": _viewModel.AmbilightSaturation = val.Value<double>(); break;
            case "TelemetrySource":
                if (Enum.TryParse<TelemetrySource>(val.ToString(), true, out var ts)) _viewModel.TelemetrySource = ts;
                break;
            case "CoolTempC": _viewModel.CoolTempC = val.Value<double>(); break;
            case "WarmTempC": _viewModel.WarmTempC = val.Value<double>(); break;
            case "HotTempC": _viewModel.HotTempC = val.Value<double>(); break;
        }
    }

    private void ApplySettingFromWeb(string? key, JToken? val)
    {
        if (string.IsNullOrEmpty(key) || val is null) return;
        switch (key)
        {
            case "TargetFps": _viewModel.TargetFps = val.Value<int>(); break;
            case "StartWithWindows": _viewModel.StartWithWindows = val.Value<bool>(); break;
            case "StartMinimizedToTray": _viewModel.StartMinimizedToTray = val.Value<bool>(); break;
            case "MinimizeToTrayOnClose": _viewModel.MinimizeToTrayOnClose = val.Value<bool>(); break;
            case "EnableGlobalBlackoutHotkey": _viewModel.EnableGlobalBlackoutHotkey = val.Value<bool>(); break;
            case "EnableProcessWatcher": _viewModel.EnableProcessWatcher = val.Value<bool>(); break;
            case "EnableLocalWebRemote": _viewModel.EnableLocalWebRemote = val.Value<bool>(); break;
            case "EnableWledSync": _viewModel.EnableWledSync = val.Value<bool>(); break;
            case "WledIpAddress": _viewModel.WledIpAddress = val.ToString(); break;
            case "Language": _viewModel.Language = val.ToString(); break;
        }
    }

    private void PushFullStateToWeb()
    {
        if (!_webViewReady || WebView.CoreWebView2 is null) return;

        var hidDiag = _hardware.GetHidDiagnostics();
        var plugins = _pluginManager.GetLoadedPlugins();

        var payload = new
        {
            activeEffect = _viewModel.ActiveEffect.ToString(),
            activePluginId = _pipeline.ActivePluginId,
            isPaused = _pipeline.IsPaused,
            brightness = _viewModel.MasterBrightness,
            isBlackout = _viewModel.IsBlackout,
            isConnected = _viewModel.IsOpenRgbConnected,
            hidBadge = _viewModel.HidStatusBadge,
            hidStatus = _viewModel.OpenRgbStatusText,
            activeProfileId = _viewModel.ActiveProfile.Id,
            selectedProfileId = _viewModel.SelectedProfile?.Id ?? _viewModel.ActiveProfile.Id,
            dataPath = _viewModel.DataDirectoryPath,
            pluginsPath = _pluginManager.PluginsDirectory,
            hidDiagnostics = new
            {
                isConnected = hidDiag.IsConnected,
                controllerName = hidDiag.ControllerName,
                vidPid = hidDiag.VidPid,
                channelCount = hidDiag.ChannelCount,
                packetLatencyMs = hidDiag.PacketLatencyMs,
                hardwareFps = hidDiag.HardwareFps
            },
            plugins = plugins.Select(pl => new
            {
                id = pl.Id,
                name = pl.Name,
                author = pl.Author,
                description = pl.Description,
                accent = pl.AccentHex
            }),
            profiles = _viewModel.Profiles.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                effect = p.Effect.ToString(),
                processes = p.AssociatedProcesses
            }),
            zones = _viewModel.Zones.Select(z => new
            {
                zoneIndex = z.ZoneIndex,
                name = z.Name,
                ledCount = z.LedCount,
                deviceType = (int)z.DeviceType,
                deviceTypeLabel = z.DeviceTypeLabel,
                isEnabled = z.IsEnabled,
                isSynced = z.IsSynced,
                independentEffect = z.IndependentEffect.ToString()
            }),
            params_ = new
            {
                primaryColorHex = _viewModel.PrimaryColorHex,
                speed = _viewModel.Speed,
                scale = _viewModel.Scale,
                reverseDirection = _viewModel.ReverseDirection,
                animateGradient = _viewModel.AnimateGradient,
                gradientAngleDegrees = _viewModel.GradientAngleDegrees,
                audioMode = _viewModel.AudioMode.ToString(),
                audioSensitivity = _viewModel.AudioSensitivity,
                captureMonitorIndex = _viewModel.CaptureMonitorIndex,
                ambilightSmoothing = _viewModel.AmbilightSmoothing,
                ambilightSaturation = _viewModel.AmbilightSaturation,
                telemetrySource = _viewModel.TelemetrySource.ToString(),
                coolTempC = _viewModel.CoolTempC,
                warmTempC = _viewModel.WarmTempC,
                hotTempC = _viewModel.HotTempC
            },
            settings = new
            {
                targetFps = _viewModel.TargetFps,
                startWithWindows = _viewModel.StartWithWindows,
                startMinimizedToTray = _viewModel.StartMinimizedToTray,
                minimizeToTrayOnClose = _viewModel.MinimizeToTrayOnClose,
                enableGlobalBlackoutHotkey = _viewModel.EnableGlobalBlackoutHotkey,
                enableProcessWatcher = _viewModel.EnableProcessWatcher,
                enableLocalWebRemote = _viewModel.EnableLocalWebRemote,
                enableWledSync = _viewModel.EnableWledSync,
                wledIpAddress = _viewModel.WledIpAddress,
                language = _viewModel.Language
            }
        };

        string json = JsonConvert.SerializeObject(new { type = "state", payload }).Replace("\"params_\":", "\"params\":");
        WebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void LiveTickTimer_Tick(object? sender, EventArgs e)
    {
        if (!_webViewReady || WebView.CoreWebView2 is null || !IsVisible || WindowState == WindowState.Minimized)
            return;

        _pipeline.CopyPreviewData(_pixelCopyBuffer, _zoneColorCopy, _audioCopyBuffer, out var diagnostics);
        _viewModel.RefreshLiveTelemetryAndDiagnostics(diagnostics, _audioCopyBuffer);

        // Sample 16 horizontal color stops into pre-allocated scratch array (zero per-tick array allocation)
        const int stops = 16;
        int midY = CanvasSurface.DefaultHeight / 2;
        for (int i = 0; i < stops; i++)
        {
            int x = i * (CanvasSurface.DefaultWidth - 1) / (stops - 1);
            int idx = (midY * CanvasSurface.DefaultWidth + x) * 4;
            _bannerStripScratch[i] = $"#{_pixelCopyBuffer[idx + 2]:X2}{_pixelCopyBuffer[idx + 1]:X2}{_pixelCopyBuffer[idx]:X2}";
        }

        // Sample 8 color stops per channel for live mini LED strips
        var zoneStrips = new Dictionary<int, string[]>();
        foreach (var kvp in _zoneColorCopy)
        {
            var arr = kvp.Value;
            if (arr.Length == 0) continue;
            string[] mini = new string[8];
            for (int i = 0; i < 8; i++)
            {
                int src = i * (arr.Length - 1) / 7;
                mini[i] = arr[src].ToHex();
            }
            zoneStrips[kvp.Key] = mini;
        }

        var hidDiag = _hardware.GetHidDiagnostics();
        var tick = new
        {
            cpuTemp = _viewModel.CpuTempText,
            gpuTemp = _viewModel.GpuTempText,
            cpuLoad = _viewModel.CpuLoadPercent,
            gpuLoad = _viewModel.GpuLoadPercent,
            cpuLoadText = _viewModel.CpuLoadText,
            gpuLoadText = _viewModel.GpuLoadText,
            fanRpm = _viewModel.SystemFanRpmText,
            cpuName = _viewModel.CpuName,
            gpuName = _viewModel.GpuName,
            hidBadge = _viewModel.HidStatusBadge,
            hidLatencyMs = hidDiag.PacketLatencyMs,
            hidHwFps = hidDiag.HardwareFps,
            bannerStrip = _bannerStripScratch,
            zoneStrips,
            audioBars = new[]
            {
                _viewModel.AudioBar0, _viewModel.AudioBar1, _viewModel.AudioBar2, _viewModel.AudioBar3,
                _viewModel.AudioBar4, _viewModel.AudioBar5, _viewModel.AudioBar6, _viewModel.AudioBar7
            },
            cpuHistory = _historyDirty ? _latestCpuHistory : null,
            gpuHistory = _historyDirty ? _latestGpuHistory : null
        };
        _historyDirty = false;

        WebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(new { type = "tick", payload = tick }));
    }

    private void RegisterGlobalHotkeyIfEnabled()
    {
        if (_viewModel.EnableGlobalBlackoutHotkey) _hotkeyService.Register(this);
        else _hotkeyService.Unregister();
    }

    private void OnHotkeySettingChanged(bool enabled)
    {
        if (enabled) _hotkeyService.Register(this);
        else _hotkeyService.Unregister();
    }

    private void UpdateTrayIconForBlackout(bool isBlackout)
    {
        TrayIcon.Icon = isBlackout ? _blackoutTrayIcon : _activeTrayIcon;
        TrayIcon.ToolTipText = isBlackout
            ? "LuminaCore — STEALTH BLACKOUT ACTIVE (Ctrl+Alt+L)"
            : "LuminaCore ARGB Engine (60 FPS) — Ctrl+Alt+L";
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        bool activeUi = IsVisible && WindowState != WindowState.Minimized;
        _pipeline.UiPreviewEnabled = activeUi;
        if (activeUi) _liveTickTimer.Start();
        else _liveTickTimer.Stop();
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool activeUi = IsVisible && WindowState != WindowState.Minimized;
        _pipeline.UiPreviewEnabled = activeUi;
        if (activeUi) _liveTickTimer.Start();
        else _liveTickTimer.Stop();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting && _viewModel.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            _viewModel.SaveAllState();
            _liveTickTimer.Stop();
            _pipeline.UiPreviewEnabled = false;
            Hide();
            _ = WebView?.CoreWebView2?.TrySuspendAsync();
            TrimWorkingSetForTray();
            return;
        }

        CleanupBeforeExit();
    }

    private void CleanupBeforeExit()
    {
        _liveTickTimer.Stop();
        _hotkeyService.Unregister();
        TrayIcon.Dispose();
        _activeTrayIcon?.Dispose();
        _blackoutTrayIcon?.Dispose();
        _activeTrayIcon = null;
        _blackoutTrayIcon = null;
    }

    public void RestoreWindowFromExternalSignal()
    {
        RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        _pipeline.UiPreviewEnabled = true;
        _liveTickTimer.Start();
        PushFullStateToWeb();
    }

    private void TrayIcon_OnDoubleClick(object sender, RoutedEventArgs e) => RestoreFromTray();
    private void TrayOpen_Click(object sender, RoutedEventArgs e) => RestoreFromTray();

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        _viewModel.SaveAllState();
        CleanupBeforeExit();
        ((App)Application.Current).ExitApplication();
    }
}
