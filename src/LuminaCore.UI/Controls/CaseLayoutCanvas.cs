using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuminaCore.Core.Engine;
using LuminaCore.Core.Models;

namespace LuminaCore.UI.Controls;

/// <summary>
/// SignalRGB-style interactive 2D Lian Li O11 Dynamic Mini V2 case layout canvas.
/// Renders the live 60 FPS shader surface underneath, draws the O11D Mini V2 case chassis overlay,
/// renders every individual virtual LED with its live sampled RGB glow, and supports dragging,
/// resizing, and rotating all 3 ARGB zones directly with the mouse.
/// </summary>
public sealed class CaseLayoutCanvas : FrameworkElement
{
    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(CaseLayoutCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty =
        DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(CaseLayoutCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SnapToGridProperty =
        DependencyProperty.Register(nameof(SnapToGrid), typeof(bool), typeof(CaseLayoutCanvas),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty SelectedZoneIndexProperty =
        DependencyProperty.Register(nameof(SelectedZoneIndex), typeof(int), typeof(CaseLayoutCanvas),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CanvasLabelProperty =
        DependencyProperty.Register(nameof(CanvasLabel), typeof(string), typeof(CaseLayoutCanvas),
            new FrameworkPropertyMetadata("2D ARGB CANVAS  •  DRAG & SAMPLE", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowChassisOverlayProperty =
        DependencyProperty.Register(nameof(ShowChassisOverlay), typeof(bool), typeof(CaseLayoutCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public string CanvasLabel
    {
        get => (string)GetValue(CanvasLabelProperty);
        set => SetValue(CanvasLabelProperty, value);
    }

    public bool ShowChassisOverlay
    {
        get => (bool)GetValue(ShowChassisOverlayProperty);
        set => SetValue(ShowChassisOverlayProperty, value);
    }

    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool SnapToGrid
    {
        get => (bool)GetValue(SnapToGridProperty);
        set => SetValue(SnapToGridProperty, value);
    }

    public int SelectedZoneIndex
    {
        get => (int)GetValue(SelectedZoneIndexProperty);
        set => SetValue(SelectedZoneIndexProperty, value);
    }

    public event Action<ZoneLayout>? ZoneGeometryChanged;
    public event Action<int>? ZoneSelected;

    private readonly WriteableBitmap _shaderBitmap;
    private readonly Int32Rect _bitmapRect;
    private List<ZoneLayout> _zones = ZoneLayout.CreateDefaultO11MiniV2Layout();
    private readonly Dictionary<int, RgbColor[]> _zoneColors = new();
    private (float U, float V)[] _coordScratch = new (float, float)[128];

    private enum DragMode { None, Move, Resize, Rotate }
    private DragMode _dragMode = DragMode.None;
    private int _dragZoneIdx = -1;
    private Point _dragStartMouse;
    private double _origX, _origY, _origW, _origH, _origRot;
    private int _hoverZoneIdx = -1;

    // Cached frozen pens/brushes for low GC at 60 FPS
    private static readonly Brush CaseGlassTint = CreateFrozenBrush(Color.FromArgb(52, 8, 10, 16));
    private static readonly Brush MotherboardFill = CreateFrozenBrush(Color.FromArgb(95, 16, 19, 28));
    private static readonly Brush DarkLabelBg = CreateFrozenBrush(Color.FromArgb(205, 10, 12, 18));
    private static readonly Pen CaseOuterPen = CreateFrozenPen(Color.FromArgb(180, 70, 82, 108), 2.0);
    private static readonly Pen CaseInnerPen = CreateFrozenPen(Color.FromArgb(100, 110, 128, 160), 1.0);
    private static readonly Pen GridPen = CreateFrozenPen(Color.FromArgb(30, 255, 255, 255), 0.75);
    private static readonly Pen MoboPen = CreateFrozenPen(Color.FromArgb(115, 0, 240, 255), 1.0);
    private static readonly Pen HandlePen = CreateFrozenPen(Color.FromArgb(255, 255, 255, 255), 1.5);
    private static readonly Pen LedRimPen = CreateFrozenPen(Color.FromArgb(175, 255, 255, 255), 0.75);
    private static readonly Brush WatermarkBrush = CreateFrozenBrush(Color.FromArgb(135, 195, 210, 235));
    private static readonly Typeface UiTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface MonoTypeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    // Reusable per-zone LED brush pools to eliminate WPF Freezable churn during 30/60 FPS repaints
    private readonly Dictionary<int, (SolidColorBrush[] Cores, SolidColorBrush[] Halos)> _ledBrushPools = new();

    public CaseLayoutCanvas()
    {
        _shaderBitmap = new WriteableBitmap(
            CanvasSurface.DefaultWidth,
            CanvasSurface.DefaultHeight,
            96, 96,
            PixelFormats.Bgra32,
            null);
        _bitmapRect = new Int32Rect(0, 0, CanvasSurface.DefaultWidth, CanvasSurface.DefaultHeight);
        ClipToBounds = true;
        Focusable = true;
    }

    private static SolidColorBrush CreateFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen CreateFrozenPen(Color c, double thickness)
    {
        var p = new Pen(CreateFrozenBrush(c), thickness);
        p.Freeze();
        return p;
    }

    public void SetZones(IReadOnlyList<ZoneLayout> zones)
    {
        _zones = zones.Select(z => z.Clone()).ToList();
        InvalidateVisual();
    }

    public void UpdateLiveFrame(byte[] pixelsBgra, Dictionary<int, RgbColor[]> zoneColors)
    {
        if (pixelsBgra.Length >= CanvasSurface.DefaultWidth * CanvasSurface.DefaultHeight * 4)
        {
            _shaderBitmap.WritePixels(_bitmapRect, pixelsBgra, CanvasSurface.DefaultWidth * 4, 0);
        }

        foreach (var kvp in zoneColors)
        {
            if (!_zoneColors.TryGetValue(kvp.Key, out var dst) || dst.Length != kvp.Value.Length)
            {
                dst = new RgbColor[kvp.Value.Length];
                _zoneColors[kvp.Key] = dst;
            }
            Array.Copy(kvp.Value, dst, kvp.Value.Length);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 4 || h <= 4) return;

        // 1. Live 60 FPS 2D Shader Surface
        dc.DrawImage(_shaderBitmap, new Rect(0, 0, w, h));

        // 2. Subtle matte-dark glass overlay so LEDs and case layout pop crisply
        dc.DrawRectangle(CaseGlassTint, null, new Rect(0, 0, w, h));

        // 3. Optional alignment grid
        if (ShowGrid)
        {
            const int cols = 16;
            const int rows = 9;
            for (int c = 1; c < cols; c++)
            {
                double x = w * c / cols;
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            }
            for (int r = 1; r < rows; r++)
            {
                double y = h * r / rows;
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            }
        }

        // 4. Optional PC chassis reference outline
        if (ShowChassisOverlay)
        {
            DrawGenericChassisOutline(dc, w, h);
        }

        // 5. Render each ARGB Zone box, virtual LEDs, and interactive drag/resize/rotate handles
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var zone in _zones)
        {
            DrawZone(dc, zone, w, h, dpi);
        }
    }

    private static void DrawGenericChassisOutline(DrawingContext dc, double w, double h)
    {
        // Clean generic PC case / desk frame reference
        var chassisRect = new Rect(w * 0.04, h * 0.05, w * 0.92, h * 0.90);
        dc.DrawRoundedRectangle(null, CaseOuterPen, chassisRect, 10, 10);
        dc.DrawRoundedRectangle(MotherboardFill, MoboPen, new Rect(w * 0.18, h * 0.22, w * 0.44, h * 0.54), 6, 6);
    }

    private void DrawZone(DrawingContext dc, ZoneLayout zone, double w, double h, double dpi)
    {
        bool isSelected = zone.ZoneIndex == SelectedZoneIndex;
        bool isHovered = zone.ZoneIndex == _hoverZoneIdx;
        var accentRgb = RgbColor.FromHex(zone.AccentHex, RgbColor.NeonCyan);
        var accentColor = Color.FromRgb(accentRgb.R, accentRgb.G, accentRgb.B);

        double cx = zone.X * w;
        double cy = zone.Y * h;
        double zw = zone.Width * w;
        double zh = zone.Height * h;
        var rect = new Rect(cx - zw * 0.5, cy - zh * 0.5, zw, zh);

        // Draw rotated bounding box
        dc.PushTransform(new RotateTransform(zone.RotationDegrees, cx, cy));
        try
        {
            byte fillAlpha = !zone.IsEnabled ? (byte)20 : (isSelected && IsInteractive ? (byte)48 : (isHovered ? (byte)36 : (byte)24));
            var boxFill = new SolidColorBrush(Color.FromArgb(fillAlpha, accentColor.R, accentColor.G, accentColor.B));
            var borderBrush = new SolidColorBrush(Color.FromArgb(
                !zone.IsEnabled ? (byte)90 : (isSelected ? (byte)245 : (byte)160),
                accentColor.R, accentColor.G, accentColor.B));
            var borderPen = new Pen(borderBrush, isSelected && IsInteractive ? 2.2 : 1.3)
            {
                DashStyle = zone.IsEnabled ? DashStyles.Solid : DashStyles.Dash
            };

            dc.DrawRoundedRectangle(boxFill, borderPen, rect, 8, 8);

            // Show compact zone badge only in interactive Layout Editor mode
            if (IsInteractive)
            {
                string tag = $"Z{zone.ZoneIndex + 1} • {zone.LedCount}";
                var tagText = new FormattedText(tag, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiTypeface, 10, Brushes.White, dpi);
                var tagBg = new Rect(rect.Left + 6, rect.Top + 5, tagText.Width + 10, tagText.Height + 3);
                dc.DrawRoundedRectangle(DarkLabelBg, new Pen(borderBrush, 0.9), tagBg, 4, 4);
                dc.DrawText(tagText, new Point(tagBg.Left + 5, tagBg.Top + 1.5));
            }

            // Interactive resize & rotate handles for selected zone
            if (IsInteractive && isSelected)
            {
                // Bottom-right resize handle
                var resizePt = new Point(rect.Right, rect.Bottom);
                dc.DrawRectangle(borderBrush, HandlePen, new Rect(resizePt.X - 6, resizePt.Y - 6, 12, 12));

                // Top-center rotate handle connected by stem
                var stemTop = new Point(cx, rect.Top - 20);
                dc.DrawLine(new Pen(borderBrush, 1.5), new Point(cx, rect.Top), stemTop);
                dc.DrawEllipse(borderBrush, HandlePen, stemTop, 6, 6);
            }
        }
        finally
        {
            dc.Pop();
        }

        // Draw every individual virtual LED node at its world (u, v) canvas coordinate with live sampled glow!
        int ledCount = Math.Clamp(zone.LedCount, 1, 256);
        if (_coordScratch.Length < ledCount)
            _coordScratch = new (float, float)[ledCount];

        zone.ComputeLedCoordinates(_coordScratch.AsSpan(0, ledCount));
        _zoneColors.TryGetValue(zone.ZoneIndex, out var sampled);

        if (!_ledBrushPools.TryGetValue(zone.ZoneIndex, out var pool) || pool.Cores.Length != ledCount)
        {
            var cores = new SolidColorBrush[ledCount];
            var halos = new SolidColorBrush[ledCount];
            for (int i = 0; i < ledCount; i++)
            {
                cores[i] = new SolidColorBrush(Colors.Black);
                halos[i] = new SolidColorBrush(Colors.Transparent);
            }
            pool = (cores, halos);
            _ledBrushPools[zone.ZoneIndex] = pool;
        }

        double baseRadius = Math.Clamp(Math.Min(w, h) * 0.011, 3.0, 5.5);

        for (int i = 0; i < ledCount; i++)
        {
            var (u, v) = _coordScratch[i];
            var pt = new Point(u * w, v * h);

            RgbColor ledRgb = (!zone.IsEnabled || sampled is null || i >= sampled.Length)
                ? RgbColor.Black
                : sampled[i];

            var c = Color.FromRgb(ledRgb.R, ledRgb.G, ledRgb.B);

            // Soft outer neon halo using pooled brush
            if (zone.IsEnabled && (ledRgb.R + ledRgb.G + ledRgb.B) > 15)
            {
                pool.Halos[i].Color = Color.FromArgb(95, c.R, c.G, c.B);
                dc.DrawEllipse(pool.Halos[i], null, pt, baseRadius * 2.15, baseRadius * 2.15);
            }

            // Crisp LED diode core using pooled brush & frozen rim pen
            pool.Cores[i].Color = c;
            dc.DrawEllipse(pool.Cores[i], LedRimPen, pt, baseRadius, baseRadius);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInteractive) return;

        var pos = e.GetPosition(this);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // 1. Check handles of currently selected zone first
        var selected = _zones.FirstOrDefault(z => z.ZoneIndex == SelectedZoneIndex);
        if (selected is not null)
        {
            var local = ToZoneLocal(pos, selected, w, h);
            double halfW = selected.Width * w * 0.5;
            double halfH = selected.Height * h * 0.5;

            // Rotate handle at (0, -halfH - 20)
            if (Distance(local, new Point(0, -halfH - 20)) <= 14)
            {
                BeginDrag(DragMode.Rotate, selected, pos);
                return;
            }

            // Resize handle at (+halfW, +halfH)
            if (Distance(local, new Point(halfW, halfH)) <= 14)
            {
                BeginDrag(DragMode.Resize, selected, pos);
                return;
            }
        }

        // 2. Hit-test zones (reverse order so top-most wins)
        for (int i = _zones.Count - 1; i >= 0; i--)
        {
            var zone = _zones[i];
            var local = ToZoneLocal(pos, zone, w, h);
            double halfW = zone.Width * w * 0.5;
            double halfH = zone.Height * h * 0.5;

            if (Math.Abs(local.X) <= halfW && Math.Abs(local.Y) <= halfH)
            {
                SelectedZoneIndex = zone.ZoneIndex;
                ZoneSelected?.Invoke(zone.ZoneIndex);
                BeginDrag(DragMode.Move, zone, pos);
                InvalidateVisual();
                return;
            }
        }
    }

    private void BeginDrag(DragMode mode, ZoneLayout zone, Point mousePos)
    {
        _dragMode = mode;
        _dragZoneIdx = zone.ZoneIndex;
        _dragStartMouse = mousePos;
        _origX = zone.X;
        _origY = zone.Y;
        _origW = zone.Width;
        _origH = zone.Height;
        _origRot = zone.RotationDegrees;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsInteractive) return;

        var pos = e.GetPosition(this);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (_dragMode == DragMode.None)
        {
            UpdateHoverAndCursor(pos, w, h);
            return;
        }

        var zone = _zones.FirstOrDefault(z => z.ZoneIndex == _dragZoneIdx);
        if (zone is null) return;

        switch (_dragMode)
        {
            case DragMode.Move:
            {
                double dx = (pos.X - _dragStartMouse.X) / w;
                double dy = (pos.Y - _dragStartMouse.Y) / h;
                double minX = Math.Min(0.45, zone.Width * 0.5 + 0.015);
                double minY = Math.Min(0.45, zone.Height * 0.5 + 0.015);
                double nx = Math.Clamp(_origX + dx, minX, 1.0 - minX);
                double ny = Math.Clamp(_origY + dy, minY, 1.0 - minY);
                if (SnapToGrid)
                {
                    nx = Math.Clamp(Math.Round(nx * 16.0) / 16.0, minX, 1.0 - minX);
                    ny = Math.Clamp(Math.Round(ny * 9.0) / 9.0, minY, 1.0 - minY);
                }
                zone.X = nx;
                zone.Y = ny;
                break;
            }

            case DragMode.Resize:
            {
                var local = ToZoneLocal(pos, zone, w, h);
                double maxW = Math.Min(0.94, Math.Min(zone.X, 1.0 - zone.X) * 2.0 - 0.02);
                double maxH = Math.Min(0.90, Math.Min(zone.Y, 1.0 - zone.Y) * 2.0 - 0.02);
                double nw = Math.Clamp(Math.Abs(local.X) * 2.0 / w, 0.08, Math.Max(0.08, maxW));
                double nh = Math.Clamp(Math.Abs(local.Y) * 2.0 / h, 0.07, Math.Max(0.07, maxH));
                if (SnapToGrid)
                {
                    nw = Math.Clamp(Math.Round(nw * 16.0) / 16.0, 0.08, Math.Max(0.08, maxW));
                    nh = Math.Clamp(Math.Round(nh * 9.0) / 9.0, 0.07, Math.Max(0.07, maxH));
                }
                zone.Width = nw;
                zone.Height = nh;
                break;
            }

            case DragMode.Rotate:
            {
                double cx = zone.X * w;
                double cy = zone.Y * h;
                double angleDeg = Math.Atan2(pos.Y - cy, pos.X - cx) * (180.0 / Math.PI) + 90.0;
                while (angleDeg > 180) angleDeg -= 360;
                while (angleDeg < -180) angleDeg += 360;
                if (SnapToGrid)
                    angleDeg = Math.Round(angleDeg / 15.0) * 15.0;
                zone.RotationDegrees = Math.Round(angleDeg, 1);
                break;
            }
        }

        ZoneGeometryChanged?.Invoke(zone.Clone());
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            _dragZoneIdx = -1;
            ReleaseMouseCapture();
        }
    }

    private void UpdateHoverAndCursor(Point pos, double w, double h)
    {
        var selected = _zones.FirstOrDefault(z => z.ZoneIndex == SelectedZoneIndex);
        if (selected is not null)
        {
            var local = ToZoneLocal(pos, selected, w, h);
            double halfW = selected.Width * w * 0.5;
            double halfH = selected.Height * h * 0.5;

            if (Distance(local, new Point(0, -halfH - 20)) <= 14)
            {
                Cursor = Cursors.Hand;
                return;
            }
            if (Distance(local, new Point(halfW, halfH)) <= 14)
            {
                Cursor = Cursors.SizeNWSE;
                return;
            }
        }

        int hit = -1;
        for (int i = _zones.Count - 1; i >= 0; i--)
        {
            var z = _zones[i];
            var local = ToZoneLocal(pos, z, w, h);
            if (Math.Abs(local.X) <= z.Width * w * 0.5 && Math.Abs(local.Y) <= z.Height * h * 0.5)
            {
                hit = z.ZoneIndex;
                break;
            }
        }

        Cursor = hit >= 0 ? Cursors.SizeAll : Cursors.Arrow;
        if (hit != _hoverZoneIdx)
        {
            _hoverZoneIdx = hit;
            InvalidateVisual();
        }
    }

    private static Point ToZoneLocal(Point worldPt, ZoneLayout zone, double w, double h)
    {
        double dx = worldPt.X - zone.X * w;
        double dy = worldPt.Y - zone.Y * h;
        double rad = -zone.RotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        return new Point(dx * cos - dy * sin, dx * sin + dy * cos);
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
