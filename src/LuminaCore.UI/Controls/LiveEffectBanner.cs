using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuminaCore.Core.Engine;

namespace LuminaCore.UI.Controls;

/// <summary>
/// Full-width 64px real-time 60 FPS animated LED preview banner rendering the active shader surface.
/// </summary>
public sealed class LiveEffectBanner : FrameworkElement
{
    private readonly WriteableBitmap _bitmap;
    private readonly Int32Rect _rect;
    private static readonly Brush TopSheenBrush = CreateTopSheen();

    public LiveEffectBanner()
    {
        _bitmap = new WriteableBitmap(
            CanvasSurface.DefaultWidth,
            CanvasSurface.DefaultHeight,
            96, 96,
            PixelFormats.Bgra32,
            null);
        _rect = new Int32Rect(0, 0, CanvasSurface.DefaultWidth, CanvasSurface.DefaultHeight);
        ClipToBounds = true;
    }

    private static LinearGradientBrush CreateTopSheen()
    {
        var b = new LinearGradientBrush(
            Color.FromArgb(45, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            new Point(0, 0),
            new Point(0, 0.55));
        b.Freeze();
        return b;
    }

    public void UpdateFrame(byte[] pixelsBgra)
    {
        if (pixelsBgra.Length >= CanvasSurface.DefaultWidth * CanvasSurface.DefaultHeight * 4)
        {
            _bitmap.WritePixels(_rect, pixelsBgra, CanvasSurface.DefaultWidth * 4, 0);
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        var rect = new Rect(0, 0, w, h);
        dc.DrawImage(_bitmap, rect);
        dc.DrawRectangle(TopSheenBrush, null, rect);
    }
}
