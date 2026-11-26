namespace LuminaCore.Core.Models;

public sealed class LightingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default Profile";
    public string Description { get; set; } = string.Empty;
    public EffectType Effect { get; set; } = EffectType.RainbowWave;
    public double MasterBrightness { get; set; } = 0.85;
    public EffectParameters Parameters { get; set; } = new();
    public List<ZoneLayout> Zones { get; set; } = [];

    /// <summary>
    /// Optional game executable names (e.g., "cod.exe", "ForzaHorizon5.exe", "RDR2.exe")
    /// that trigger this profile automatically when detected.
    /// </summary>
    public List<string> AssociatedProcesses { get; set; } = [];

    public bool IsDefault { get; set; }

    public LightingProfile Clone(string? newName = null) => new()
    {
        Id = newName is null ? Id : Guid.NewGuid().ToString("N"),
        Name = newName ?? Name,
        Description = Description,
        Effect = Effect,
        MasterBrightness = MasterBrightness,
        Parameters = Parameters.Clone(),
        Zones = Zones.Select(z => z.Clone()).ToList(),
        AssociatedProcesses = [.. AssociatedProcesses],
        IsDefault = newName is null && IsDefault
    };

    public static List<LightingProfile> CreateDefaultProfiles()
    {
        var defaultZones = ZoneLayout.CreateGeneric3ZoneLayout();

        return
        [
            new LightingProfile
            {
                Id = "default-neon-wave",
                Name = "Neon Cyber Wave",
                Description = "Signature LuminaCore cyan-violet-magenta wave across your ARGB zones",
                Effect = EffectType.Gradient,
                MasterBrightness = 0.90,
                IsDefault = true,
                Parameters = new EffectParameters
                {
                    Speed = 0.9,
                    Scale = 1.0,
                    GradientAngleDegrees = 25,
                    AnimateGradient = true,
                    GradientStops =
                    [
                        new(0.0, "#00F0FF"),
                        new(0.45, "#7B61FF"),
                        new(0.82, "#FF2E93"),
                        new(1.0, "#00F0FF")
                    ]
                },
                Zones = defaultZones.Select(z => z.Clone()).ToList(),
                AssociatedProcesses = []
            },
            new LightingProfile
            {
                Id = "game-cod",
                Name = "Call of Duty Tactical",
                Description = "Low-distraction tactical telemetry shifting from Ice White to Amber/Crimson under load",
                Effect = EffectType.HardwareTelemetry,
                MasterBrightness = 0.72,
                Parameters = new EffectParameters
                {
                    TelemetrySource = TelemetrySource.Gpu,
                    CoolTempC = 40,
                    WarmTempC = 65,
                    HotTempC = 80,
                    CoolColorHex = "#D7F4FF",
                    WarmColorHex = "#FFAE00",
                    HotColorHex = "#FF183A"
                },
                Zones = defaultZones.Select(z => z.Clone()).ToList(),
                AssociatedProcesses = ["cod.exe", "ModernWarfare.exe"]
            },
            new LightingProfile
            {
                Id = "game-forza",
                Name = "Forza Horizon Ambilight",
                Description = "60 FPS DXGI screen ambilight matching Mexico sky & sunset horizons",
                Effect = EffectType.ScreenAmbilight,
                MasterBrightness = 0.95,
                Parameters = new EffectParameters
                {
                    AmbilightSmoothing = 0.35,
                    AmbilightSaturation = 1.45,
                    AmbilightEdgeBias = 0.55
                },
                Zones = defaultZones.Select(z => z.Clone()).ToList(),
                AssociatedProcesses = ["ForzaHorizon5.exe"]
            },
            new LightingProfile
            {
                Id = "game-rdr2",
                Name = "RDR2 Frontier Ember",
                Description = "Warm campfire amber and crimson gradient tailored for Red Dead Redemption 2",
                Effect = EffectType.Gradient,
                MasterBrightness = 0.65,
                Parameters = new EffectParameters
                {
                    Speed = 0.45,
                    GradientAngleDegrees = 90,
                    AnimateGradient = true,
                    GradientStops =
                    [
                        new(0.0, "#FF4500"),
                        new(0.5, "#FFB000"),
                        new(1.0, "#8B0018")
                    ]
                },
                Zones = defaultZones.Select(z => z.Clone()).ToList(),
                AssociatedProcesses = ["RDR2.exe"]
            },
            new LightingProfile
            {
                Id = "music-reactive",
                Name = "Bass Reactor",
                Description = "WASAPI FFT audio-reactive spectrum bars and bass shockwaves",
                Effect = EffectType.AudioVisualizer,
                MasterBrightness = 0.92,
                Parameters = new EffectParameters
                {
                    AudioMode = AudioVisualizerMode.SpectrumBars,
                    AudioSensitivity = 1.8,
                    AudioLowColorHex = "#00F0FF",
                    AudioMidColorHex = "#7B61FF",
                    AudioHighColorHex = "#FF2E93"
                },
                Zones = defaultZones.Select(z => z.Clone()).ToList(),
                AssociatedProcesses = ["Spotify.exe"]
            }
        ];
    }
}

public sealed class AppSettings
{
    public string OpenRgbHost { get; set; } = "127.0.0.1";
    public int OpenRgbPort { get; set; } = 6742;
    public int TargetFps { get; set; } = 60;
    public int PreferredDeviceIndex { get; set; } = -1; // -1 = auto-discover motherboard (ASRock A620M Pro RS)
    public string ActiveProfileId { get; set; } = "default-neon-wave";
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool EnableGlobalBlackoutHotkey { get; set; } = true;
    public bool EnableProcessWatcher { get; set; } = true;
    public bool AutoReconnectOpenRgb { get; set; } = true;
    public int TelemetryPollIntervalMs { get; set; } = 1000;
    public bool EnableLocalWebRemote { get; set; }
    public int LocalWebRemotePort { get; set; } = 5050;
    public bool EnableWledSync { get; set; }
    public string WledIpAddress { get; set; } = "192.168.1.100";
    public string Language { get; set; } = "en";
}
