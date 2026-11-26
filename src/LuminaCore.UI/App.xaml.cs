using System.IO;
using System.Threading;
using System.Windows;
using LuminaCore.Capture;
using LuminaCore.Core.Engine;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Plugins;
using LuminaCore.Core.Services;
using LuminaCore.Hardware;
using LuminaCore.UI.Services;
using LuminaCore.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace LuminaCore.UI;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\LuminaCore_SingleInstance_Mutex";
    private const string ShowWindowEventName = @"Local\LuminaCore_ShowWindow_Event";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private ServiceProvider? _serviceProvider;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Signal the already-running LuminaCore instance to restore its window and exit cleanly
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowWindowEventName);
                ev.Set();
            }
            catch { }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
        {
            Dispatcher.BeginInvoke(() => _mainWindow?.RestoreWindowFromExternalSignal());
        }, null, -1, executeOnlyOnce: false);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuminaCore");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "error.log"), $"[{DateTime.Now:O}] {args.Exception}\n\n");
            }
            catch { }
        };

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Recover USB HID automatically when Windows resumes from Sleep/Hibernate
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        bool startMinimizedArg = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var vm = _serviceProvider.GetRequiredService<MainViewModel>();
        _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = _mainWindow;

        if (startMinimizedArg || vm.Settings.StartMinimizedToTray)
        {
            _mainWindow.InitializeHiddenInTray();
        }
        else
        {
            _mainWindow.Show();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && _serviceProvider is not null)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                var vm = _serviceProvider?.GetService<MainViewModel>();
                vm?.ReconnectOpenRgbCommand.Execute(null);
            });
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Persistence & OS integration
        services.AddSingleton<IProfileRepository>(_ => new ProfileRepository());
        services.AddSingleton<WindowsStartupService>();
        services.AddSingleton<GlobalHotkeyService>();

        // Hardware & capture backends (Native Direct USB HID first, OpenRGB fallback)
        services.AddSingleton<IHardwareController, SmartHardwareController>();
        services.AddSingleton<ITelemetryService, LibreHardwareTelemetryService>();
        services.AddSingleton<IScreenCaptureService, DxgiScreenCaptureService>();
        services.AddSingleton<IAudioCaptureService, WasapiAudioCaptureService>();
        services.AddSingleton<IProcessWatcherService, ProcessWatcherService>();

        // 60 FPS Shader Effects
        services.AddSingleton<ILightEffect, SolidColorEffect>();
        services.AddSingleton<ILightEffect, MultiStopGradientEffect>();
        services.AddSingleton<ILightEffect, RainbowWaveEffect>();
        services.AddSingleton<ILightEffect, RainbowSpiralEffect>();
        services.AddSingleton<ILightEffect, ScreenAmbilightEffect>();
        services.AddSingleton<ILightEffect, AudioVisualizerEffect>();
        services.AddSingleton<ILightEffect, HardwareTelemetryEffect>();
        services.AddSingleton<ILightEffect, BreathingEffect>();
        services.AddSingleton<ILightEffect, PlasmaEffect>();

        // Render pipeline, plugins & UI
        services.AddSingleton<LuaPluginManager>();
        services.AddSingleton<RenderPipeline>();
        services.AddSingleton<LocalWebRemoteService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    public async void ExitApplication()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (_serviceProvider is not null)
        {
            try
            {
                var pipeline = _serviceProvider.GetRequiredService<RenderPipeline>();
                await pipeline.StopAsync();
            }
            catch { }

            try { _serviceProvider.Dispose(); } catch { }
            _serviceProvider = null;
        }

        try { _showEvent?.Dispose(); } catch { }
        try { _instanceMutex?.ReleaseMutex(); _instanceMutex?.Dispose(); } catch { }

        Shutdown();
    }
}
