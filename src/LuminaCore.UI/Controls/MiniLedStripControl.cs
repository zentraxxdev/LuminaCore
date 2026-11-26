using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuminaCore.Core.Models;

namespace LuminaCore.UI.Controls;

/// <summary>
/// Allocation-free mini live LED preview strip rendered inside each detected hardware channel card.
/// </summary>
public sealed class MiniLedStripControl : FrameworkElement
{
    public static readonly DependencyProperty ColorsProperty =
        DependencyProperty.Register(nameof(Colors), typeof(IReadOnlyList<RgbColor>), typeof(MiniLedStripControl),
            new FrameworkPropertyMetadata(null, OnColorsChanged));

    public IReadOnlyList<RgbColor>? Colors
    {
        get => (IReadOnlyList<RgbColor>?)GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    private const int StripWidth = 24;
    private readonly WriteableBitmap _bitmap = new(StripWidth, 1, 96, 96, PixelFormats.Bgra32, null);
    private readonly byte[] _pixels = new byte[StripWidth * 4];
    private readonly Int32Rect _rect = new(0, 0, StripWidth, 1);

    public MiniLedStripControl()
    {
        Height = 7;
        ClipToBounds = true;
    }

    private static void OnColorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MiniLedStripControl)d).UpdatePixels(e.NewValue as IReadOnlyList<RgbColor>);
    }

    private void UpdatePixels(IReadOnlyList<RgbColor>? colors)
    {
        if (colors is null || colors.Count == 0)
        {
            for (int i = 0; i < StripWidth; i++)
            {
                int idx = i * 4;
                _pixels[idx] = 28;
                _pixels[idx + 1] = 22;
                _pixels[idx + 2] = 18;
                _pixels[idx + 3] = 255;
            }
        }
        else
        {
            for (int i = 0; i < StripWidth; i++)
            {
                int srcIdx = StripWidth <= 1 ? 0 : (int)MathF.Round(i * (colors.Count - 1) / (float)(StripWidth - 1));
                var c = colors[Math.Clamp(srcIdx, 0, colors.Count - 1)];
                int idx = i * 4;
                _pixels[idx] = c.B;
                _pixels[idx + 1] = c.G;
                _pixels[idx + 2] = c.R;
                _pixels[idx + 3] = 255;
            }
        }

        _bitmap.WritePixels(_rect, _pixels, StripWidth * 4, 0);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        var rect = new Rect(0, 0, w, h);
        dc.PushClip(new RectangleGeometry(rect, h * 0.5, h * 0.5));
        dc.DrawImage(_bitmap, rect);
        dc.Pop();
    }
}
