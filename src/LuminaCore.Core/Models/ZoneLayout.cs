namespace LuminaCore.Core.Models;

public enum ZoneLedArrangement
{
    HorizontalStrip = 0,
    VerticalStrip = 1,
    TopRadiatorAndPump = 2,
    SideAndRearCluster = 3,
    Ring = 4
}

public enum ZoneDeviceType
{
    Fan = 0,
    LedStrip = 1,
    Pump = 2,
    Motherboard = 3
}

public enum ChannelColorOrder
{
    GRB = 0,
    RGB = 1,
    BRG = 2,
    RBG = 3
}

/// <summary>
/// Represents a physical ARGB header/zone placed on the normalized 2D case layout canvas [0..1, 0..1].
/// </summary>
public sealed class ZoneLayout
{
    public int ZoneIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HardwareDescription { get; set; } = string.Empty;
    public int LedCount { get; set; } = 24;

    /// <summary>Center X in normalized canvas coordinates [0..1].</summary>
    public double X { get; set; } = 0.5;

    /// <summary>Center Y in normalized canvas coordinates [0..1].</summary>
    public double Y { get; set; } = 0.5;

    /// <summary>Width in normalized canvas coordinates [0.05..1.0].</summary>
    public double Width { get; set; } = 0.5;

    /// <summary>Height in normalized canvas coordinates [0.05..1.0].</summary>
    public double Height { get; set; } = 0.14;

    /// <summary>Rotation in degrees around the zone center (-180..180).</summary>
    public double RotationDegrees { get; set; }

    public ZoneLedArrangement Arrangement { get; set; } = ZoneLedArrangement.HorizontalStrip;
    public ZoneDeviceType DeviceType { get; set; } = ZoneDeviceType.Fan;
    public ChannelColorOrder ColorOrder { get; set; } = ChannelColorOrder.GRB;
    public bool IsEnabled { get; set; } = true;
    public bool IsSynced { get; set; } = true;
    public EffectType IndependentEffect { get; set; } = EffectType.Solid;
    public string IndependentColorHex { get; set; } = "#00E5FF";
    public string AccentHex { get; set; } = "#00E5FF";

    /// <summary>
    /// Computes the normalized 2D canvas coordinate (u, v) in [0..1] for each LED index in this zone,
    /// applying zone position, dimensions, arrangement geometry, and rotation.
    /// </summary>
    public void ComputeLedCoordinates(Span<(float U, float V)> destination)
    {
        int count = Math.Min(LedCount, destination.Length);
        if (count <= 0) return;

        float rad = (float)(RotationDegrees * Math.PI / 180.0);
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        float cx = (float)X;
        float cy = (float)Y;
        float halfW = (float)(Width * 0.5);
        float halfH = (float)(Height * 0.5);

        for (int i = 0; i < count; i++)
        {
            // Local coordinates in [-1..1, -1..1] relative to zone bounding box
            (float lx, float ly) = GetLocalLedPosition(i, count, Arrangement);

            float dx = lx * halfW;
            float dy = ly * halfH;

            float rx = dx * cos - dy * sin;
            float ry = dx * sin + dy * cos;

            destination[i] = (
                Math.Clamp(cx + rx, 0f, 1f),
                Math.Clamp(cy + ry, 0f, 1f));
        }
    }

    private static (float Lx, float Ly) GetLocalLedPosition(int index, int total, ZoneLedArrangement arrangement)
    {
        float t = total <= 1 ? 0.5f : index / (float)(total - 1);

        switch (arrangement)
        {
            case ZoneLedArrangement.HorizontalStrip:
                // 3 fans laid horizontally: slight wave inside each fan ring
                return (-0.92f + t * 1.84f, 0f);

            case ZoneLedArrangement.VerticalStrip:
                return (0f, -0.92f + t * 1.84f);

            case ZoneLedArrangement.TopRadiatorAndPump:
            {
                // First 75% of LEDs span the 3 top radiator fans; last 25% form the AIO pump ring below center
                int radCount = Math.Max(1, (int)(total * 0.75f));
                if (index < radCount)
                {
                    float rt = radCount <= 1 ? 0.5f : index / (float)(radCount - 1);
                    return (-0.92f + rt * 1.84f, -0.55f);
                }
                else
                {
                    int pumpIndex = index - radCount;
                    int pumpTotal = Math.Max(1, total - radCount);
                    float angle = (pumpIndex / (float)pumpTotal) * MathF.PI * 2f;
                    return (MathF.Cos(angle) * 0.18f, 0.55f + MathF.Sin(angle) * 0.38f);
                }
            }

            case ZoneLedArrangement.SideAndRearCluster:
            {
                // First 66% on the 2 vertical side fans (right side), last 34% on rear exhaust fan (left side)
                int sideCount = Math.Max(1, (int)(total * 0.66f));
                if (index < sideCount)
                {
                    float st = sideCount <= 1 ? 0.5f : index / (float)(sideCount - 1);
                    return (0.85f, -0.85f + st * 1.70f);
                }
                else
                {
                    int rearIndex = index - sideCount;
                    int rearTotal = Math.Max(1, total - sideCount);
                    float angle = (rearIndex / (float)rearTotal) * MathF.PI * 2f;
                    return (-0.84f + MathF.Cos(angle) * 0.14f, -0.15f + MathF.Sin(angle) * 0.34f);
                }
            }

            case ZoneLedArrangement.Ring:
            {
                float angle = (index / (float)total) * MathF.PI * 2f;
                return (MathF.Cos(angle) * 0.85f, MathF.Sin(angle) * 0.85f);
            }

            default:
                return (-0.92f + t * 1.84f, 0f);
        }
    }

    public ZoneLayout Clone() => new()
    {
        ZoneIndex = ZoneIndex,
        Name = Name,
        HardwareDescription = HardwareDescription,
        LedCount = LedCount,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        RotationDegrees = RotationDegrees,
        Arrangement = Arrangement,
        DeviceType = DeviceType,
        ColorOrder = ColorOrder,
        IsEnabled = IsEnabled,
        IsSynced = IsSynced,
        IndependentEffect = IndependentEffect,
        IndependentColorHex = IndependentColorHex,
        AccentHex = AccentHex
    };

    public static List<ZoneLayout> CreateDefaultLayout() => CreateGeneric3ZoneLayout();

    public static List<ZoneLayout> CreateGeneric3ZoneLayout() =>
    [
        new ZoneLayout
        {
            ZoneIndex = 0,
            Name = "ARGB Header 1",
            HardwareDescription = "Channel 1",
            LedCount = 24,
            X = 0.50,
            Y = 0.22,
            Width = 0.64,
            Height = 0.16,
            RotationDegrees = 0,
            Arrangement = ZoneLedArrangement.HorizontalStrip,
            IsEnabled = true,
            AccentHex = "#00F0FF"
        },
        new ZoneLayout
        {
            ZoneIndex = 1,
            Name = "ARGB Header 2",
            HardwareDescription = "Channel 2",
            LedCount = 24,
            X = 0.50,
            Y = 0.50,
            Width = 0.64,
            Height = 0.16,
            RotationDegrees = 0,
            Arrangement = ZoneLedArrangement.HorizontalStrip,
            IsEnabled = true,
            AccentHex = "#7B61FF"
        },
        new ZoneLayout
        {
            ZoneIndex = 2,
            Name = "ARGB Header 3",
            HardwareDescription = "Channel 3",
            LedCount = 16,
            X = 0.50,
            Y = 0.78,
            Width = 0.64,
            Height = 0.16,
            RotationDegrees = 0,
            Arrangement = ZoneLedArrangement.HorizontalStrip,
            IsEnabled = true,
            AccentHex = "#FF2E93"
        }
    ];

    /// <summary>
    /// Automatically builds a 2D canvas layout from a real OpenRGB device's discovered zones and LED counts.
    /// </summary>
    public static List<ZoneLayout> CreateFromHardwareDevice(HardwareDeviceInfo device)
    {
        if (device.Zones is not { Count: > 0 })
            return CreateGeneric3ZoneLayout();

        string[] accents = ["#00F0FF", "#7B61FF", "#FF2E93", "#00FF9C", "#FFAE00", "#38BDF8"];
        var result = new List<ZoneLayout>(device.Zones.Count);
        int count = device.Zones.Count;

        for (int i = 0; i < count; i++)
        {
            var hwZone = device.Zones[i];
            int leds = hwZone.LedCount > 0 ? hwZone.LedCount : 12;
            double y = count == 1 ? 0.5 : 0.18 + (0.64 * i / (count - 1));
            double h = Math.Clamp(0.60 / Math.Max(1, count), 0.10, 0.24);

            result.Add(new ZoneLayout
            {
                ZoneIndex = hwZone.Index,
                Name = string.IsNullOrWhiteSpace(hwZone.Name) ? $"Zone {hwZone.Index + 1}" : hwZone.Name,
                HardwareDescription = $"{device.Name} • {leds} LEDs",
                LedCount = leds,
                X = 0.50,
                Y = Math.Round(y, 3),
                Width = 0.68,
                Height = Math.Round(h, 3),
                RotationDegrees = 0,
                Arrangement = leds <= 12 ? ZoneLedArrangement.Ring : ZoneLedArrangement.HorizontalStrip,
                IsEnabled = true,
                AccentHex = accents[i % accents.Length]
            });
        }

        return result;
    }

    /// <summary>
    /// Optional dual-chamber showcase preset (e.g. O11D Mini V2).
    /// </summary>
    public static List<ZoneLayout> CreateDefaultO11MiniV2Layout() =>
    [
        new ZoneLayout
        {
            ZoneIndex = 0,
            Name = "Zone 1: Top",
            HardwareDescription = "Top Radiator / Fans",
            LedCount = 24,
            X = 0.47,
            Y = 0.22,
            Width = 0.62,
            Height = 0.22,
            RotationDegrees = 0,
            Arrangement = ZoneLedArrangement.HorizontalStrip,
            IsEnabled = true,
            AccentHex = "#00F0FF"
        },
        new ZoneLayout
        {
            ZoneIndex = 1,
            Name = "Zone 2: Bottom",
            HardwareDescription = "Bottom Intake Fans",
            LedCount = 24,
            X = 0.47,
            Y = 0.84,
            Width = 0.62,
            Height = 0.14,
            RotationDegrees = 0,
            Arrangement = ZoneLedArrangement.HorizontalStrip,
            IsEnabled = true,
            AccentHex = "#7B61FF"
        },
        new ZoneLayout
        {
            ZoneIndex = 2,
            Name = "Zone 3: Side & Rear",
            HardwareDescription = "Side & Rear Fans",
            LedCount = 24,
            X = 0.50,
            Y = 0.52,
            Width = 0.82,
            Height = 0.44,
            RotationDegrees = 0,
            Arrangement = ZoneLedArrangement.SideAndRearCluster,
            IsEnabled = true,
            AccentHex = "#FF2E93"
        }
    ];
}
