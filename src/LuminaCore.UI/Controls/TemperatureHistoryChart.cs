using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LuminaCore.UI.Controls;

/// <summary>
/// Live 60-second dual-series CPU & GPU temperature history chart rendered directly via WPF DrawingContext.
/// </summary>
public sealed class TemperatureHistoryChart : FrameworkElement
{
    private double[] _cpuSamples = [];
    private double[] _gpuSamples = [];

    private static readonly Pen GridPen = FreezePen(Color.FromArgb(38, 148, 163, 184), 1.0);
    private static readonly Pen CpuPen = FreezePen(Color.FromRgb(0, 229, 255), 2.2);
    private static readonly Pen GpuPen = FreezePen(Color.FromRgb(139, 92, 246), 2.2);
    private static readonly Brush CpuFill = FreezeGradient(Color.FromArgb(72, 0, 229, 255), Color.FromArgb(0, 0, 229, 255));
    private static readonly Brush GpuFill = FreezeGradient(Color.FromArgb(64, 139, 92, 246), Color.FromArgb(0, 139, 92, 246));
    private static readonly Brush AxisTextBrush = FreezeBrush(Color.FromRgb(100, 116, 139));
    private static readonly Typeface AxisTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    public TemperatureHistoryChart()
    {
        ClipToBounds = true;
    }

    public void UpdateSeries(IReadOnlyList<double> cpuHistory, IReadOnlyList<double> gpuHistory)
    {
        _cpuSamples = cpuHistory.ToArray();
        _gpuSamples = gpuHistory.ToArray();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 60 || h < 50) return;

        const double padLeft = 36;
        const double padRight = 10;
        const double padTop = 14;
        const double padBottom = 18;

        double plotW = w - padLeft - padRight;
        double plotH = h - padTop - padBottom;
        if (plotW <= 10 || plotH <= 10) return;

        const double minTemp = 20.0;
        const double maxTemp = 100.0;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Horizontal temperature guide lines (30°, 50°, 70°, 90°)
        foreach (double temp in new[] { 30.0, 50.0, 70.0, 90.0 })
        {
            double y = padTop + plotH * (1.0 - (temp - minTemp) / (maxTemp - minTemp));
            dc.DrawLine(GridPen, new Point(padLeft, y), new Point(w - padRight, y));

            var label = new FormattedText($"{temp:F0}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 10, AxisTextBrush, dpi);
            dc.DrawText(label, new Point(4, y - label.Height * 0.5));
        }

        // Draw GPU curve first, then CPU on top
        DrawCurve(dc, _gpuSamples, padLeft, padTop, plotW, plotH, minTemp, maxTemp, GpuFill, GpuPen);
        DrawCurve(dc, _cpuSamples, padLeft, padTop, plotW, plotH, minTemp, maxTemp, CpuFill, CpuPen);

        // X-axis time hints
        var leftT = new FormattedText("-60s", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 9.5, AxisTextBrush, dpi);
        var rightT = new FormattedText("Now", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 9.5, AxisTextBrush, dpi);
        dc.DrawText(leftT, new Point(padLeft, h - 14));
        dc.DrawText(rightT, new Point(w - padRight - rightT.Width, h - 14));
    }

    private static void DrawCurve(
        DrawingContext dc,
        double[] samples,
        double left, double top, double width, double height,
        double minVal, double maxVal,
        Brush fillBrush, Pen strokePen)
    {
        if (samples.Length < 2) return;

        var strokeGeo = new StreamGeometry();
        var fillGeo = new StreamGeometry();
        double bottomY = top + height;

        using (var sCtx = strokeGeo.Open())
        using (var fCtx = fillGeo.Open())
        {
            for (int i = 0; i < samples.Length; i++)
            {
                double x = left + width * (i / (double)(samples.Length - 1));
                double norm = Math.Clamp((samples[i] - minVal) / (maxVal - minVal), 0.0, 1.0);
                double y = top + height * (1.0 - norm);
                var pt = new Point(x, y);

                if (i == 0)
                {
                    sCtx.BeginFigure(pt, isFilled: false, isClosed: false);
                    fCtx.BeginFigure(new Point(x, bottomY), isFilled: true, isClosed: true);
                    fCtx.LineTo(pt, false, false);
                }
                else
                {
                    sCtx.LineTo(pt, true, true);
                    fCtx.LineTo(pt, false, true);
                }

                if (i == samples.Length - 1)
                {
                    fCtx.LineTo(new Point(x, bottomY), false, false);
                }
            }
        }

        strokeGeo.Freeze();
        fillGeo.Freeze();
        dc.DrawGeometry(fillBrush, null, fillGeo);
        dc.DrawGeometry(null, strokePen, strokeGeo);
    }

    private static SolidColorBrush FreezeBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen FreezePen(Color c, double t) { var p = new Pen(FreezeBrush(c), t); p.Freeze(); return p; }
    private static LinearGradientBrush FreezeGradient(Color top, Color bottom)
    {
        var g = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
        g.Freeze();
        return g;
    }
}
