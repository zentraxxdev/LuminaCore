using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuminaCore.Core.Models;

namespace LuminaCore.UI.Controls;

/// <summary>
/// Sleek 2D Saturation-Value color pad + vertical Hue spectrum bar for instant visual color picking.
/// </summary>
public sealed class HsvColorPicker : FrameworkElement
{
    public static readonly DependencyProperty HexColorProperty =
        DependencyProperty.Register(nameof(HexColor), typeof(string), typeof(HsvColorPicker),
            new FrameworkPropertyMetadata("#00F0FF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, OnHexChanged));

    public string HexColor
    {
        get => (string)GetValue(HexColorProperty);
        set => SetValue(HexColorProperty, value);
    }

    private float _hue = 184f;
    private float _sat = 1f;
    private float _val = 1f;
    private bool _internalUpdate;

    private readonly WriteableBitmap _svBitmap = new(128, 128, 96, 96, PixelFormats.Bgra32, null);
    private readonly WriteableBitmap _hueBitmap = new(1, 256, 96, 96, PixelFormats.Bgra32, null);
    private readonly byte[] _svPixels = new byte[128 * 128 * 4];
    private float _renderedHue = -1f;

    private enum DragTarget { None, SvPad, HueBar }
    private DragTarget _dragTarget = DragTarget.None;

    private const double HueBarWidth = 18;
    private const double Gap = 12;

    public HsvColorPicker()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        RenderHueBar();
        RenderSvPad();
    }

    private static void OnHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (HsvColorPicker)d;
        if (picker._internalUpdate) return;

        var rgb = RgbColor.FromHex(e.NewValue as string, RgbColor.NeonCyan);
        rgb.ToHsv(out float h, out float s, out float v);
        if (s > 0.01f) picker._hue = h;
        picker._sat = s;
        picker._val = v;
        picker.RenderSvPad();
        picker.InvalidateVisual();
    }

    private void RenderHueBar()
    {
        var pixels = new byte[256 * 4];
        for (int y = 0; y < 256; y++)
        {
            float hue = y / 255f * 360f;
            var c = RgbColor.FromHsv(hue, 1f, 1f);
            int idx = y * 4;
            pixels[idx] = c.B;
            pixels[idx + 1] = c.G;
            pixels[idx + 2] = c.R;
            pixels[idx + 3] = 255;
        }
        _hueBitmap.WritePixels(new Int32Rect(0, 0, 1, 256), pixels, 4, 0);
    }

    private void RenderSvPad()
    {
        if (MathF.Abs(_renderedHue - _hue) < 0.2f) return;
        _renderedHue = _hue;

        for (int y = 0; y < 128; y++)
        {
            float v = 1f - y / 127f;
            for (int x = 0; x < 128; x++)
            {
                float s = x / 127f;
                var c = RgbColor.FromHsv(_hue, s, v);
                int idx = (y * 128 + x) * 4;
                _svPixels[idx] = c.B;
                _svPixels[idx + 1] = c.G;
                _svPixels[idx + 2] = c.R;
                _svPixels[idx + 3] = 255;
            }
        }
        _svBitmap.WritePixels(new Int32Rect(0, 0, 128, 128), _svPixels, 128 * 4, 0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= HueBarWidth + Gap + 10 || h <= 10) return;

        double padW = w - HueBarWidth - Gap;
        var padRect = new Rect(0, 0, padW, h);
        var hueRect = new Rect(padW + Gap, 0, HueBarWidth, h);

        // Rounded SV pad
        dc.PushClip(new RectangleGeometry(padRect, 8, 8));
        dc.DrawImage(_svBitmap, padRect);
        dc.Pop();

        // Rounded Hue bar
        dc.PushClip(new RectangleGeometry(hueRect, 6, 6));
        dc.DrawImage(_hueBitmap, hueRect);
        dc.Pop();

        // SV thumb
        double tx = _sat * padW;
        double ty = (1.0 - _val) * h;
        dc.DrawEllipse(null, new Pen(Brushes.Black, 2.5), new Point(tx, ty), 6.5, 6.5);
        dc.DrawEllipse(null, new Pen(Brushes.White, 1.75), new Point(tx, ty), 6.5, 6.5);

        // Hue thumb
        double hy = Math.Clamp(_hue / 360.0, 0.0, 1.0) * h;
        dc.DrawRoundedRectangle(Brushes.Transparent, new Pen(Brushes.White, 2),
            new Rect(hueRect.Left - 1, Math.Clamp(hy - 3, 0, h - 6), HueBarWidth + 2, 6), 3, 3);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        double padW = ActualWidth - HueBarWidth - Gap;

        _dragTarget = pos.X >= padW + Gap * 0.5 ? DragTarget.HueBar : DragTarget.SvPad;
        CaptureMouse();
        UpdateFromMouse(pos);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragTarget != DragTarget.None && e.LeftButton == MouseButtonState.Pressed)
            UpdateFromMouse(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragTarget != DragTarget.None)
        {
            _dragTarget = DragTarget.None;
            ReleaseMouseCapture();
        }
    }

    private void UpdateFromMouse(Point pos)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double padW = Math.Max(1, w - HueBarWidth - Gap);

        if (_dragTarget == DragTarget.HueBar)
        {
            _hue = (float)(Math.Clamp(pos.Y / h, 0.0, 1.0) * 359.9);
            RenderSvPad();
        }
        else if (_dragTarget == DragTarget.SvPad)
        {
            _sat = (float)Math.Clamp(pos.X / padW, 0.0, 1.0);
            _val = (float)(1.0 - Math.Clamp(pos.Y / h, 0.0, 1.0));
        }

        _internalUpdate = true;
        HexColor = RgbColor.FromHsv(_hue, _sat, _val).ToHex();
        _internalUpdate = false;

        InvalidateVisual();
    }
}
