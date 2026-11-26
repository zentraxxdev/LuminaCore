using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LuminaCore.Core.Engine;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;
using LuminaCore.UI.Infrastructure;
using LuminaCore.UI.Services;

namespace LuminaCore.UI.ViewModels;

public enum NavSection
{
    Dashboard,
    Effects,
    CaseLayout,
    GameProfiles,
    FanCurves,
    Settings
}

public sealed class EffectCardItem : ObservableObject
{
    private bool _isSelected;
    public EffectType EffectType { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string AccentHex { get; init; } = "#00E5FF";
    public string Badge { get; init; } = string.Empty;
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public Brush PreviewBrush { get; init; } = Brushes.Cyan;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public static Brush CreatePreviewBrush(EffectType type)
    {
        var grad = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        switch (type)
        {
            case EffectType.Solid:
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 180, 216), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 229, 255), 1));
                break;
            case EffectType.Gradient:
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 229, 255), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(139, 92, 246), 0.5));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(236, 72, 153), 1));
                break;
            case EffectType.RainbowWave:
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(255, 59, 92), 0.0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(255, 190, 11), 0.25));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 245, 160), 0.5));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 217, 245), 0.75));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(168, 85, 247), 1.0));
                break;
            case EffectType.RainbowSpiral:
                grad.StartPoint = new Point(0, 0);
                grad.EndPoint = new Point(1, 1);
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(255, 46, 147), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(123, 97, 255), 0.45));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 240, 255), 0.85));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 255, 156), 1));
                break;
            case EffectType.ScreenAmbilight:
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(14, 165, 233), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(56, 189, 248), 0.4));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(249, 115, 22), 1));
                break;
            case EffectType.AudioVisualizer:
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 229, 255), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(139, 92, 246), 0.45));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(255, 94, 0), 1));
                break;
            case EffectType.HardwareTelemetry:
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(215, 244, 255), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(255, 174, 0), 0.55));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(255, 24, 58), 1));
                break;
            case EffectType.Breathing:
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(24, 18, 48), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(139, 92, 246), 0.5));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(24, 18, 48), 1));
                break;
            case EffectType.Plasma:
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 229, 255), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(139, 92, 246), 0.4));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(236, 72, 153), 0.8));
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 229, 255), 1));
                break;
        }
        grad.Freeze();
        return grad;
    }
}

public sealed class GradientStopViewModel : ObservableObject
{
    private double _position;
    private string _colorHex = "#00F0FF";
    private readonly Action _onChanged;

    public GradientStopViewModel(GradientStopConfig model, Action onChanged)
    {
        _position = model.Position;
        _colorHex = model.ColorHex;
        _onChanged = onChanged;
    }

    public double Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, Math.Clamp(value, 0.0, 1.0)))
                _onChanged();
        }
    }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (SetProperty(ref _colorHex, value))
                _onChanged();
        }
    }

    public GradientStopConfig ToModel() => new(_position, _colorHex);
}

public sealed class ZoneItemViewModel : ObservableObject
{
    private readonly ZoneLayout _model;
    private readonly Action _onChanged;
    private IReadOnlyList<RgbColor>? _liveColors;

    public ZoneItemViewModel(ZoneLayout model, Action onChanged)
    {
        _model = model.Clone();
        _onChanged = onChanged;
        CycleDeviceTypeCommand = new RelayCommand(() =>
        {
            var next = (ZoneDeviceType)(((int)DeviceType + 1) % 4);
            DeviceType = next;
        });
        ToggleSyncCommand = new RelayCommand(() => IsSynced = !IsSynced);
    }

    public ICommand CycleDeviceTypeCommand { get; }
    public ICommand ToggleSyncCommand { get; }

    public int ZoneIndex => _model.ZoneIndex;
    public string AccentHex => _model.AccentHex;

    public IReadOnlyList<RgbColor>? LiveColors
    {
        get => _liveColors;
        set => SetProperty(ref _liveColors, value);
    }

    public ZoneDeviceType DeviceType
    {
        get => _model.DeviceType;
        set
        {
            if (_model.DeviceType != value)
            {
                _model.DeviceType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DeviceTypeLabel));
                OnPropertyChanged(nameof(DeviceIconSymbol));
                _onChanged();
            }
        }
    }

    public string DeviceTypeLabel => DeviceType switch
    {
        ZoneDeviceType.Fan => "Fan",
        ZoneDeviceType.LedStrip => "LED Strip",
        ZoneDeviceType.Pump => "AIO Pump",
        ZoneDeviceType.Motherboard => "Motherboard",
        _ => "Channel"
    };

    public Wpf.Ui.Controls.SymbolRegular DeviceIconSymbol => DeviceType switch
    {
        ZoneDeviceType.Fan => Wpf.Ui.Controls.SymbolRegular.WeatherBlowingSnow24,
        ZoneDeviceType.LedStrip => Wpf.Ui.Controls.SymbolRegular.Lightbulb24,
        ZoneDeviceType.Pump => Wpf.Ui.Controls.SymbolRegular.Drop24,
        ZoneDeviceType.Motherboard => Wpf.Ui.Controls.SymbolRegular.DeveloperBoard24,
        _ => Wpf.Ui.Controls.SymbolRegular.Lightbulb24
    };

    public bool IsSynced
    {
        get => _model.IsSynced;
        set
        {
            if (_model.IsSynced != value)
            {
                _model.IsSynced = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SyncModeLabel));
                _onChanged();
            }
        }
    }

    public string SyncModeLabel => IsSynced ? "Sync" : "Solo";

    public EffectType IndependentEffect
    {
        get => _model.IndependentEffect;
        set
        {
            if (_model.IndependentEffect != value)
            {
                _model.IndependentEffect = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public string IndependentColorHex
    {
        get => _model.IndependentColorHex;
        set
        {
            if (_model.IndependentColorHex != value)
            {
                _model.IndependentColorHex = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public string Name
    {
        get => _model.Name;
        set { if (_model.Name != value) { _model.Name = value; OnPropertyChanged(); _onChanged(); } }
    }

    public string HardwareDescription
    {
        get => _model.HardwareDescription;
        set { if (_model.HardwareDescription != value) { _model.HardwareDescription = value; OnPropertyChanged(); _onChanged(); } }
    }

    public int LedCount
    {
        get => _model.LedCount;
        set
        {
            int clamped = Math.Clamp(value, 1, 120);
            if (_model.LedCount != clamped) { _model.LedCount = clamped; OnPropertyChanged(); _onChanged(); }
        }
    }

    public double X
    {
        get => _model.X;
        set
        {
            double clamped = Math.Round(Math.Clamp(value, 0.05, 0.95), 3);
            if (Math.Abs(_model.X - clamped) > 0.0005) { _model.X = clamped; OnPropertyChanged(); _onChanged(); }
        }
    }

    public double Y
    {
        get => _model.Y;
        set
        {
            double clamped = Math.Round(Math.Clamp(value, 0.05, 0.95), 3);
            if (Math.Abs(_model.Y - clamped) > 0.0005) { _model.Y = clamped; OnPropertyChanged(); _onChanged(); }
        }
    }

    public double Width
    {
        get => _model.Width;
        set
        {
            double clamped = Math.Round(Math.Clamp(value, 0.08, 0.94), 3);
            if (Math.Abs(_model.Width - clamped) > 0.0005) { _model.Width = clamped; OnPropertyChanged(); _onChanged(); }
        }
    }

    public double Height
    {
        get => _model.Height;
        set
        {
            double clamped = Math.Round(Math.Clamp(value, 0.07, 0.90), 3);
            if (Math.Abs(_model.Height - clamped) > 0.0005) { _model.Height = clamped; OnPropertyChanged(); _onChanged(); }
        }
    }

    public double RotationDegrees
    {
        get => _model.RotationDegrees;
        set
        {
            double clamped = Math.Round(Math.Clamp(value, -180.0, 180.0), 1);
            if (Math.Abs(_model.RotationDegrees - clamped) > 0.05) { _model.RotationDegrees = clamped; OnPropertyChanged(); _onChanged(); }
        }
    }

    public ZoneLedArrangement Arrangement
    {
        get => _model.Arrangement;
        set { if (_model.Arrangement != value) { _model.Arrangement = value; OnPropertyChanged(); _onChanged(); } }
    }

    public bool IsEnabled
    {
        get => _model.IsEnabled;
        set { if (_model.IsEnabled != value) { _model.IsEnabled = value; OnPropertyChanged(); _onChanged(); } }
    }

    public void SyncFromCanvas(ZoneLayout updated)
    {
        _model.X = Math.Round(updated.X, 3);
        _model.Y = Math.Round(updated.Y, 3);
        _model.Width = Math.Round(updated.Width, 3);
        _model.Height = Math.Round(updated.Height, 3);
        _model.RotationDegrees = Math.Round(updated.RotationDegrees, 1);
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(RotationDegrees));
    }

    public ZoneLayout ToModel() => _model.Clone();
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly IHardwareController _hardware;
    private readonly IScreenCaptureService _screenCapture;
    private readonly IAudioCaptureService _audioCapture;
    private readonly ITelemetryService _telemetry;
    private readonly IProfileRepository _repository;
    private readonly IProcessWatcherService _processWatcher;
    private readonly WindowsStartupService _startupService;
    private readonly LocalWebRemoteService _webRemote;

    private CancellationTokenSource? _reconnectCts;
    private bool _suppressPipelineSync;
    private string _manualProfileIdBeforeGame = string.Empty;

    public event Action? ZonesStructureChanged;
    public event Action<bool>? BlackoutStateChanged;
    public event Action<bool>? HotkeySettingChanged;
    public event Action<IReadOnlyList<double>, IReadOnlyList<double>>? TemperatureHistoryUpdated;

    private readonly Queue<double> _cpuHistory = new(64);
    private readonly Queue<double> _gpuHistory = new(64);
    private long _lastHistoryPushMs;

    public MainViewModel(
        RenderPipeline pipeline,
        IHardwareController hardware,
        IScreenCaptureService screenCapture,
        IAudioCaptureService audioCapture,
        ITelemetryService telemetry,
        IProfileRepository repository,
        IProcessWatcherService processWatcher,
        WindowsStartupService startupService,
        LocalWebRemoteService webRemote)
    {
        _pipeline = pipeline;
        _hardware = hardware;
        _screenCapture = screenCapture;
        _audioCapture = audioCapture;
        _telemetry = telemetry;
        _repository = repository;
        _processWatcher = processWatcher;
        _startupService = startupService;
        _webRemote = webRemote;

        Settings = _repository.LoadSettings();
        Settings.StartWithWindows = _startupService.IsEnabledInRegistry() || Settings.StartWithWindows;

        var loadedProfiles = _repository.LoadProfiles();
        Profiles = new ObservableCollection<LightingProfile>(loadedProfiles);

        EffectCards = new ObservableCollection<EffectCardItem>(
            _pipeline.AvailableEffects
                .OrderBy(e => (int)e.Type)
                .Select(e => new EffectCardItem
                {
                    EffectType = e.Type,
                    DisplayName = e.DisplayName,
                    Subtitle = e.Subtitle,
                    AccentHex = e.AccentHex,
                    PreviewBrush = EffectCardItem.CreatePreviewBrush(e.Type),
                    Badge = e.Type switch
                    {
                        EffectType.ScreenAmbilight => "DXGI",
                        EffectType.AudioVisualizer => "AUDIO",
                        EffectType.HardwareTelemetry => "THERMAL",
                        _ => string.Empty
                    }
                }));

        // Wire commands
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is NavSection section) CurrentSection = section;
            else if (p is string s && Enum.TryParse<NavSection>(s, out var parsed)) CurrentSection = parsed;
        });

        SelectEffectCommand = new RelayCommand(p =>
        {
            if (p is EffectType et) ActiveEffect = et;
            else if (p is EffectCardItem card) ActiveEffect = card.EffectType;
        });

        ToggleBlackoutCommand = new RelayCommand(() => IsBlackout = !IsBlackout);
        ReconnectOpenRgbCommand = new RelayCommand(async () => await ConnectHardwareAsync());
        DisconnectOpenRgbCommand = new RelayCommand(() => _hardware.Disconnect());
        ResetLayoutCommand = new RelayCommand(ResetZonesToDefault);
        AddZoneCommand = new RelayCommand(AddCustomZone);
        RemoveZoneCommand = new RelayCommand(RemoveSelectedZone);
        SyncZonesFromHardwareCommand = new RelayCommand(SyncZonesFromOpenRgbDevice);
        ApplyCasePresetCommand = new RelayCommand(p => ApplyCasePreset(p as string));
        SetDirectionCommand = new RelayCommand(p =>
        {
            if (p is string dir) ReverseDirection = dir.Equals("Reverse", StringComparison.OrdinalIgnoreCase);
        });
        ApplyQuickPaletteCommand = new RelayCommand(p => ApplyQuickPalette(p as string));
        SyncAllChannelsCommand = new RelayCommand(() =>
        {
            foreach (var z in Zones) z.IsSynced = true;
            PushZonesToPipeline();
        });
        ExportProfilesCommand = new RelayCommand(ExportProfilesToDesktop);

        // Seed 60s history chart with smooth initial baseline
        for (int i = 0; i < 60; i++)
        {
            _cpuHistory.Enqueue(42.0);
            _gpuHistory.Enqueue(44.0);
        }

        SetPrimarySwatchCommand = new RelayCommand(p =>
        {
            if (p is string hex) PrimaryColorHex = hex;
        });

        AddGradientStopCommand = new RelayCommand(AddGradientStop);
        RemoveGradientStopCommand = new RelayCommand(p =>
        {
            if (p is GradientStopViewModel stop && GradientStops.Count > 2)
            {
                GradientStops.Remove(stop);
                PushParametersToPipeline();
            }
        });

        ApplyProfileCommand = new RelayCommand(p =>
        {
            if (p is LightingProfile prof) ActivateProfile(prof, isAutoSwitch: false);
        });
        CreateProfileCommand = new RelayCommand(CreateNewProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateSelectedProfile);
        DeleteProfileCommand = new RelayCommand(DeleteSelectedProfile);
        SaveProfilesCommand = new RelayCommand(SaveAllState);
        AddProcessBindingCommand = new RelayCommand(AddProcessBinding);
        RemoveProcessBindingCommand = new RelayCommand(p =>
        {
            if (p is string proc && SelectedProfile is not null)
            {
                SelectedProfile.AssociatedProcesses.Remove(proc);
                RefreshSelectedProfileProcesses();
                SaveAllState();
                _processWatcher.UpdateProfiles(Profiles.ToList());
            }
        });
        AddPresetProcessCommand = new RelayCommand(p =>
        {
            if (p is string exe)
            {
                NewProcessExecutable = exe;
                AddProcessBinding();
            }
        });
        OpenDataFolderCommand = new RelayCommand(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", _repository.DataDirectory) { UseShellExecute = true });
            }
            catch { }
        });

        // Subscribe to hardware & process watcher events
        _hardware.ConnectionStateChanged += (_, state) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() => ApplyHardwareState(state));
        };

        _processWatcher.ProcessMatchChanged += (_, args) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() => HandleProcessWatcherMatch(args));
        };

        // Load initial active profile
        var initialProfile = Profiles.FirstOrDefault(p => p.Id == Settings.ActiveProfileId)
                             ?? Profiles.FirstOrDefault(p => p.IsDefault)
                             ?? Profiles[0];
        _manualProfileIdBeforeGame = initialProfile.Id;
        ActivateProfile(initialProfile, isAutoSwitch: false);
        SelectedProfile = initialProfile;

        // Start core background engines
        _pipeline.TargetFps = Settings.TargetFps;
        _telemetry.Start(Settings.TelemetryPollIntervalMs);
        _pipeline.Start();

        _processWatcher.UpdateProfiles(Profiles.ToList());
        if (Settings.EnableProcessWatcher)
            _processWatcher.Start();

        _webRemote.Configure(Settings.EnableLocalWebRemote, Settings.LocalWebRemotePort);
        if (_hardware is LuminaCore.Hardware.SmartHardwareController smart)
            smart.ConfigureWled(Settings.EnableWledSync, Settings.WledIpAddress);

        StartOpenRgbAutoReconnectLoop();
    }

    // --- Navigation & Collections ---
    private NavSection _currentSection = NavSection.Dashboard;
    public NavSection CurrentSection
    {
        get => _currentSection;
        set => SetProperty(ref _currentSection, value);
    }

    public AppSettings Settings { get; }
    public ObservableCollection<LightingProfile> Profiles { get; }
    public ObservableCollection<EffectCardItem> EffectCards { get; }
    public ObservableCollection<ZoneItemViewModel> Zones { get; } = [];
    public ObservableCollection<GradientStopViewModel> GradientStops { get; } = [];
    public ObservableCollection<HardwareDeviceInfo> DiscoveredDevices { get; } = [];
    public ObservableCollection<HardwareZoneInfo> ActiveDeviceZones { get; } = [];
    public ObservableCollection<string> SelectedProfileProcesses { get; } = [];

    public Array AudioModes => Enum.GetValues(typeof(AudioVisualizerMode));
    public Array TelemetrySources => Enum.GetValues(typeof(TelemetrySource));
    public Array ZoneArrangements => Enum.GetValues(typeof(ZoneLedArrangement));
    public EffectType[] IndependentEffects { get; } = [EffectType.Solid, EffectType.RainbowWave, EffectType.Breathing, EffectType.Plasma];
    public string[] AvailableMonitors { get; } = ["Primary Monitor", "Display 2", "Display 3"];
    public int[] AvailableFpsOptions { get; } = [30, 60];
    public string[] QuickColorSwatches { get; } = ["#00E5FF", "#8B5CF6", "#EC4899", "#00FF9C", "#FFAE00", "#FF2D55", "#FFFFFF", "#38BDF8"];

    // --- Active Profile & Effect State ---
    private LightingProfile _activeProfile = null!;
    public LightingProfile ActiveProfile
    {
        get => _activeProfile;
        private set => SetProperty(ref _activeProfile, value);
    }

    private LightingProfile? _selectedProfile;
    public LightingProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
                RefreshSelectedProfileProcesses();
        }
    }

    private int _selectedZoneIndex;
    public int SelectedZoneIndex
    {
        get => _selectedZoneIndex;
        set
        {
            if (SetProperty(ref _selectedZoneIndex, value))
                OnPropertyChanged(nameof(SelectedZone));
        }
    }

    public ZoneItemViewModel? SelectedZone => Zones.FirstOrDefault(z => z.ZoneIndex == SelectedZoneIndex) ?? Zones.FirstOrDefault();

    private bool _snapToGrid;
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => SetProperty(ref _snapToGrid, value);
    }

    private bool _showLayoutGrid = true;
    public bool ShowLayoutGrid
    {
        get => _showLayoutGrid;
        set => SetProperty(ref _showLayoutGrid, value);
    }

    private bool _showChassisOverlay;
    public bool ShowChassisOverlay
    {
        get => _showChassisOverlay;
        set => SetProperty(ref _showChassisOverlay, value);
    }

    private string _activeHardwareSummary = "Auto-Detecting Hardware...";
    public string ActiveHardwareSummary
    {
        get => _activeHardwareSummary;
        private set => SetProperty(ref _activeHardwareSummary, value);
    }

    private EffectType _activeEffect;
    public EffectType ActiveEffect
    {
        get => _activeEffect;
        set
        {
            if (SetProperty(ref _activeEffect, value))
            {
                foreach (var card in EffectCards)
                    card.IsSelected = card.EffectType == value;

                if (!_suppressPipelineSync)
                {
                    _activeProfile.Effect = value;
                    _pipeline.UpdateEffect(value);
                    SaveProfilesQuietly();
                }

                OnPropertyChanged(nameof(ActiveEffectDisplayName));
            }
        }
    }

    public string ActiveEffectDisplayName =>
        EffectCards.FirstOrDefault(c => c.EffectType == ActiveEffect)?.DisplayName ?? ActiveEffect.ToString();

    private double _masterBrightness = 0.85;
    public double MasterBrightness
    {
        get => _masterBrightness;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (SetProperty(ref _masterBrightness, clamped) && !_suppressPipelineSync)
            {
                _activeProfile.MasterBrightness = clamped;
                _pipeline.UpdateMasterBrightness(clamped);
            }
        }
    }

    private bool _isBlackout;
    public bool IsBlackout
    {
        get => _isBlackout;
        set
        {
            if (SetProperty(ref _isBlackout, value))
            {
                _pipeline.IsBlackout = value;
                BlackoutStateChanged?.Invoke(value);
                OnPropertyChanged(nameof(BlackoutButtonLabel));
            }
        }
    }

    public string BlackoutButtonLabel => IsBlackout ? "LIGHTS OFF (STEALTH)" : "LIGHTS ON";

    // --- Live Effect Parameter Bindings ---
    private string _primaryColorHex = "#00F0FF";
    public string PrimaryColorHex
    {
        get => _primaryColorHex;
        set
        {
            if (SetProperty(ref _primaryColorHex, value))
            {
                var c = RgbColor.FromHex(value);
                _colorR = c.R; _colorG = c.G; _colorB = c.B;
                OnPropertyChanged(nameof(ColorR));
                OnPropertyChanged(nameof(ColorG));
                OnPropertyChanged(nameof(ColorB));
                PushParametersToPipeline();
            }
        }
    }

    private byte _colorR, _colorG = 240, _colorB = 255;
    public byte ColorR
    {
        get => _colorR;
        set { if (SetProperty(ref _colorR, value)) SyncHexFromRgb(); }
    }
    public byte ColorG
    {
        get => _colorG;
        set { if (SetProperty(ref _colorG, value)) SyncHexFromRgb(); }
    }
    public byte ColorB
    {
        get => _colorB;
        set { if (SetProperty(ref _colorB, value)) SyncHexFromRgb(); }
    }

    private void SyncHexFromRgb()
    {
        string hex = new RgbColor(_colorR, _colorG, _colorB).ToHex();
        if (_primaryColorHex != hex)
        {
            _primaryColorHex = hex;
            OnPropertyChanged(nameof(PrimaryColorHex));
            PushParametersToPipeline();
        }
    }

    private double _speed = 1.0;
    public double Speed { get => _speed; set { if (SetProperty(ref _speed, value)) PushParametersToPipeline(); } }

    private double _scale = 1.0;
    public double Scale { get => _scale; set { if (SetProperty(ref _scale, value)) PushParametersToPipeline(); } }

    private double _directionDegrees;
    public double DirectionDegrees { get => _directionDegrees; set { if (SetProperty(ref _directionDegrees, value)) PushParametersToPipeline(); } }

    private bool _reverseDirection;
    public bool ReverseDirection { get => _reverseDirection; set { if (SetProperty(ref _reverseDirection, value)) PushParametersToPipeline(); } }

    private double _gradientAngleDegrees;
    public double GradientAngleDegrees { get => _gradientAngleDegrees; set { if (SetProperty(ref _gradientAngleDegrees, value)) PushParametersToPipeline(); } }

    private bool _animateGradient = true;
    public bool AnimateGradient { get => _animateGradient; set { if (SetProperty(ref _animateGradient, value)) PushParametersToPipeline(); } }

    private double _ambilightSmoothing = 0.28;
    public double AmbilightSmoothing { get => _ambilightSmoothing; set { if (SetProperty(ref _ambilightSmoothing, value)) PushParametersToPipeline(); } }

    private double _ambilightSaturation = 1.35;
    public double AmbilightSaturation { get => _ambilightSaturation; set { if (SetProperty(ref _ambilightSaturation, value)) PushParametersToPipeline(); } }

    private double _ambilightEdgeBias = 0.4;
    public double AmbilightEdgeBias { get => _ambilightEdgeBias; set { if (SetProperty(ref _ambilightEdgeBias, value)) PushParametersToPipeline(); } }

    private int _captureMonitorIndex;
    public int CaptureMonitorIndex { get => _captureMonitorIndex; set { if (SetProperty(ref _captureMonitorIndex, value)) PushParametersToPipeline(); } }

    // 8-band live audio bars for the Audio Reactive inspector
    private double _audioBar0, _audioBar1, _audioBar2, _audioBar3, _audioBar4, _audioBar5, _audioBar6, _audioBar7;
    public double AudioBar0 { get => _audioBar0; private set => SetProperty(ref _audioBar0, value); }
    public double AudioBar1 { get => _audioBar1; private set => SetProperty(ref _audioBar1, value); }
    public double AudioBar2 { get => _audioBar2; private set => SetProperty(ref _audioBar2, value); }
    public double AudioBar3 { get => _audioBar3; private set => SetProperty(ref _audioBar3, value); }
    public double AudioBar4 { get => _audioBar4; private set => SetProperty(ref _audioBar4, value); }
    public double AudioBar5 { get => _audioBar5; private set => SetProperty(ref _audioBar5, value); }
    public double AudioBar6 { get => _audioBar6; private set => SetProperty(ref _audioBar6, value); }
    public double AudioBar7 { get => _audioBar7; private set => SetProperty(ref _audioBar7, value); }

    public string HidStatusBadge => IsOpenRgbConnected
        ? $"USB HID Connected • {FpsText}"
        : $"USB HID Offline • {FpsText}";

    private AudioVisualizerMode _audioMode = AudioVisualizerMode.SpectrumBars;
    public AudioVisualizerMode AudioMode { get => _audioMode; set { if (SetProperty(ref _audioMode, value)) PushParametersToPipeline(); } }

    private double _audioSensitivity = 1.6;
    public double AudioSensitivity { get => _audioSensitivity; set { if (SetProperty(ref _audioSensitivity, value)) PushParametersToPipeline(); } }

    private string _audioLowColorHex = "#00F0FF";
    public string AudioLowColorHex { get => _audioLowColorHex; set { if (SetProperty(ref _audioLowColorHex, value)) PushParametersToPipeline(); } }

    private string _audioMidColorHex = "#7B61FF";
    public string AudioMidColorHex { get => _audioMidColorHex; set { if (SetProperty(ref _audioMidColorHex, value)) PushParametersToPipeline(); } }

    private string _audioHighColorHex = "#FF2E93";
    public string AudioHighColorHex { get => _audioHighColorHex; set { if (SetProperty(ref _audioHighColorHex, value)) PushParametersToPipeline(); } }

    private TelemetrySource _telemetrySource = TelemetrySource.Cpu;
    public TelemetrySource TelemetrySource { get => _telemetrySource; set { if (SetProperty(ref _telemetrySource, value)) PushParametersToPipeline(); } }

    private double _coolTempC = 40;
    public double CoolTempC { get => _coolTempC; set { if (SetProperty(ref _coolTempC, value)) PushParametersToPipeline(); } }

    private double _warmTempC = 65;
    public double WarmTempC { get => _warmTempC; set { if (SetProperty(ref _warmTempC, value)) PushParametersToPipeline(); } }

    private double _hotTempC = 80;
    public double HotTempC { get => _hotTempC; set { if (SetProperty(ref _hotTempC, value)) PushParametersToPipeline(); } }

    private string _coolColorHex = "#D7F4FF";
    public string CoolColorHex { get => _coolColorHex; set { if (SetProperty(ref _coolColorHex, value)) PushParametersToPipeline(); } }

    private string _warmColorHex = "#FFAE00";
    public string WarmColorHex { get => _warmColorHex; set { if (SetProperty(ref _warmColorHex, value)) PushParametersToPipeline(); } }

    private string _hotColorHex = "#FF183A";
    public string HotColorHex { get => _hotColorHex; set { if (SetProperty(ref _hotColorHex, value)) PushParametersToPipeline(); } }

    private bool _telemetryPulseAtHot = true;
    public bool TelemetryPulseAtHot { get => _telemetryPulseAtHot; set { if (SetProperty(ref _telemetryPulseAtHot, value)) PushParametersToPipeline(); } }

    // --- Live Telemetry, Audio & Diagnostics ---
    private string _fpsText = "60.0 FPS";
    public string FpsText { get => _fpsText; private set => SetProperty(ref _fpsText, value); }

    private string _frameMsText = "0.2 ms";
    public string FrameMsText { get => _frameMsText; private set => SetProperty(ref _frameMsText, value); }

    private string _memoryText = "32 MB";
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }

    private string _cpuTempText = "42°C";
    public string CpuTempText { get => _cpuTempText; private set => SetProperty(ref _cpuTempText, value); }

    private string _gpuTempText = "39°C";
    public string GpuTempText { get => _gpuTempText; private set => SetProperty(ref _gpuTempText, value); }

    private double _cpuLoadPercent = 14;
    public double CpuLoadPercent { get => _cpuLoadPercent; private set => SetProperty(ref _cpuLoadPercent, value); }

    private double _gpuLoadPercent = 8;
    public double GpuLoadPercent { get => _gpuLoadPercent; private set => SetProperty(ref _gpuLoadPercent, value); }

    private string _cpuLoadText = "14%";
    public string CpuLoadText { get => _cpuLoadText; private set => SetProperty(ref _cpuLoadText, value); }

    private string _gpuLoadText = "8%";
    public string GpuLoadText { get => _gpuLoadText; private set => SetProperty(ref _gpuLoadText, value); }

    private string _systemFanRpmText = "1,120 RPM";
    public string SystemFanRpmText { get => _systemFanRpmText; private set => SetProperty(ref _systemFanRpmText, value); }

    private string _exportStatusText = string.Empty;
    public string ExportStatusText { get => _exportStatusText; private set => SetProperty(ref _exportStatusText, value); }

    public string AppVersionText => "LuminaCore v2.0 • .NET 8 • Multi-Vendor HID + WebView2";

    public bool EnableLocalWebRemote
    {
        get => Settings.EnableLocalWebRemote;
        set
        {
            Settings.EnableLocalWebRemote = value;
            _webRemote.Configure(value, Settings.LocalWebRemotePort);
            OnPropertyChanged();
            _repository.SaveSettings(Settings);
        }
    }

    public int LocalWebRemotePort
    {
        get => Settings.LocalWebRemotePort;
        set
        {
            Settings.LocalWebRemotePort = Math.Clamp(value, 1024, 65535);
            _webRemote.Configure(Settings.EnableLocalWebRemote, Settings.LocalWebRemotePort);
            OnPropertyChanged();
            _repository.SaveSettings(Settings);
        }
    }

    public bool EnableWledSync
    {
        get => Settings.EnableWledSync;
        set
        {
            Settings.EnableWledSync = value;
            if (_hardware is LuminaCore.Hardware.SmartHardwareController smart)
                smart.ConfigureWled(value, Settings.WledIpAddress);
            OnPropertyChanged();
            _repository.SaveSettings(Settings);
        }
    }

    public string WledIpAddress
    {
        get => Settings.WledIpAddress;
        set
        {
            Settings.WledIpAddress = value;
            if (_hardware is LuminaCore.Hardware.SmartHardwareController smart)
                smart.ConfigureWled(Settings.EnableWledSync, value);
            OnPropertyChanged();
            _repository.SaveSettings(Settings);
        }
    }

    public string Language
    {
        get => Settings.Language;
        set { Settings.Language = value; OnPropertyChanged(); _repository.SaveSettings(Settings); }
    }

    private string _cpuName = "AMD Ryzen 5 7600X";
    public string CpuName { get => _cpuName; private set => SetProperty(ref _cpuName, value); }

    private string _gpuName = "AMD Radeon RX 9060 XT";
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }

    private string _telemetryStatusText = "Sensors initializing...";
    public string TelemetryStatusText { get => _telemetryStatusText; private set => SetProperty(ref _telemetryStatusText, value); }

    private string _captureStatusText = "DXGI Ready";
    public string CaptureStatusText { get => _captureStatusText; private set => SetProperty(ref _captureStatusText, value); }

    private string _audioStatusText = "WASAPI Ready";
    public string AudioStatusText { get => _audioStatusText; private set => SetProperty(ref _audioStatusText, value); }

    private double _audioBassLevel;
    public double AudioBassLevel { get => _audioBassLevel; private set => SetProperty(ref _audioBassLevel, value); }

    private double _audioMidLevel;
    public double AudioMidLevel { get => _audioMidLevel; private set => SetProperty(ref _audioMidLevel, value); }

    private double _audioTrebleLevel;
    public double AudioTrebleLevel { get => _audioTrebleLevel; private set => SetProperty(ref _audioTrebleLevel, value); }

    private bool _isOpenRgbConnected;
    public bool IsOpenRgbConnected { get => _isOpenRgbConnected; private set => SetProperty(ref _isOpenRgbConnected, value); }

    private string _openRgbStatusText = "Connecting to OpenRGB (127.0.0.1:6742)...";
    public string OpenRgbStatusText { get => _openRgbStatusText; private set => SetProperty(ref _openRgbStatusText, value); }

    private string _activeGameStatusText = "No mapped game running (Manual Profile)";
    public string ActiveGameStatusText { get => _activeGameStatusText; private set => SetProperty(ref _activeGameStatusText, value); }

    private string _newProcessExecutable = string.Empty;
    public string NewProcessExecutable { get => _newProcessExecutable; set => SetProperty(ref _newProcessExecutable, value); }

    private HardwareDeviceInfo? _selectedHardwareDevice;
    public HardwareDeviceInfo? SelectedHardwareDevice
    {
        get => _selectedHardwareDevice;
        set
        {
            if (SetProperty(ref _selectedHardwareDevice, value) && value is not null)
            {
                Settings.PreferredDeviceIndex = value.DeviceIndex;
                _hardware.SelectDevice(value.DeviceIndex);
                _repository.SaveSettings(Settings);
            }
        }
    }

    // --- Settings Wrappers ---
    public string OpenRgbHost
    {
        get => Settings.OpenRgbHost;
        set { Settings.OpenRgbHost = value; OnPropertyChanged(); _repository.SaveSettings(Settings); }
    }

    public int OpenRgbPort
    {
        get => Settings.OpenRgbPort;
        set { Settings.OpenRgbPort = Math.Clamp(value, 1, 65535); OnPropertyChanged(); _repository.SaveSettings(Settings); }
    }

    public int TargetFps
    {
        get => Settings.TargetFps;
        set
        {
            Settings.TargetFps = Math.Clamp(value, 15, 120);
            _pipeline.TargetFps = Settings.TargetFps;
            OnPropertyChanged();
            _repository.SaveSettings(Settings);
        }
    }

    public bool StartWithWindows
    {
        get => Settings.StartWithWindows;
        set
        {
            Settings.StartWithWindows = value;
            _startupService.SetStartup(value, Settings.StartMinimizedToTray);
            OnPropertyChanged();
            _repository.SaveSettings(Settings);
        }
    }

    public bool StartMinimizedToTray
    {
        get => Settings.StartMinimizedToTray;
        set
        {
            Settings.StartMinimizedToTray = value;
            if (Settings.StartWithWindows)
                _startupService.SetStartup(true, value);
            OnPropertyChanged();
            _repository.SaveSettings(Settings);
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => Settings.MinimizeToTrayOnClose;
        set { Settings.MinimizeToTrayOnClose = value; OnPropertyChanged(); _repository.SaveSettings(Settings); }
    }

    public bool EnableGlobalBlackoutHotkey
    {
        get => Settings.EnableGlobalBlackoutHotkey;
        set
        {
            Settings.EnableGlobalBlackoutHotkey = value;
            OnPropertyChanged();
            HotkeySettingChanged?.Invoke(value);
            _repository.SaveSettings(Settings);
        }
    }

    public bool EnableProcessWatcher
    {
        get => Settings.EnableProcessWatcher;
        set
        {
            Settings.EnableProcessWatcher = value;
            if (value) _processWatcher.Start();
            else _processWatcher.Stop();
            OnPropertyChanged();
            _repository.SaveSettings(Settings);
        }
    }

    public bool AutoReconnectOpenRgb
    {
        get => Settings.AutoReconnectOpenRgb;
        set { Settings.AutoReconnectOpenRgb = value; OnPropertyChanged(); _repository.SaveSettings(Settings); }
    }

    public string DataDirectoryPath => _repository.DataDirectory;

    // --- Commands ---
    public ICommand NavigateCommand { get; }
    public ICommand SelectEffectCommand { get; }
    public ICommand ToggleBlackoutCommand { get; }
    public ICommand ReconnectOpenRgbCommand { get; }
    public ICommand DisconnectOpenRgbCommand { get; }
    public ICommand ResetLayoutCommand { get; }
    public ICommand AddZoneCommand { get; }
    public ICommand RemoveZoneCommand { get; }
    public ICommand SyncZonesFromHardwareCommand { get; }
    public ICommand ApplyCasePresetCommand { get; }
    public ICommand SetDirectionCommand { get; }
    public ICommand ApplyQuickPaletteCommand { get; }
    public ICommand SyncAllChannelsCommand { get; }
    public ICommand ExportProfilesCommand { get; }
    public ICommand SetPrimarySwatchCommand { get; }
    public ICommand AddGradientStopCommand { get; }
    public ICommand RemoveGradientStopCommand { get; }
    public ICommand ApplyProfileCommand { get; }
    public ICommand CreateProfileCommand { get; }
    public ICommand DuplicateProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand SaveProfilesCommand { get; }
    public ICommand AddProcessBindingCommand { get; }
    public ICommand RemoveProcessBindingCommand { get; }
    public ICommand AddPresetProcessCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    public void RefreshLiveTelemetryAndDiagnostics(RenderDiagnostics diag, AudioSpectrumFrame audio)
    {
        FpsText = $"{diag.CurrentFps:F0} FPS";
        OnPropertyChanged(nameof(HidStatusBadge));
        FrameMsText = $"{diag.AverageFrameMs:F2} ms";
        MemoryText = $"{diag.WorkingSetMb:F1} MB";

        var snap = _telemetry.GetLatestSnapshot();
        CpuTempText = $"{snap.CpuTempC:F0}°C";
        GpuTempText = $"{snap.GpuTempC:F0}°C";
        CpuLoadPercent = Math.Clamp(snap.CpuLoadPercent, 0, 100);
        GpuLoadPercent = Math.Clamp(snap.GpuLoadPercent, 0, 100);
        CpuLoadText = $"{CpuLoadPercent:F0}%";
        GpuLoadText = $"{GpuLoadPercent:F0}%";
        SystemFanRpmText = snap.SystemFanRpm > 0 ? $"{snap.SystemFanRpm:N0} RPM" : "Auto (PWM)";
        CpuName = snap.CpuName;
        GpuName = snap.GpuName;
        TelemetryStatusText = snap.StatusMessage;

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastHistoryPushMs >= 1000)
        {
            _lastHistoryPushMs = nowMs;
            if (_cpuHistory.Count >= 60) _cpuHistory.Dequeue();
            if (_gpuHistory.Count >= 60) _gpuHistory.Dequeue();
            _cpuHistory.Enqueue(snap.CpuTempC);
            _gpuHistory.Enqueue(snap.GpuTempC);
            TemperatureHistoryUpdated?.Invoke(_cpuHistory.ToArray(), _gpuHistory.ToArray());
        }

        CaptureStatusText = _screenCapture.StatusMessage;
        AudioStatusText = _audioCapture.StatusMessage;
        AudioBassLevel = audio.BassEnergy;
        AudioMidLevel = audio.MidEnergy;
        AudioTrebleLevel = audio.TrebleEnergy;

        if (audio.Bands.Length >= 32)
        {
            AudioBar0 = Math.Clamp(audio.Bands[1] * 1.2, 0.06, 1.0);
            AudioBar1 = Math.Clamp(audio.Bands[4] * 1.2, 0.06, 1.0);
            AudioBar2 = Math.Clamp(audio.Bands[8] * 1.2, 0.06, 1.0);
            AudioBar3 = Math.Clamp(audio.Bands[12] * 1.2, 0.06, 1.0);
            AudioBar4 = Math.Clamp(audio.Bands[16] * 1.2, 0.06, 1.0);
            AudioBar5 = Math.Clamp(audio.Bands[20] * 1.2, 0.06, 1.0);
            AudioBar6 = Math.Clamp(audio.Bands[24] * 1.2, 0.06, 1.0);
            AudioBar7 = Math.Clamp(audio.Bands[28] * 1.2, 0.06, 1.0);
        }
    }

    public void UpdateZoneLiveColors(Dictionary<int, RgbColor[]> zoneColors)
    {
        foreach (var z in Zones)
        {
            if (zoneColors.TryGetValue(z.ZoneIndex, out var arr))
            {
                z.LiveColors = arr.ToArray();
            }
        }
    }

    public void OnCanvasZoneGeometryChanged(ZoneLayout updated)
    {
        var vm = Zones.FirstOrDefault(z => z.ZoneIndex == updated.ZoneIndex);
        if (vm is not null)
        {
            vm.SyncFromCanvas(updated);
            PushZonesToPipeline();
        }
    }

    public IReadOnlyList<ZoneLayout> GetCurrentZones() => Zones.Select(z => z.ToModel()).ToList();

    public void ActivateProfile(LightingProfile profile, bool isAutoSwitch)
    {
        _suppressPipelineSync = true;
        try
        {
            ActiveProfile = profile;
            if (!isAutoSwitch)
            {
                _manualProfileIdBeforeGame = profile.Id;
                Settings.ActiveProfileId = profile.Id;
                _repository.SaveSettings(Settings);
            }

            ActiveEffect = profile.Effect;
            MasterBrightness = profile.MasterBrightness;

            var p = profile.Parameters;
            PrimaryColorHex = p.PrimaryColorHex;
            Speed = p.Speed;
            Scale = p.Scale;
            DirectionDegrees = p.DirectionDegrees;
            ReverseDirection = p.ReverseDirection;
            GradientAngleDegrees = p.GradientAngleDegrees;
            AnimateGradient = p.AnimateGradient;
            AmbilightSmoothing = p.AmbilightSmoothing;
            AmbilightSaturation = p.AmbilightSaturation;
            AmbilightEdgeBias = p.AmbilightEdgeBias;
            AudioMode = p.AudioMode;
            AudioSensitivity = p.AudioSensitivity;
            AudioLowColorHex = p.AudioLowColorHex;
            AudioMidColorHex = p.AudioMidColorHex;
            AudioHighColorHex = p.AudioHighColorHex;
            TelemetrySource = p.TelemetrySource;
            CoolTempC = p.CoolTempC;
            WarmTempC = p.WarmTempC;
            HotTempC = p.HotTempC;
            CoolColorHex = p.CoolColorHex;
            WarmColorHex = p.WarmColorHex;
            HotColorHex = p.HotColorHex;
            TelemetryPulseAtHot = p.TelemetryPulseAtHot;

            GradientStops.Clear();
            foreach (var stop in p.GradientStops)
                GradientStops.Add(new GradientStopViewModel(stop, PushParametersToPipeline));

            Zones.Clear();
            foreach (var z in profile.Zones)
                Zones.Add(new ZoneItemViewModel(z, PushZonesToPipeline));

            SelectedZoneIndex = Zones.FirstOrDefault()?.ZoneIndex ?? 0;
        }
        finally
        {
            _suppressPipelineSync = false;
        }

        _pipeline.ApplyProfile(profile);
        ZonesStructureChanged?.Invoke();
    }

    private void PushParametersToPipeline()
    {
        if (_suppressPipelineSync) return;

        var stops = GradientStops.Select(s => s.ToModel()).ToList();
        void Apply(EffectParameters p)
        {
            p.PrimaryColorHex = PrimaryColorHex;
            p.Speed = Speed;
            p.Scale = Scale;
            p.DirectionDegrees = DirectionDegrees;
            p.ReverseDirection = ReverseDirection;
            p.GradientAngleDegrees = GradientAngleDegrees;
            p.AnimateGradient = AnimateGradient;
            p.GradientStops = stops;
            p.AmbilightSmoothing = AmbilightSmoothing;
            p.AmbilightSaturation = AmbilightSaturation;
            p.AmbilightEdgeBias = AmbilightEdgeBias;
            p.AudioMode = AudioMode;
            p.AudioSensitivity = AudioSensitivity;
            p.AudioLowColorHex = AudioLowColorHex;
            p.AudioMidColorHex = AudioMidColorHex;
            p.AudioHighColorHex = AudioHighColorHex;
            p.TelemetrySource = TelemetrySource;
            p.CoolTempC = CoolTempC;
            p.WarmTempC = WarmTempC;
            p.HotTempC = HotTempC;
            p.CoolColorHex = CoolColorHex;
            p.WarmColorHex = WarmColorHex;
            p.HotColorHex = HotColorHex;
            p.TelemetryPulseAtHot = TelemetryPulseAtHot;
        }

        Apply(_activeProfile.Parameters);
        _pipeline.UpdateParameters(Apply);
    }

    private void PushZonesToPipeline()
    {
        if (_suppressPipelineSync) return;

        var models = Zones.Select(z => z.ToModel()).ToList();
        _activeProfile.Zones = models;
        _pipeline.UpdateZones(models);
        ZonesStructureChanged?.Invoke();
    }

    private void ResetZonesToDefault()
    {
        ApplyZoneList(ZoneLayout.CreateGeneric3ZoneLayout());
    }

    private void ApplyCasePreset(string? preset)
    {
        var list = preset switch
        {
            "O11" => ZoneLayout.CreateDefaultO11MiniV2Layout(),
            "Single" =>
            [
                new ZoneLayout
                {
                    ZoneIndex = 0,
                    Name = "Primary LED Strip",
                    HardwareDescription = "Single Continuous ARGB Zone",
                    LedCount = 30,
                    X = 0.5, Y = 0.5, Width = 0.78, Height = 0.22,
                    Arrangement = ZoneLedArrangement.HorizontalStrip,
                    IsEnabled = true,
                    AccentHex = "#00F0FF"
                }
            ],
            _ => ZoneLayout.CreateGeneric3ZoneLayout()
        };
        ApplyZoneList(list);
    }

    private void AddCustomZone()
    {
        int nextIdx = Zones.Count == 0 ? 0 : Zones.Max(z => z.ZoneIndex) + 1;
        string[] accents = ["#00F0FF", "#7B61FF", "#FF2E93", "#00FF9C", "#FFAE00"];
        var added = new ZoneLayout
        {
            ZoneIndex = nextIdx,
            Name = $"Custom Zone {nextIdx + 1}",
            HardwareDescription = "Custom ARGB Strip / Fan",
            LedCount = 16,
            X = 0.5,
            Y = Math.Clamp(0.25 + (Zones.Count % 4) * 0.16, 0.15, 0.85),
            Width = 0.55,
            Height = 0.14,
            Arrangement = ZoneLedArrangement.HorizontalStrip,
            IsEnabled = true,
            AccentHex = accents[nextIdx % accents.Length]
        };

        Zones.Add(new ZoneItemViewModel(added, PushZonesToPipeline));
        SelectedZoneIndex = added.ZoneIndex;
        PushZonesToPipeline();
        SaveProfilesQuietly();
    }

    private void RemoveSelectedZone()
    {
        if (Zones.Count <= 1) return;
        var target = SelectedZone;
        if (target is null) return;

        Zones.Remove(target);
        SelectedZoneIndex = Zones.FirstOrDefault()?.ZoneIndex ?? 0;
        PushZonesToPipeline();
        SaveProfilesQuietly();
    }

    private void SyncZonesFromOpenRgbDevice()
    {
        var dev = SelectedHardwareDevice ?? _hardware.ConnectionState.ActiveDevice;
        if (dev is null || dev.Zones.Count == 0) return;
        ApplyZoneList(ZoneLayout.CreateFromHardwareDevice(dev));
    }

    private void ApplyZoneList(List<ZoneLayout> newZones)
    {
        _suppressPipelineSync = true;
        Zones.Clear();
        foreach (var z in newZones)
            Zones.Add(new ZoneItemViewModel(z, PushZonesToPipeline));
        SelectedZoneIndex = Zones.FirstOrDefault()?.ZoneIndex ?? 0;
        _suppressPipelineSync = false;

        PushZonesToPipeline();
        SaveProfilesQuietly();
    }

    private void AddGradientStop()
    {
        GradientStops.Add(new GradientStopViewModel(new GradientStopConfig(0.5, "#00FF9C"), PushParametersToPipeline));
        PushParametersToPipeline();
    }

    private void CreateNewProfile()
    {
        var created = new LightingProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"Custom Profile {Profiles.Count + 1}",
            Description = "User-created LuminaCore profile",
            Effect = ActiveEffect,
            MasterBrightness = MasterBrightness,
            Parameters = _activeProfile.Parameters.Clone(),
            Zones = GetCurrentZones().ToList()
        };
        Profiles.Add(created);
        SelectedProfile = created;
        ActivateProfile(created, isAutoSwitch: false);
        SaveAllState();
    }

    private void DuplicateSelectedProfile()
    {
        var src = SelectedProfile ?? ActiveProfile;
        var dup = src.Clone($"{src.Name} (Copy)");
        Profiles.Add(dup);
        SelectedProfile = dup;
        SaveAllState();
    }

    private void DeleteSelectedProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1) return;
        var toRemove = SelectedProfile;
        Profiles.Remove(toRemove);
        SelectedProfile = Profiles[0];
        if (ActiveProfile.Id == toRemove.Id)
            ActivateProfile(Profiles[0], isAutoSwitch: false);
        SaveAllState();
    }

    private void AddProcessBinding()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(NewProcessExecutable)) return;
        string cleaned = NewProcessExecutable.Trim();
        if (!cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleaned += ".exe";

        if (!SelectedProfile.AssociatedProcesses.Any(p => p.Equals(cleaned, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedProfile.AssociatedProcesses.Add(cleaned);
            RefreshSelectedProfileProcesses();
            _processWatcher.UpdateProfiles(Profiles.ToList());
            SaveAllState();
        }
        NewProcessExecutable = string.Empty;
    }

    private void RefreshSelectedProfileProcesses()
    {
        SelectedProfileProcesses.Clear();
        if (SelectedProfile is null) return;
        foreach (var proc in SelectedProfile.AssociatedProcesses)
            SelectedProfileProcesses.Add(proc);
    }

    private void HandleProcessWatcherMatch(ProcessMatchEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.ProfileId) && !string.IsNullOrEmpty(args.ProcessName))
        {
            var matched = Profiles.FirstOrDefault(p => p.Id == args.ProfileId);
            if (matched is not null)
            {
                ActiveGameStatusText = $"Detected {args.ProcessName} → Auto-switched to \"{matched.Name}\"";
                ActivateProfile(matched, isAutoSwitch: true);
                return;
            }
        }

        ActiveGameStatusText = "No mapped game running (Manual Profile)";
        var restore = Profiles.FirstOrDefault(p => p.Id == _manualProfileIdBeforeGame) ?? Profiles.FirstOrDefault();
        if (restore is not null && ActiveProfile.Id != restore.Id)
        {
            ActivateProfile(restore, isAutoSwitch: true);
        }
    }

    private void ApplyQuickPalette(string? palette)
    {
        switch (palette)
        {
            case "Cyber":
                PrimaryColorHex = "#00E5FF";
                AudioLowColorHex = "#00E5FF";
                AudioMidColorHex = "#8B5CF6";
                AudioHighColorHex = "#EC4899";
                break;
            case "Arctic":
                PrimaryColorHex = "#D7F4FF";
                AudioLowColorHex = "#E0F2FE";
                AudioMidColorHex = "#38BDF8";
                AudioHighColorHex = "#0284C7";
                break;
            case "Sunset":
                PrimaryColorHex = "#FF6B00";
                AudioLowColorHex = "#FF4500";
                AudioMidColorHex = "#FFAE00";
                AudioHighColorHex = "#EC4899";
                break;
            case "Matrix":
                PrimaryColorHex = "#00FF9C";
                AudioLowColorHex = "#00FF9C";
                AudioMidColorHex = "#10B981";
                AudioHighColorHex = "#00E5FF";
                break;
            case "Violet":
                PrimaryColorHex = "#8B5CF6";
                AudioLowColorHex = "#6366F1";
                AudioMidColorHex = "#8B5CF6";
                AudioHighColorHex = "#EC4899";
                break;
        }
    }

    private void ExportProfilesToDesktop()
    {
        try
        {
            SaveAllState();
            string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LuminaCore-profiles-backup.json");
            File.Copy(_repository.ProfilesFilePath, dest, overwrite: true);
            ExportStatusText = "Exported backup to Desktop!";
        }
        catch (Exception ex)
        {
            ExportStatusText = $"Export failed: {ex.Message}";
        }
    }

    public void SaveAllState()
    {
        SaveProfilesQuietly();
        _repository.SaveSettings(Settings);
        _processWatcher.UpdateProfiles(Profiles.ToList());
    }

    private void SaveProfilesQuietly()
    {
        try { _repository.SaveProfiles(Profiles); } catch { }
    }

    private async Task ConnectHardwareAsync()
    {
        OpenRgbStatusText = $"Connecting to OpenRGB ({Settings.OpenRgbHost}:{Settings.OpenRgbPort})...";
        await _hardware.ConnectAsync(Settings.OpenRgbHost, Settings.OpenRgbPort, Settings.PreferredDeviceIndex);
    }

    private void ApplyHardwareState(HardwareConnectionState state)
    {
        IsOpenRgbConnected = state.IsConnected;
        OpenRgbStatusText = state.StatusMessage;
        OnPropertyChanged(nameof(HidStatusBadge));

        DiscoveredDevices.Clear();
        foreach (var dev in state.DiscoveredDevices)
            DiscoveredDevices.Add(dev);

        _selectedHardwareDevice = state.ActiveDevice is not null
            ? DiscoveredDevices.FirstOrDefault(d => d.DeviceIndex == state.ActiveDevice.DeviceIndex)
            : null;
        OnPropertyChanged(nameof(SelectedHardwareDevice));

        ActiveDeviceZones.Clear();
        if (state.ActiveDevice is not null)
        {
            ActiveHardwareSummary = $"{state.ActiveDevice.Name} • {state.ActiveDevice.Zones.Count} Zone(s)";
            foreach (var z in state.ActiveDevice.Zones)
                ActiveDeviceZones.Add(z);
        }
        else
        {
            ActiveHardwareSummary = state.IsConnected
                ? "OpenRGB Connected (0 RGB devices found)"
                : "Virtual Preview Mode (OpenRGB offline)";
        }
    }

    private void StartOpenRgbAutoReconnectLoop()
    {
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;

        _ = Task.Run(async () =>
        {
            await ConnectHardwareAsync();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var conn = _hardware.ConnectionState;
                    if (Settings.AutoReconnectOpenRgb && (!conn.IsConnected || conn.ActiveDevice is null))
                    {
                        await _hardware.ConnectAsync(Settings.OpenRgbHost, Settings.OpenRgbPort, Settings.PreferredDeviceIndex, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void Dispose()
    {
        SaveAllState();
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
    }
}
