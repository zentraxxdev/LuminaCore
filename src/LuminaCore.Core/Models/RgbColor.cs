using System.Globalization;
using System.Runtime.CompilerServices;

namespace LuminaCore.Core.Models;

/// <summary>
/// Allocation-free 24-bit RGB color value used across the 60 FPS lighting pipeline.
/// </summary>
public readonly struct RgbColor : IEquatable<RgbColor>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public RgbColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);
    public static readonly RgbColor NeonCyan = new(0, 240, 255);
    public static readonly RgbColor NeonMagenta = new(255, 46, 147);
    public static readonly RgbColor ElectricViolet = new(123, 97, 255);
    public static readonly RgbColor IceWhite = new(215, 244, 255);
    public static readonly RgbColor Amber = new(255, 174, 0);
    public static readonly RgbColor Crimson = new(255, 24, 58);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RgbColor Lerp(RgbColor a, RgbColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new RgbColor(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RgbColor ScaleBrightness(float factor)
    {
        if (factor >= 0.999f) return this;
        if (factor <= 0.001f) return Black;
        return new RgbColor(
            (byte)Math.Clamp(R * factor, 0f, 255f),
            (byte)Math.Clamp(G * factor, 0f, 255f),
            (byte)Math.Clamp(B * factor, 0f, 255f));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RgbColor BoostSaturation(float saturationMultiplier)
    {
        if (MathF.Abs(saturationMultiplier - 1f) < 0.01f) return this;
        ToHsv(out float h, out float s, out float v);
        s = Math.Clamp(s * saturationMultiplier, 0f, 1f);
        return FromHsv(h, s, v);
    }

    public void ToHsv(out float h, out float s, out float v)
    {
        float r = R / 255f;
        float g = G / 255f;
        float b = B / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        v = max;
        s = max <= 1e-5f ? 0f : delta / max;

        if (delta <= 1e-5f)
        {
            h = 0f;
            return;
        }

        if (max == r)
            h = 60f * (((g - b) / delta) % 6f);
        else if (max == g)
            h = 60f * (((b - r) / delta) + 2f);
        else
            h = 60f * (((r - g) / delta) + 4f);

        if (h < 0f) h += 360f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RgbColor FromHsv(float hueDegrees, float saturation, float value)
    {
        float h = ((hueDegrees % 360f) + 360f) % 360f;
        float s = Math.Clamp(saturation, 0f, 1f);
        float v = Math.Clamp(value, 0f, 1f);

        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;

        float r1, g1, b1;
        if (h < 60f) { r1 = c; g1 = x; b1 = 0f; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0f; }
        else if (h < 180f) { r1 = 0f; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0f; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0f; b1 = c; }
        else { r1 = c; g1 = 0f; b1 = x; }

        return new RgbColor(
            (byte)Math.Clamp((r1 + m) * 255f, 0f, 255f),
            (byte)Math.Clamp((g1 + m) * 255f, 0f, 255f),
            (byte)Math.Clamp((b1 + m) * 255f, 0f, 255f));
    }

    public static RgbColor FromHex(string? hex, RgbColor? fallback = null)
    {
        var def = fallback ?? NeonCyan;
        if (string.IsNullOrWhiteSpace(hex)) return def;

        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 8)
            clean = clean.Substring(2); // strip alpha AARRGGBB -> RRGGBB

        if (clean.Length != 6) return def;

        if (byte.TryParse(clean.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
            byte.TryParse(clean.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
            byte.TryParse(clean.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return new RgbColor(r, g, b);
        }

        return def;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public bool Equals(RgbColor other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is RgbColor other && Equals(other);
    public override int GetHashCode() => (R << 16) | (G << 8) | B;
    public static bool operator ==(RgbColor left, RgbColor right) => left.Equals(right);
    public static bool operator !=(RgbColor left, RgbColor right) => !left.Equals(right);
    public override string ToString() => ToHex();
}
