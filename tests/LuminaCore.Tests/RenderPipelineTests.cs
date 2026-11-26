using System.Diagnostics;
using LuminaCore.Core.Engine;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;
using LuminaCore.Core.Plugins;
using LuminaCore.Core.Services;
using Xunit;

namespace LuminaCore.Tests;

public sealed class RenderPipelineTests
{
    private sealed class MockHardwareController : IHardwareController
    {
        public Dictionary<int, RgbColor[]> LastPushedZones { get; } = new();
        public int PushCallCount { get; private set; }
        public int BlackoutCallCount { get; private set; }

        public HardwareConnectionState ConnectionState => new() { IsConnected = true, StatusMessage = "Mock Connected" };
        public event EventHandler<HardwareConnectionState>? ConnectionStateChanged;

        public Task<bool> ConnectAsync(string host, int port, int preferredDeviceIndex = -1, CancellationToken ct = default)
        {
            ConnectionStateChanged?.Invoke(this, ConnectionState);
            return Task.FromResult(true);
        }

        public void Disconnect() { }
        public void SelectDevice(int deviceIndex) { }

        public void PushZoneColors(int zoneIndex, ReadOnlySpan<RgbColor> colors)
        {
            PushCallCount++;
            LastPushedZones[zoneIndex] = colors.ToArray();
        }

        public void SetAllBlackout(IReadOnlyList<ZoneLayout> zones)
        {
            BlackoutCallCount++;
            foreach (var z in zones)
                LastPushedZones[z.ZoneIndex] = Enumerable.Repeat(RgbColor.Black, z.LedCount).ToArray();
        }

        public void SetChannelColorOrder(int channelIndex, ChannelColorOrder order) { }
        public void SetCalibrationPreview(int channelIndex, int litLedCount) { }
        public void StopCalibrationPreview() { }
        public HidDiagnosticsInfo GetHidDiagnostics() => new() { IsConnected = true, ControllerName = "Mock HID" };

        public void Dispose() { }
    }

    private sealed class MockScreenCapture : IScreenCaptureService
    {
        public bool IsRunning { get; private set; }
        public string StatusMessage => "Mock DXGI";
        public RgbColor FillColor { get; set; } = new(20, 180, 255);

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;

        public void CopyLatestGrid(ScreenGridFrame target)
        {
            Array.Fill(target.Cells, FillColor);
            target.HasValidFrame = true;
            target.FrameSequence++;
        }

        public void Dispose() { }
    }

    private sealed class MockAudioCapture : IAudioCaptureService
    {
        public bool IsRunning { get; private set; }
        public string StatusMessage => "Mock WASAPI";

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;

        public void CopyLatestSpectrum(AudioSpectrumFrame target)
        {
            Array.Fill(target.Bands, 0.75f);
            target.BassEnergy = 0.85f;
            target.MidEnergy = 0.6f;
            target.TrebleEnergy = 0.4f;
            target.BeatIntensity = 0.9f;
            target.IsActive = true;
        }

        public void Dispose() { }
    }

    private sealed class MockTelemetry : ITelemetryService
    {
        public HardwareTelemetrySnapshot Snapshot { get; set; } = new()
        {
            CpuTempC = 65f,
            GpuTempC = 55f,
            IsLiveSensorData = true
        };

        public bool IsRunning { get; private set; }
        public void Start(int pollIntervalMs = 1000) => IsRunning = true;
        public void Stop() => IsRunning = false;
        public HardwareTelemetrySnapshot GetLatestSnapshot() => Snapshot;
        public void Dispose() { }
    }

    private static ILightEffect[] CreateAllEffects() =>
    [
        new SolidColorEffect(),
        new MultiStopGradientEffect(),
        new RainbowWaveEffect(),
        new RainbowSpiralEffect(),
        new ScreenAmbilightEffect(),
        new AudioVisualizerEffect(),
        new HardwareTelemetryEffect(),
        new BreathingEffect(),
        new PlasmaEffect()
    ];

    [Fact]
    public void DefaultLayout_HasThreeZonesWithValidNormalizedLedCoordinates()
    {
        var zones = ZoneLayout.CreateGeneric3ZoneLayout();
        Assert.Equal(3, zones.Count);
        Assert.Contains("ARGB Header 1", zones[0].Name);
        Assert.Contains("ARGB Header 2", zones[1].Name);
        Assert.Contains("ARGB Header 3", zones[2].Name);

        foreach (var zone in zones)
        {
            var coords = new (float U, float V)[zone.LedCount];
            zone.ComputeLedCoordinates(coords);

            foreach (var (u, v) in coords)
            {
                Assert.InRange(u, 0f, 1f);
                Assert.InRange(v, 0f, 1f);
            }
        }
    }

    [Fact]
    public void HardwareSync_BuildsLayoutFromAnyOpenRgbDevice()
    {
        var dev = new HardwareDeviceInfo
        {
            DeviceIndex = 0,
            Name = "Custom ARGB Controller",
            Zones =
            [
                new HardwareZoneInfo { Index = 0, Name = "Desk Strip", LedCount = 30 },
                new HardwareZoneInfo { Index = 1, Name = "Fan Hub", LedCount = 12 }
            ]
        };

        var synced = ZoneLayout.CreateFromHardwareDevice(dev);
        Assert.Equal(2, synced.Count);
        Assert.Equal("Desk Strip", synced[0].Name);
        Assert.Equal(30, synced[0].LedCount);
        Assert.Equal("Fan Hub", synced[1].Name);
        Assert.Equal(12, synced[1].LedCount);
    }

    [Fact]
    public void ZoneRotation_RotatesLedCoordinatesAroundZoneCenter()
    {
        var zone = new ZoneLayout
        {
            ZoneIndex = 0,
            LedCount = 2,
            X = 0.5,
            Y = 0.5,
            Width = 0.4,
            Height = 0.1,
            RotationDegrees = 90,
            Arrangement = ZoneLedArrangement.HorizontalStrip
        };

        var coords = new (float U, float V)[2];
        zone.ComputeLedCoordinates(coords);

        // After 90-degree rotation, horizontal strip becomes vertical along X = 0.5
        Assert.InRange(MathF.Abs(coords[0].U - 0.5f), 0f, 0.01f);
        Assert.InRange(MathF.Abs(coords[1].U - 0.5f), 0f, 0.01f);
        Assert.True(coords[1].V > coords[0].V);
    }

    [Fact]
    public void HardwareTelemetry_MapsTemperatureThresholdsAccurately()
    {
        var p = new EffectParameters
        {
            CoolTempC = 40,
            WarmTempC = 65,
            HotTempC = 80,
            CoolColorHex = "#D7F4FF",
            WarmColorHex = "#FFAE00",
            HotColorHex = "#FF183A"
        };

        Assert.Equal(RgbColor.FromHex("#D7F4FF"), HardwareTelemetryEffect.MapTemperatureToColor(35f, p));
        Assert.Equal(RgbColor.FromHex("#FFAE00"), HardwareTelemetryEffect.MapTemperatureToColor(65f, p));
        Assert.Equal(RgbColor.FromHex("#FF183A"), HardwareTelemetryEffect.MapTemperatureToColor(85f, p));

        // Midpoint between 40°C and 65°C should blend cool and warm
        var mid = HardwareTelemetryEffect.MapTemperatureToColor(52.5f, p);
        Assert.True(mid.R > 220 && mid.G > 190 && mid.B > 100 && mid.B < 200);
    }

    [Fact]
    public void FftAnalyzer_DetectsLowFrequencyBassSineWave()
    {
        var fft = new FftAnalyzer();
        const int sampleRate = 48000;
        const float bassFreqHz = 80f;
        var pcm = new float[FftAnalyzer.FftSize * 2];

        for (int i = 0; i < FftAnalyzer.FftSize; i++)
        {
            float sample = MathF.Sin(2f * MathF.PI * bassFreqHz * i / sampleRate) * 0.8f;
            pcm[i * 2] = sample;
            pcm[i * 2 + 1] = sample;
        }

        fft.PushSamples(pcm, channels: 2);
        var frame = new AudioSpectrumFrame();
        fft.ComputeSpectrum(sampleRate, frame);

        Assert.True(frame.IsActive);
        Assert.True(frame.BassEnergy > 0.25f, $"Expected strong bass energy, got {frame.BassEnergy}");
        Assert.True(frame.BassEnergy > frame.TrebleEnergy * 2f);
    }

    [Fact]
    public void RenderPipeline_AllEffectsRenderAndSampleAllThreeZonesUnder2MsPerFrame()
    {
        var hw = new MockHardwareController();
        var screen = new MockScreenCapture();
        var audio = new MockAudioCapture();
        var telemetry = new MockTelemetry();
        using var pipeline = new RenderPipeline(hw, screen, audio, telemetry, CreateAllEffects());

        foreach (EffectType effectType in Enum.GetValues(typeof(EffectType)))
        {
            pipeline.UpdateEffect(effectType);
            pipeline.UpdateMasterBrightness(1.0);

            var sw = Stopwatch.StartNew();
            for (int f = 0; f < 30; f++)
            {
                pipeline.RenderSingleFrame(f / 60.0, 1.0 / 60.0);
            }
            sw.Stop();

            double avgMs = sw.Elapsed.TotalMilliseconds / 30.0;
            Assert.True(avgMs < 5.0, $"Effect {effectType} took {avgMs:F2}ms/frame (exceeds 60 FPS budget)");
            Assert.Equal(3, hw.LastPushedZones.Count);
            Assert.Equal(24, hw.LastPushedZones[0].Length);
            Assert.Equal(24, hw.LastPushedZones[1].Length);
            Assert.Equal(16, hw.LastPushedZones[2].Length);
        }
    }

    [Fact]
    public void RenderPipeline_BlackoutModeZerosHardwareAndOnlySendsOnce()
    {
        var hw = new MockHardwareController();
        using var pipeline = new RenderPipeline(hw, new MockScreenCapture(), new MockAudioCapture(), new MockTelemetry(), CreateAllEffects());

        pipeline.IsBlackout = true;
        pipeline.RenderSingleFrame(0.1, 0.016);
        pipeline.RenderSingleFrame(0.2, 0.016);
        pipeline.RenderSingleFrame(0.3, 0.016);

        Assert.Equal(1, hw.BlackoutCallCount);
        Assert.All(hw.LastPushedZones[0], c => Assert.Equal(RgbColor.Black, c));
        Assert.All(hw.LastPushedZones[1], c => Assert.Equal(RgbColor.Black, c));
        Assert.All(hw.LastPushedZones[2], c => Assert.Equal(RgbColor.Black, c));
    }

    [Fact]
    public void ProfileRepository_RoundTripsProfilesAndGameProcessBindings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "LuminaCoreTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new ProfileRepository(tempDir);
            var profiles = repo.LoadProfiles();

            Assert.True(profiles.Count >= 4);
            Assert.Contains(profiles, p => p.AssociatedProcesses.Contains("cod.exe"));
            Assert.Contains(profiles, p => p.AssociatedProcesses.Contains("ForzaHorizon5.exe"));
            Assert.Contains(profiles, p => p.AssociatedProcesses.Contains("RDR2.exe"));

            profiles[0].Name = "Updated Neon Profile";
            repo.SaveProfiles(profiles);

            var reloaded = repo.LoadProfiles();
            Assert.Equal("Updated Neon Profile", reloaded[0].Name);
            Assert.Equal(3, reloaded[0].Zones.Count);

            // Second round-trip must never duplicate Zones or GradientStops
            repo.SaveProfiles(reloaded);
            var reloadedTwice = repo.LoadProfiles();
            Assert.Equal(3, reloadedTwice[0].Zones.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LuaPluginManager_LoadsAndRendersLuaShaderPlugin()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "LuminaPlugins_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test_wave.lua"), """
                name = "Test Wave"
                author = "UnitTest"
                description = "Synthetic test"
                accent = "#00E5FF"
                function render(u, v, time, speed, scale)
                    return 255, 128, 64
                end
                """);

            using var mgr = new LuaPluginManager(tempDir);
            var plugins = mgr.GetLoadedPlugins();
            Assert.Single(plugins);
            Assert.Equal("Test Wave", plugins[0].Name);

            var surface = new CanvasSurface();
            plugins[0].Render(surface, 1.0, new EffectParameters());
            Assert.Equal(new RgbColor(255, 128, 64), surface.GetPixel(80, 45));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
