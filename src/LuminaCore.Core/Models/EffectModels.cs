namespace LuminaCore.Core.Models;

public enum EffectType
{
    Solid = 0,
    Gradient = 1,
    RainbowWave = 2,
    RainbowSpiral = 3,
    ScreenAmbilight = 4,
    AudioVisualizer = 5,
    HardwareTelemetry = 6,
    Breathing = 7,
    Plasma = 8
}

public enum AudioVisualizerMode
{
    SpectrumBars = 0,
    BassPulse = 1,
    BeatShift = 2
}

public enum TelemetrySource
{
    Cpu = 0,
    Gpu = 1,
    MaxCpuGpu = 2
}

public sealed class GradientStopConfig
{
    public double Position { get; set; }
    public string ColorHex { get; set; } = "#00F0FF";

    public GradientStopConfig() { }
    public GradientStopConfig(double position, string colorHex)
    {
        Position = Math.Clamp(position, 0.0, 1.0);
        ColorHex = colorHex;
    }

    public GradientStopConfig Clone() => new(Position, ColorHex);
}

public sealed class EffectParameters
{
    // Solid Color
    public string PrimaryColorHex { get; set; } = "#00F0FF";
    public string SecondaryColorHex { get; set; } = "#FF2E93";

    // Multi-Stop Gradient
    public List<GradientStopConfig> GradientStops { get; set; } =
    [
        new(0.0, "#00F0FF"),
        new(0.5, "#7B61FF"),
        new(1.0, "#FF2E93")
    ];
    public double GradientAngleDegrees { get; set; } = 0;
    public bool AnimateGradient { get; set; } = true;

    // Shared motion controls (Rainbow Wave / Spiral / Gradient)
    public double Speed { get; set; } = 1.0;          // 0.0 .. 5.0
    public double Scale { get; set; } = 1.0;          // 0.25 .. 4.0
    public double DirectionDegrees { get; set; } = 0; // 0 .. 360
    public bool ReverseDirection { get; set; }

    // Screen Ambilight (DXGI)
    public double AmbilightSmoothing { get; set; } = 0.28;   // 0.05 .. 0.95 (higher = snappier)
    public double AmbilightSaturation { get; set; } = 1.35;  // 0.5 .. 2.0
    public double AmbilightEdgeBias { get; set; } = 0.4;     // 0.0 .. 1.0
    public int CaptureMonitorIndex { get; set; } = 0;

    // Audio Visualizer (WASAPI + FFT)
    public AudioVisualizerMode AudioMode { get; set; } = AudioVisualizerMode.SpectrumBars;
    public double AudioSensitivity { get; set; } = 1.6;      // 0.5 .. 4.0
    public string AudioLowColorHex { get; set; } = "#00F0FF";
    public string AudioMidColorHex { get; set; } = "#7B61FF";
    public string AudioHighColorHex { get; set; } = "#FF2E93";

    // Hardware Telemetry (LibreHardwareMonitor)
    public TelemetrySource TelemetrySource { get; set; } = TelemetrySource.Cpu;
    public double CoolTempC { get; set; } = 40.0;
    public double WarmTempC { get; set; } = 65.0;
    public double HotTempC { get; set; } = 80.0;
    public string CoolColorHex { get; set; } = "#D7F4FF";    // Ice White
    public string WarmColorHex { get; set; } = "#FFAE00";    // Amber
    public string HotColorHex { get; set; } = "#FF183A";     // Crimson
    public bool TelemetryPulseAtHot { get; set; } = true;

    public EffectParameters Clone() => new()
    {
        PrimaryColorHex = PrimaryColorHex,
        SecondaryColorHex = SecondaryColorHex,
        GradientStops = GradientStops.Select(s => s.Clone()).ToList(),
        GradientAngleDegrees = GradientAngleDegrees,
        AnimateGradient = AnimateGradient,
        Speed = Speed,
        Scale = Scale,
        DirectionDegrees = DirectionDegrees,
        ReverseDirection = ReverseDirection,
        AmbilightSmoothing = AmbilightSmoothing,
        AmbilightSaturation = AmbilightSaturation,
        AmbilightEdgeBias = AmbilightEdgeBias,
        CaptureMonitorIndex = CaptureMonitorIndex,
        AudioMode = AudioMode,
        AudioSensitivity = AudioSensitivity,
        AudioLowColorHex = AudioLowColorHex,
        AudioMidColorHex = AudioMidColorHex,
        AudioHighColorHex = AudioHighColorHex,
        TelemetrySource = TelemetrySource,
        CoolTempC = CoolTempC,
        WarmTempC = WarmTempC,
        HotTempC = HotTempC,
        CoolColorHex = CoolColorHex,
        WarmColorHex = WarmColorHex,
        HotColorHex = HotColorHex,
        TelemetryPulseAtHot = TelemetryPulseAtHot
    };
}
