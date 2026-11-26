using System.Diagnostics;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;
using LuminaCore.Core.Plugins;

namespace LuminaCore.Core.Engine;

/// <summary>
/// Snapshot delivered from the 60 FPS background render loop to the UI thread for preview updates.
/// </summary>
public sealed class RenderFramePreview
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] PixelsBgra { get; init; } = [];
    public Dictionary<int, RgbColor[]> ZoneColors { get; init; } = [];
    public RenderDiagnostics Diagnostics { get; init; } = new();
    public HardwareTelemetrySnapshot Telemetry { get; init; } = new();
    public AudioSpectrumFrame AudioSummary { get; init; } = new();
}

/// <summary>
/// High-performance 60 FPS lockstep render engine running on a dedicated PeriodicTimer background Task.
/// Renders the active 2D shader onto a compact CanvasSurface, samples every LED coordinate for the 3 ARGB zones,
/// and transmits colors to the OpenRGB hardware backend with minimal CPU and zero per-frame allocations.
/// </summary>
public sealed class RenderPipeline : IDisposable
{
    private readonly IHardwareController _hardware;
    private readonly IScreenCaptureService _screenCapture;
    private readonly IAudioCaptureService _audioCapture;
    private readonly ITelemetryService _telemetry;
    private readonly Dictionary<EffectType, ILightEffect> _effects;
    private readonly LuaPluginManager? _pluginManager;

    private readonly CanvasSurface _surface = new(CanvasSurface.DefaultWidth, CanvasSurface.DefaultHeight);
    private readonly ScreenGridFrame _screenGrid = new();
    private readonly AudioSpectrumFrame _audioSpectrum = new();

    // Reusable per-zone scratch buffers to avoid GC churn at 60 FPS
    private readonly Dictionary<int, RgbColor[]> _zoneColorBuffers = new();
    private (float U, float V)[] _coordScratch = new (float, float)[128];

    // Double-buffered preview copy for thread-safe UI reads
    private readonly object _previewLock = new();
    private readonly byte[] _previewPixels = new byte[CanvasSurface.DefaultWidth * CanvasSurface.DefaultHeight * 4];
    private readonly Dictionary<int, RgbColor[]> _previewZoneColors = new();
    private RenderDiagnostics _latestDiagnostics = new();

    private readonly object _stateLock = new();
    private LightingProfile _activeProfile;
    private int _targetFps = 60;
    private bool _isBlackout;
    private bool _blackoutSent;
    private bool _isPaused;
    private double _pausedElapsedSeconds;
    private string? _activePluginId;
    private bool _uiPreviewEnabled = true;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private long _frameCounter;

    public event Action? FrameRendered;

    public RenderPipeline(
        IHardwareController hardware,
        IScreenCaptureService screenCapture,
        IAudioCaptureService audioCapture,
        ITelemetryService telemetry,
        IEnumerable<ILightEffect> effects,
        LuaPluginManager? pluginManager = null)
    {
        _hardware = hardware;
        _screenCapture = screenCapture;
        _audioCapture = audioCapture;
        _telemetry = telemetry;
        _effects = effects.ToDictionary(e => e.Type);
        _pluginManager = pluginManager;
        _activeProfile = LightingProfile.CreateDefaultProfiles()[0];
    }

    public bool IsPaused
    {
        get { lock (_stateLock) return _isPaused; }
        set { lock (_stateLock) _isPaused = value; }
    }

    public string? ActivePluginId
    {
        get { lock (_stateLock) return _activePluginId; }
        set { lock (_stateLock) _activePluginId = value; }
    }

    public IReadOnlyCollection<ILightEffect> AvailableEffects => _effects.Values;

    public bool IsBlackout
    {
        get { lock (_stateLock) return _isBlackout; }
        set
        {
            lock (_stateLock)
            {
                if (_isBlackout == value) return;
                _isBlackout = value;
                _blackoutSent = false;
            }
        }
    }

    public bool UiPreviewEnabled
    {
        get { lock (_stateLock) return _uiPreviewEnabled; }
        set { lock (_stateLock) _uiPreviewEnabled = value; }
    }

    public int TargetFps
    {
        get { lock (_stateLock) return _targetFps; }
        set { lock (_stateLock) _targetFps = Math.Clamp(value, 15, 120); }
    }

    public void ApplyProfile(LightingProfile profile)
    {
        lock (_stateLock)
        {
            _activeProfile = profile.Clone();
            _blackoutSent = false;
        }
        SyncCaptureServicesForEffect(profile.Effect);
    }

    public void UpdateEffect(EffectType effectType)
    {
        lock (_stateLock)
        {
            _activeProfile.Effect = effectType;
            _activePluginId = null;
        }
        SyncCaptureServicesForEffect(effectType);
    }

    public void UpdateParameters(Action<EffectParameters> mutator)
    {
        lock (_stateLock)
        {
            mutator(_activeProfile.Parameters);
        }
    }

    public void UpdateMasterBrightness(double brightness)
    {
        lock (_stateLock)
        {
            _activeProfile.MasterBrightness = Math.Clamp(brightness, 0.0, 1.0);
        }
    }

    public void UpdateZones(IReadOnlyList<ZoneLayout> zones)
    {
        lock (_stateLock)
        {
            _activeProfile.Zones = zones.Select(z => z.Clone()).ToList();
        }
    }

    public LightingProfile GetActiveProfileSnapshot()
    {
        lock (_stateLock) return _activeProfile.Clone();
    }

    public void Start()
    {
        if (_loopTask is { IsCompleted: false }) return;

        _cts = new CancellationTokenSource();
        var effect = GetActiveProfileSnapshot().Effect;
        SyncCaptureServicesForEffect(effect);
        _loopTask = Task.Factory.StartNew(
            () => RunLoopAsync(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
        _cts = null;
        _screenCapture.Stop();
        _audioCapture.Stop();
    }

    private void SyncCaptureServicesForEffect(EffectType effect)
    {
        // Only run DXGI capture when Screen Ambilight is active to keep idle CPU <0.5%
        if (effect == EffectType.ScreenAmbilight)
        {
            if (!_screenCapture.IsRunning) _screenCapture.Start();
        }
        else if (_screenCapture.IsRunning)
        {
            _screenCapture.Stop();
        }

        // Only run WASAPI loopback when Audio Visualizer is active
        if (effect == EffectType.AudioVisualizer)
        {
            if (!_audioCapture.IsRunning) _audioCapture.Start();
        }
        else if (_audioCapture.IsRunning)
        {
            _audioCapture.Stop();
        }
    }

    /// <summary>
    /// Executes a single deterministic frame (also callable directly from unit/performance tests).
    /// </summary>
    public void RenderSingleFrame(double elapsedSeconds, double deltaSeconds)
    {
        LightingProfile profile;
        bool blackout;
        bool sendPreview;
        bool paused;
        string? pluginId;

        lock (_stateLock)
        {
            profile = _activeProfile;
            blackout = _isBlackout;
            sendPreview = _uiPreviewEnabled;
            paused = _isPaused;
            pluginId = _activePluginId;
            if (!paused) _pausedElapsedSeconds = elapsedSeconds;
        }

        double effectiveElapsed = paused ? _pausedElapsedSeconds : elapsedSeconds;
        double effectiveDelta = paused ? 0.0 : deltaSeconds;
        var telemetry = _telemetry.GetLatestSnapshot();

        if (blackout)
        {
            _surface.Fill(RgbColor.Black);
            if (!_blackoutSent)
            {
                _hardware.SetAllBlackout(profile.Zones);
                _blackoutSent = true;
            }
            UpdatePreviewSnapshot(profile, true, 0, 0, telemetry);
            return;
        }

        if (profile.Effect == EffectType.ScreenAmbilight && pluginId is null)
            _screenCapture.CopyLatestGrid(_screenGrid);

        if (profile.Effect == EffectType.AudioVisualizer && pluginId is null)
            _audioCapture.CopyLatestSpectrum(_audioSpectrum);

        var activePlugin = pluginId is not null ? _pluginManager?.TryGetPlugin(pluginId) : null;
        if (activePlugin is not null)
        {
            activePlugin.Render(_surface, effectiveElapsed, profile.Parameters);
        }
        else if (_effects.TryGetValue(profile.Effect, out var effectImpl))
        {
            effectImpl.Render(
                _surface,
                effectiveElapsed,
                effectiveDelta,
                profile.Parameters,
                _screenGrid,
                _audioSpectrum,
                telemetry);
        }

        float brightness = (float)Math.Clamp(profile.MasterBrightness, 0.0, 1.0);
        int totalSampled = 0;

        for (int zIdx = 0; zIdx < profile.Zones.Count; zIdx++)
        {
            var zone = profile.Zones[zIdx];
            int ledCount = Math.Clamp(zone.LedCount, 1, 256);

            if (!_zoneColorBuffers.TryGetValue(zone.ZoneIndex, out var buffer) || buffer.Length != ledCount)
            {
                buffer = new RgbColor[ledCount];
                _zoneColorBuffers[zone.ZoneIndex] = buffer;
            }

            if (!zone.IsEnabled)
            {
                Array.Fill(buffer, RgbColor.Black);
            }
            else if (!zone.IsSynced)
            {
                RenderIndependentZone(zone, buffer, elapsedSeconds, brightness, profile.Parameters);
                totalSampled += ledCount;
            }
            else
            {
                if (_coordScratch.Length < ledCount)
                    _coordScratch = new (float, float)[ledCount];

                zone.ComputeLedCoordinates(_coordScratch.AsSpan(0, ledCount));

                for (int i = 0; i < ledCount; i++)
                {
                    var (u, v) = _coordScratch[i];
                    buffer[i] = _surface.SampleNormalized(u, v).ScaleBrightness(brightness);
                }
                totalSampled += ledCount;
            }

            _hardware.PushZoneColors(zone.ZoneIndex, buffer);
        }

        if (sendPreview)
        {
            UpdatePreviewSnapshot(profile, false, totalSampled, 0, telemetry);
        }
    }

    private static void RenderIndependentZone(
        ZoneLayout zone,
        RgbColor[] buffer,
        double elapsedSeconds,
        float brightness,
        EffectParameters parameters)
    {
        var baseColor = RgbColor.FromHex(zone.IndependentColorHex, RgbColor.NeonCyan);
        float speed = (float)Math.Clamp(parameters.Speed, 0.2, 5.0);
        int count = buffer.Length;

        switch (zone.IndependentEffect)
        {
            case EffectType.RainbowWave:
            case EffectType.RainbowSpiral:
                for (int i = 0; i < count; i++)
                {
                    float hue = (i / (float)Math.Max(1, count)) * 360f - (float)(elapsedSeconds * speed * 110.0);
                    buffer[i] = RgbColor.FromHsv(hue, 1f, 1f).ScaleBrightness(brightness);
                }
                break;

            case EffectType.Breathing:
            {
                float wave = 0.5f - 0.5f * MathF.Cos((float)(elapsedSeconds * speed * 1.8));
                float b = (0.08f + 0.92f * MathF.Pow(wave, 1.35f)) * brightness;
                Array.Fill(buffer, baseColor.ScaleBrightness(b));
                break;
            }

            case EffectType.Plasma:
            {
                var c2 = RgbColor.FromHex("#8B5CF6");
                for (int i = 0; i < count; i++)
                {
                    float u = i / (float)Math.Max(1, count) * 4f;
                    float m = 0.5f + 0.5f * MathF.Sin(u + (float)(elapsedSeconds * speed * 2.0));
                    buffer[i] = RgbColor.Lerp(baseColor, c2, m).ScaleBrightness(brightness);
                }
                break;
            }

            default:
                Array.Fill(buffer, baseColor.ScaleBrightness(brightness));
                break;
        }
    }

    private void UpdatePreviewSnapshot(
        LightingProfile profile,
        bool blackout,
        int totalSampled,
        double avgFrameMs,
        HardwareTelemetrySnapshot telemetry)
    {
        lock (_previewLock)
        {
            _surface.CopyTo(_previewPixels);

            foreach (var zone in profile.Zones)
            {
                int count = Math.Clamp(zone.LedCount, 1, 256);
                if (!_previewZoneColors.TryGetValue(zone.ZoneIndex, out var dst) || dst.Length != count)
                {
                    dst = new RgbColor[count];
                    _previewZoneColors[zone.ZoneIndex] = dst;
                }

                if (blackout)
                {
                    Array.Fill(dst, RgbColor.Black);
                }
                else if (_zoneColorBuffers.TryGetValue(zone.ZoneIndex, out var src) && src.Length == count)
                {
                    Array.Copy(src, dst, count);
                }
            }

            _latestDiagnostics = new RenderDiagnostics
            {
                CurrentFps = _currentMeasuredFps,
                AverageFrameMs = avgFrameMs > 0 ? avgFrameMs : _latestDiagnostics.AverageFrameMs,
                WorkingSetMb = _cachedWorkingSetMb,
                IsBlackout = blackout,
                ActiveEffectName = _effects.TryGetValue(profile.Effect, out var fx) ? fx.DisplayName : profile.Effect.ToString(),
                ActiveProfileName = profile.Name,
                TotalSampledLeds = totalSampled
            };
        }
    }

    private double _currentMeasuredFps;
    private double _cachedWorkingSetMb;

    public void CopyPreviewData(
        byte[] targetPixelsBgra,
        Dictionary<int, RgbColor[]> targetZoneColors,
        AudioSpectrumFrame targetAudio,
        out RenderDiagnostics diagnostics)
    {
        lock (_previewLock)
        {
            Buffer.BlockCopy(_previewPixels, 0, targetPixelsBgra, 0, Math.Min(_previewPixels.Length, targetPixelsBgra.Length));
            foreach (var kvp in _previewZoneColors)
            {
                if (!targetZoneColors.TryGetValue(kvp.Key, out var dst) || dst.Length != kvp.Value.Length)
                {
                    dst = new RgbColor[kvp.Value.Length];
                    targetZoneColors[kvp.Key] = dst;
                }
                Array.Copy(kvp.Value, dst, kvp.Value.Length);
            }
            diagnostics = _latestDiagnostics;
        }

        if (_audioCapture.IsRunning)
            _audioCapture.CopyLatestSpectrum(targetAudio);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        double lastElapsed = 0;
        double fpsWindowStart = 0;
        int fpsFrameCount = 0;
        double frameTimeAccumMs = 0;
        int lastConfiguredFps = TargetFps;

        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / lastConfiguredFps));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                int desiredFps = TargetFps;
                if (desiredFps != lastConfiguredFps)
                {
                    lastConfiguredFps = desiredFps;
                    timer.Dispose();
                    timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / lastConfiguredFps));
                }

                double now = clock.Elapsed.TotalSeconds;
                double dt = Math.Clamp(now - lastElapsed, 0.001, 0.25);
                lastElapsed = now;

                long frameStartTicks = Stopwatch.GetTimestamp();
                RenderSingleFrame(now, dt);
                double frameMs = Stopwatch.GetElapsedTime(frameStartTicks).TotalMilliseconds;

                fpsFrameCount++;
                frameTimeAccumMs += frameMs;
                _frameCounter++;

                double fpsSpan = now - fpsWindowStart;
                if (fpsSpan >= 0.5)
                {
                    _currentMeasuredFps = fpsFrameCount / fpsSpan;
                    double avgMs = frameTimeAccumMs / Math.Max(1, fpsFrameCount);
                    _cachedWorkingSetMb = Environment.WorkingSet / (1024.0 * 1024.0);

                    lock (_previewLock)
                    {
                        _latestDiagnostics = new RenderDiagnostics
                        {
                            CurrentFps = _currentMeasuredFps,
                            AverageFrameMs = avgMs,
                            WorkingSetMb = _cachedWorkingSetMb,
                            IsBlackout = _latestDiagnostics.IsBlackout,
                            ActiveEffectName = _latestDiagnostics.ActiveEffectName,
                            ActiveProfileName = _latestDiagnostics.ActiveProfileName,
                            TotalSampledLeds = _latestDiagnostics.TotalSampledLeds
                        };
                    }

                    fpsFrameCount = 0;
                    frameTimeAccumMs = 0;
                    fpsWindowStart = now;
                }

                if (UiPreviewEnabled)
                {
                    FrameRendered?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown
        }
        finally
        {
            timer.Dispose();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
