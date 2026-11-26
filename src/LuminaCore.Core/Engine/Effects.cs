using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;

namespace LuminaCore.Core.Engine;

public sealed class SolidColorEffect : ILightEffect
{
    public EffectType Type => EffectType.Solid;
    public string DisplayName => "Solid Color";
    public string Subtitle => "Static HEX / RGB";
    public string AccentHex => "#00F0FF";

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        var color = RgbColor.FromHex(parameters.PrimaryColorHex, RgbColor.NeonCyan);
        surface.Fill(color);
    }
}

public sealed class MultiStopGradientEffect : ILightEffect
{
    public EffectType Type => EffectType.Gradient;
    public string DisplayName => "Gradient";
    public string Subtitle => "Multi-stop color flow";
    public string AccentHex => "#7B61FF";

    private readonly (float Pos, RgbColor Color)[] _stopScratch = new (float, RgbColor)[16];

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        int stopCount = 0;
        var stops = parameters.GradientStops;
        if (stops != null && stops.Count > 0)
        {
            foreach (var s in stops)
            {
                if (stopCount >= _stopScratch.Length) break;
                _stopScratch[stopCount++] = ((float)Math.Clamp(s.Position, 0.0, 1.0), RgbColor.FromHex(s.ColorHex));
            }
            Array.Sort(_stopScratch, 0, stopCount, Comparer<(float Pos, RgbColor Color)>.Create((a, b) => a.Pos.CompareTo(b.Pos)));
        }
        else
        {
            _stopScratch[0] = (0f, RgbColor.FromHex(parameters.PrimaryColorHex, RgbColor.NeonCyan));
            _stopScratch[1] = (1f, RgbColor.FromHex(parameters.SecondaryColorHex, RgbColor.NeonMagenta));
            stopCount = 2;
        }

        float rad = (float)(parameters.GradientAngleDegrees * Math.PI / 180.0);
        float dirX = MathF.Cos(rad);
        float dirY = MathF.Sin(rad);
        float scale = (float)Math.Clamp(parameters.Scale, 0.25, 4.0);
        float dirSign = parameters.ReverseDirection ? -1f : 1f;
        float phase = parameters.AnimateGradient
            ? (float)(elapsedSeconds * parameters.Speed * 0.22 * dirSign)
            : 0f;

        int w = surface.Width;
        int h = surface.Height;
        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        for (int y = 0; y < h; y++)
        {
            float v = y * invH - 0.5f;
            for (int x = 0; x < w; x++)
            {
                float u = x * invW - 0.5f;
                float proj = (u * dirX + v * dirY) * scale + 0.5f + phase;

                float t;
                if (parameters.AnimateGradient)
                {
                    // Seamless ping-pong wrap when endpoints differ, or smooth cyclic wrap
                    float mod = ((proj % 2f) + 2f) % 2f;
                    t = mod <= 1f ? mod : 2f - mod;
                }
                else
                {
                    t = Math.Clamp(proj, 0f, 1f);
                }

                surface.SetPixel(x, y, SampleGradient(t, stopCount));
            }
        }
    }

    private RgbColor SampleGradient(float t, int count)
    {
        if (count <= 0) return RgbColor.NeonCyan;
        if (count == 1 || t <= _stopScratch[0].Pos) return _stopScratch[0].Color;
        if (t >= _stopScratch[count - 1].Pos) return _stopScratch[count - 1].Color;

        for (int i = 0; i < count - 1; i++)
        {
            var a = _stopScratch[i];
            var b = _stopScratch[i + 1];
            if (t >= a.Pos && t <= b.Pos)
            {
                float span = MathF.Max(1e-5f, b.Pos - a.Pos);
                return RgbColor.Lerp(a.Color, b.Color, (t - a.Pos) / span);
            }
        }

        return _stopScratch[count - 1].Color;
    }
}

public sealed class RainbowWaveEffect : ILightEffect
{
    public EffectType Type => EffectType.RainbowWave;
    public string DisplayName => "Rainbow Wave";
    public string Subtitle => "Directional spectrum";
    public string AccentHex => "#00FFAA";

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        float rad = (float)(parameters.DirectionDegrees * Math.PI / 180.0);
        float dirX = MathF.Cos(rad);
        float dirY = MathF.Sin(rad);
        float scale = (float)Math.Clamp(parameters.Scale, 0.25, 4.0) * 360f;
        float dirSign = parameters.ReverseDirection ? 1f : -1f;
        float timeHue = (float)(elapsedSeconds * parameters.Speed * 110.0 * dirSign);

        int w = surface.Width;
        int h = surface.Height;
        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        for (int y = 0; y < h; y++)
        {
            float v = y * invH;
            for (int x = 0; x < w; x++)
            {
                float u = x * invW;
                float hue = (u * dirX + v * dirY) * scale + timeHue;
                surface.SetPixel(x, y, RgbColor.FromHsv(hue, 1f, 1f));
            }
        }
    }
}

public sealed class RainbowSpiralEffect : ILightEffect
{
    public EffectType Type => EffectType.RainbowSpiral;
    public string DisplayName => "Rainbow Spiral";
    public string Subtitle => "Rotating color vortex";
    public string AccentHex => "#FF2E93";

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        float scale = (float)Math.Clamp(parameters.Scale, 0.25, 4.0);
        float dirSign = parameters.ReverseDirection ? -1f : 1f;
        float timeShift = (float)(elapsedSeconds * parameters.Speed * 140.0 * dirSign);

        int w = surface.Width;
        int h = surface.Height;
        float aspect = w / (float)h;
        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        for (int y = 0; y < h; y++)
        {
            float dy = (y * invH - 0.5f) * 2f;
            for (int x = 0; x < w; x++)
            {
                float dx = (x * invW - 0.5f) * 2f * aspect;
                float angleDeg = MathF.Atan2(dy, dx) * (180f / MathF.PI);
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float hue = angleDeg + dist * 165f * scale + timeShift;
                surface.SetPixel(x, y, RgbColor.FromHsv(hue, 1f, 1f));
            }
        }
    }
}

public sealed class ScreenAmbilightEffect : ILightEffect
{
    public EffectType Type => EffectType.ScreenAmbilight;
    public string DisplayName => "Screen Ambilight";
    public string Subtitle => "Live monitor sync";
    public string AccentHex => "#38BDF8";

    private readonly float[] _smoothR = new float[ScreenGridFrame.CellCount];
    private readonly float[] _smoothG = new float[ScreenGridFrame.CellCount];
    private readonly float[] _smoothB = new float[ScreenGridFrame.CellCount];
    private bool _initialized;

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        float alpha = (float)Math.Clamp(parameters.AmbilightSmoothing, 0.05, 0.95);
        float sat = (float)Math.Clamp(parameters.AmbilightSaturation, 0.5, 2.0);
        float edgeBias = (float)Math.Clamp(parameters.AmbilightEdgeBias, 0.0, 1.0);

        if (screenGrid.HasValidFrame)
        {
            for (int i = 0; i < ScreenGridFrame.CellCount; i++)
            {
                var c = screenGrid.Cells[i].BoostSaturation(sat);
                if (!_initialized)
                {
                    _smoothR[i] = c.R;
                    _smoothG[i] = c.G;
                    _smoothB[i] = c.B;
                }
                else
                {
                    _smoothR[i] += (c.R - _smoothR[i]) * alpha;
                    _smoothG[i] += (c.G - _smoothG[i]) * alpha;
                    _smoothB[i] += (c.B - _smoothB[i]) * alpha;
                }
            }
            _initialized = true;
        }
        else
        {
            // Smooth ambient fallback visualization when DXGI hasn't delivered a desktop frame yet
            for (int gy = 0; gy < ScreenGridFrame.GridHeight; gy++)
            {
                for (int gx = 0; gx < ScreenGridFrame.GridWidth; gx++)
                {
                    int i = gy * ScreenGridFrame.GridWidth + gx;
                    float hue = (float)(gx * 16 + gy * 12 + elapsedSeconds * 32.0);
                    var sim = RgbColor.FromHsv(hue, 0.85f, 0.75f);
                    _smoothR[i] = sim.R;
                    _smoothG[i] = sim.G;
                    _smoothB[i] = sim.B;
                }
            }
        }

        int w = surface.Width;
        int h = surface.Height;
        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        for (int y = 0; y < h; y++)
        {
            float v = y * invH;
            // Optional edge-bias pushes sampling slightly outward toward screen borders
            float sv = ApplyEdgeBias(v, edgeBias);
            float gy = sv * (ScreenGridFrame.GridHeight - 1);
            int y0 = (int)gy;
            int y1 = Math.Min(y0 + 1, ScreenGridFrame.GridHeight - 1);
            float ty = gy - y0;

            for (int x = 0; x < w; x++)
            {
                float u = x * invW;
                float su = ApplyEdgeBias(u, edgeBias);
                float gx = su * (ScreenGridFrame.GridWidth - 1);
                int x0 = (int)gx;
                int x1 = Math.Min(x0 + 1, ScreenGridFrame.GridWidth - 1);
                float tx = gx - x0;

                int i00 = y0 * ScreenGridFrame.GridWidth + x0;
                int i10 = y0 * ScreenGridFrame.GridWidth + x1;
                int i01 = y1 * ScreenGridFrame.GridWidth + x0;
                int i11 = y1 * ScreenGridFrame.GridWidth + x1;

                float w00 = (1f - tx) * (1f - ty);
                float w10 = tx * (1f - ty);
                float w01 = (1f - tx) * ty;
                float w11 = tx * ty;

                byte r = (byte)Math.Clamp(_smoothR[i00] * w00 + _smoothR[i10] * w10 + _smoothR[i01] * w01 + _smoothR[i11] * w11, 0f, 255f);
                byte g = (byte)Math.Clamp(_smoothG[i00] * w00 + _smoothG[i10] * w10 + _smoothG[i01] * w01 + _smoothG[i11] * w11, 0f, 255f);
                byte b = (byte)Math.Clamp(_smoothB[i00] * w00 + _smoothB[i10] * w10 + _smoothB[i01] * w01 + _smoothB[i11] * w11, 0f, 255f);

                surface.SetPixel(x, y, new RgbColor(r, g, b));
            }
        }
    }

    private static float ApplyEdgeBias(float coord, float bias)
    {
        if (bias <= 0.01f) return coord;
        float centered = (coord - 0.5f) * 2f; // -1..1
        float sign = MathF.Sign(centered);
        float pushed = sign * MathF.Pow(MathF.Abs(centered), 1f - bias * 0.55f);
        return Math.Clamp(pushed * 0.5f + 0.5f, 0f, 1f);
    }
}

public sealed class AudioVisualizerEffect : ILightEffect
{
    public EffectType Type => EffectType.AudioVisualizer;
    public string DisplayName => "Audio Reactive";
    public string Subtitle => "Music & bass sync";
    public string AccentHex => "#FF5E00";

    private float _beatHueAccumulator;

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        float sens = (float)Math.Clamp(parameters.AudioSensitivity, 0.5, 4.0);
        var lowColor = RgbColor.FromHex(parameters.AudioLowColorHex, RgbColor.NeonCyan);
        var midColor = RgbColor.FromHex(parameters.AudioMidColorHex, RgbColor.ElectricViolet);
        var highColor = RgbColor.FromHex(parameters.AudioHighColorHex, RgbColor.NeonMagenta);

        // Gentle ambient idle pulse when silence is detected so the case stays stylish
        float bass = audioSpectrum.IsActive
            ? Math.Clamp(audioSpectrum.BassEnergy * sens, 0f, 1f)
            : 0.12f + 0.06f * MathF.Sin((float)elapsedSeconds * 2.2f);
        float beat = audioSpectrum.IsActive
            ? Math.Clamp(audioSpectrum.BeatIntensity * sens, 0f, 1f)
            : 0f;

        int w = surface.Width;
        int h = surface.Height;
        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        switch (parameters.AudioMode)
        {
            case AudioVisualizerMode.BassPulse:
            {
                float aspect = w / (float)h;
                float ringRadius = 0.25f + bass * 1.15f;
                for (int y = 0; y < h; y++)
                {
                    float dy = (y * invH - 0.5f) * 2f;
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x * invW - 0.5f) * 2f * aspect;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        float wave = MathF.Exp(-MathF.Pow((dist - ringRadius) * 2.8f, 2f));
                        float core = Math.Clamp(1f - dist * 0.72f, 0f, 1f) * (0.18f + bass * 0.82f);
                        float energy = Math.Clamp(core + wave * (0.4f + beat * 0.7f), 0f, 1f);

                        var baseCol = RgbColor.Lerp(lowColor, midColor, Math.Clamp(dist * 0.65f, 0f, 1f));
                        var finalCol = RgbColor.Lerp(baseCol, highColor, beat * wave);
                        surface.SetPixel(x, y, finalCol.ScaleBrightness(0.08f + energy * 0.92f));
                    }
                }
                break;
            }

            case AudioVisualizerMode.BeatShift:
            {
                _beatHueAccumulator = (_beatHueAccumulator + (float)(deltaSeconds * 25.0) + beat * 18f) % 360f;
                for (int y = 0; y < h; y++)
                {
                    float v = y * invH;
                    for (int x = 0; x < w; x++)
                    {
                        float u = x * invW;
                        int bandIdx = Math.Clamp((int)(u * AudioSpectrumFrame.BandCount), 0, AudioSpectrumFrame.BandCount - 1);
                        float bandVal = audioSpectrum.IsActive
                            ? Math.Clamp(audioSpectrum.Bands[bandIdx] * sens, 0f, 1f)
                            : 0.2f + 0.1f * MathF.Sin((float)elapsedSeconds * 3f + u * 6f);

                        float hue = _beatHueAccumulator + u * 95f + v * 45f;
                        float brightness = Math.Clamp(0.16f + bandVal * 0.75f + beat * 0.3f, 0f, 1f);
                        surface.SetPixel(x, y, RgbColor.FromHsv(hue, 0.95f, brightness));
                    }
                }
                break;
            }

            case AudioVisualizerMode.SpectrumBars:
            default:
            {
                for (int x = 0; x < w; x++)
                {
                    float u = x * invW;
                    int bandIdx = Math.Clamp((int)(u * AudioSpectrumFrame.BandCount), 0, AudioSpectrumFrame.BandCount - 1);
                    float rawBand = audioSpectrum.IsActive
                        ? Math.Clamp(audioSpectrum.Bands[bandIdx] * sens, 0f, 1f)
                        : 0.14f + 0.08f * MathF.Sin((float)elapsedSeconds * 2.5f + bandIdx * 0.35f);

                    var barColor = u < 0.5f
                        ? RgbColor.Lerp(lowColor, midColor, u * 2f)
                        : RgbColor.Lerp(midColor, highColor, (u - 0.5f) * 2f);

                    for (int y = 0; y < h; y++)
                    {
                        float fromBottom = 1f - y * invH;
                        if (fromBottom <= rawBand)
                        {
                            float intensity = 0.55f + 0.45f * (fromBottom / MathF.Max(0.05f, rawBand));
                            var c = RgbColor.Lerp(barColor, RgbColor.White, beat * 0.25f * fromBottom);
                            surface.SetPixel(x, y, c.ScaleBrightness(intensity));
                        }
                        else
                        {
                            // Subtle dark ambient floor glow so upper fans never look completely dead
                            float glow = MathF.Exp(-(fromBottom - rawBand) * 6.5f) * 0.35f + bass * 0.09f;
                            surface.SetPixel(x, y, barColor.ScaleBrightness(Math.Clamp(glow, 0.03f, 0.45f)));
                        }
                    }
                }
                break;
            }
        }
    }
}

public sealed class HardwareTelemetryEffect : ILightEffect
{
    public EffectType Type => EffectType.HardwareTelemetry;
    public string DisplayName => "Thermal";
    public string Subtitle => "CPU / GPU temp reactive";
    public string AccentHex => "#FFAE00";

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        float tempC = parameters.TelemetrySource switch
        {
            TelemetrySource.Cpu => telemetry.CpuTempC,
            TelemetrySource.Gpu => telemetry.GpuTempC,
            TelemetrySource.MaxCpuGpu => MathF.Max(telemetry.CpuTempC, telemetry.GpuTempC),
            _ => telemetry.CpuTempC
        };

        var primaryColor = MapTemperatureToColor(tempC, parameters);
        var coolCol = RgbColor.FromHex(parameters.CoolColorHex, RgbColor.IceWhite);
        var hotCol = RgbColor.FromHex(parameters.HotColorHex, RgbColor.Crimson);

        float hotRatio = Math.Clamp(
            (tempC - (float)parameters.WarmTempC) / MathF.Max(1f, (float)(parameters.HotTempC - parameters.WarmTempC)),
            0f, 1f);

        // Thermal breathing wave speeds up smoothly as temperature rises
        float pulseHz = 0.45f + hotRatio * 1.9f;
        float pulseDepth = parameters.TelemetryPulseAtHot ? (0.08f + hotRatio * 0.24f) : 0.06f;

        int w = surface.Width;
        int h = surface.Height;
        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        for (int y = 0; y < h; y++)
        {
            float v = y * invH;
            // Top of case slightly warmer tint, bottom intake slightly cooler tint
            float verticalShift = (1f - v) * 0.14f;
            for (int x = 0; x < w; x++)
            {
                float u = x * invW;
                float wave = 0.5f + 0.5f * MathF.Sin((float)(elapsedSeconds * Math.PI * 2.0 * pulseHz) + u * 2.4f - v * 1.8f);
                float brightness = 1f - pulseDepth * (1f - wave);

                var c = verticalShift > 0.07f
                    ? RgbColor.Lerp(primaryColor, hotCol, (verticalShift - 0.07f) * hotRatio)
                    : RgbColor.Lerp(primaryColor, coolCol, (0.07f - verticalShift) * (1f - hotRatio));

                surface.SetPixel(x, y, c.ScaleBrightness(brightness));
            }
        }
    }

    public static RgbColor MapTemperatureToColor(float tempC, EffectParameters parameters)
    {
        var cool = RgbColor.FromHex(parameters.CoolColorHex, RgbColor.IceWhite);
        var warm = RgbColor.FromHex(parameters.WarmColorHex, RgbColor.Amber);
        var hot = RgbColor.FromHex(parameters.HotColorHex, RgbColor.Crimson);

        float tCool = (float)parameters.CoolTempC;
        float tWarm = (float)Math.Max(parameters.CoolTempC + 1.0, parameters.WarmTempC);
        float tHot = (float)Math.Max(tWarm + 1.0, parameters.HotTempC);

        if (tempC <= tCool) return cool;
        if (tempC <= tWarm)
        {
            float t = (tempC - tCool) / (tWarm - tCool);
            return RgbColor.Lerp(cool, warm, t);
        }
        if (tempC < tHot)
        {
            float t = (tempC - tWarm) / (tHot - tWarm);
            return RgbColor.Lerp(warm, hot, t);
        }
        return hot;
    }
}

public sealed class BreathingEffect : ILightEffect
{
    public EffectType Type => EffectType.Breathing;
    public string DisplayName => "Breathing";
    public string Subtitle => "Soft organic pulse";
    public string AccentHex => "#A855F7";

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        var primary = RgbColor.FromHex(parameters.PrimaryColorHex, RgbColor.ElectricViolet);
        float speed = (float)Math.Clamp(parameters.Speed, 0.2, 4.0);
        // Smooth cosine ease in [0.06 .. 1.0] so LEDs never completely snap black
        float wave = 0.5f - 0.5f * MathF.Cos((float)(elapsedSeconds * speed * 1.8));
        float brightness = 0.06f + 0.94f * MathF.Pow(wave, 1.35f);
        surface.Fill(primary.ScaleBrightness(brightness));
    }
}

public sealed class PlasmaEffect : ILightEffect
{
    public EffectType Type => EffectType.Plasma;
    public string DisplayName => "Plasma";
    public string Subtitle => "Liquid neon energy";
    public string AccentHex => "#EC4899";

    public void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry)
    {
        float speed = (float)Math.Clamp(parameters.Speed, 0.1, 5.0);
        float scale = (float)Math.Clamp(parameters.Scale, 0.25, 4.0);
        float dir = parameters.ReverseDirection ? -1f : 1f;
        float t = (float)(elapsedSeconds * speed * 1.6 * dir);

        int w = surface.Width;
        int h = surface.Height;
        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        var c1 = RgbColor.FromHex("#00E5FF");
        var c2 = RgbColor.FromHex("#8B5CF6");
        var c3 = RgbColor.FromHex("#EC4899");

        for (int y = 0; y < h; y++)
        {
            float v = (y * invH - 0.5f) * 4.5f * scale;
            for (int x = 0; x < w; x++)
            {
                float u = (x * invW - 0.5f) * 6.0f * scale;
                float v1 = MathF.Sin(u + t);
                float v2 = MathF.Sin(v + t * 0.85f);
                float v3 = MathF.Sin((u + v) * 0.7f - t * 0.65f);
                float v4 = MathF.Sin(MathF.Sqrt(u * u + v * v + 0.5f) * 1.6f - t * 1.15f);

                float norm = (v1 + v2 + v3 + v4) * 0.125f + 0.5f; // [0..1]
                var color = norm < 0.5f
                    ? RgbColor.Lerp(c1, c2, norm * 2f)
                    : RgbColor.Lerp(c2, c3, (norm - 0.5f) * 2f);

                surface.SetPixel(x, y, color);
            }
        }
    }
}
