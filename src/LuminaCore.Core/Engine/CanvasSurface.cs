using System.Runtime.CompilerServices;
using LuminaCore.Core.Models;

namespace LuminaCore.Core.Engine;

/// <summary>
/// Allocation-free 2D BGRA32 pixel canvas (default 160x90 = 57.6 KB) rendered at 60 FPS.
/// Both the WPF live preview WriteableBitmap and the 3 ARGB hardware zones sample directly from this surface.
/// </summary>
public sealed class CanvasSurface
{
    public const int DefaultWidth = 160;
    public const int DefaultHeight = 90;

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;
    public byte[] PixelsBgra { get; }

    public CanvasSurface(int width = DefaultWidth, int height = DefaultHeight)
    {
        Width = Math.Max(16, width);
        Height = Math.Max(9, height);
        PixelsBgra = new byte[Width * Height * 4];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, RgbColor color)
    {
        int idx = (y * Width + x) * 4;
        PixelsBgra[idx] = color.B;
        PixelsBgra[idx + 1] = color.G;
        PixelsBgra[idx + 2] = color.R;
        PixelsBgra[idx + 3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RgbColor GetPixel(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int idx = (y * Width + x) * 4;
        return new RgbColor(PixelsBgra[idx + 2], PixelsBgra[idx + 1], PixelsBgra[idx]);
    }

    public void Fill(RgbColor color)
    {
        var span = PixelsBgra.AsSpan();
        for (int i = 0; i < span.Length; i += 4)
        {
            span[i] = color.B;
            span[i + 1] = color.G;
            span[i + 2] = color.R;
            span[i + 3] = 255;
        }
    }

    /// <summary>
    /// Bilinearly samples the 2D canvas at normalized coordinates u, v in [0..1].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RgbColor SampleNormalized(float u, float v)
    {
        float fx = Math.Clamp(u, 0f, 1f) * (Width - 1);
        float fy = Math.Clamp(v, 0f, 1f) * (Height - 1);

        int x0 = (int)fx;
        int y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, Width - 1);
        int y1 = Math.Min(y0 + 1, Height - 1);

        float tx = fx - x0;
        float ty = fy - y0;

        int i00 = (y0 * Width + x0) * 4;
        int i10 = (y0 * Width + x1) * 4;
        int i01 = (y1 * Width + x0) * 4;
        int i11 = (y1 * Width + x1) * 4;

        float w00 = (1f - tx) * (1f - ty);
        float w10 = tx * (1f - ty);
        float w01 = (1f - tx) * ty;
        float w11 = tx * ty;

        byte b = (byte)Math.Clamp(PixelsBgra[i00] * w00 + PixelsBgra[i10] * w10 + PixelsBgra[i01] * w01 + PixelsBgra[i11] * w11, 0f, 255f);
        byte g = (byte)Math.Clamp(PixelsBgra[i00 + 1] * w00 + PixelsBgra[i10 + 1] * w10 + PixelsBgra[i01 + 1] * w01 + PixelsBgra[i11 + 1] * w11, 0f, 255f);
        byte r = (byte)Math.Clamp(PixelsBgra[i00 + 2] * w00 + PixelsBgra[i10 + 2] * w10 + PixelsBgra[i01 + 2] * w01 + PixelsBgra[i11 + 2] * w11, 0f, 255f);

        return new RgbColor(r, g, b);
    }

    public void CopyTo(byte[] destination)
    {
        Buffer.BlockCopy(PixelsBgra, 0, destination, 0, Math.Min(PixelsBgra.Length, destination.Length));
    }
}
