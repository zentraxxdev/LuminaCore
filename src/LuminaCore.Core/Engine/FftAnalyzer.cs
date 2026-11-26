using LuminaCore.Core.Models;

namespace LuminaCore.Core.Engine;

/// <summary>
/// Allocation-free in-place Radix-2 Cooley-Tukey FFT with Hann windowing,
/// logarithmic 32-band spectrum binning, and adaptive beat onset detection.
/// </summary>
public sealed class FftAnalyzer
{
    public const int FftSize = 1024; // ~21ms window at 48kHz -> responsive 60 FPS audio reactivity

    private readonly float[] _real = new float[FftSize];
    private readonly float[] _imag = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _ringBuffer = new float[FftSize];
    private readonly float[] _smoothedBands = new float[AudioSpectrumFrame.BandCount];
    private int _ringWritePos;
    private float _rollingBassAvg = 0.05f;
    private float _beatDecay;

    public FftAnalyzer()
    {
        for (int i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
        }
    }

    /// <summary>
    /// Feeds interleaved IEEE float PCM audio samples (mono or stereo) into the ring buffer.
    /// </summary>
    public void PushSamples(ReadOnlySpan<float> interleavedSamples, int channels)
    {
        if (channels <= 0) channels = 1;
        for (int i = 0; i + channels - 1 < interleavedSamples.Length; i += channels)
        {
            float mono = 0f;
            for (int c = 0; c < channels; c++)
                mono += interleavedSamples[i + c];
            mono /= channels;

            _ringBuffer[_ringWritePos] = mono;
            _ringWritePos = (_ringWritePos + 1) & (FftSize - 1);
        }
    }

    /// <summary>
    /// Computes the FFT and populates the target <see cref="AudioSpectrumFrame"/>.
    /// </summary>
    public void ComputeSpectrum(int sampleRate, AudioSpectrumFrame target)
    {
        float peak = 0f;
        int idx = _ringWritePos;
        for (int i = 0; i < FftSize; i++)
        {
            float sample = _ringBuffer[idx];
            float abs = MathF.Abs(sample);
            if (abs > peak) peak = abs;

            _real[i] = sample * _window[i];
            _imag[i] = 0f;
            idx = (idx + 1) & (FftSize - 1);
        }

        ExecuteRadix2Fft(_real, _imag);

        int halfN = FftSize / 2;
        float binHz = Math.Max(1f, sampleRate / (float)FftSize);
        const float minFreq = 35f;
        float maxFreq = MathF.Min(16000f, sampleRate * 0.45f);
        float logMin = MathF.Log10(minFreq);
        float logMax = MathF.Log10(maxFreq);

        float bassSum = 0f, midSum = 0f, trebleSum = 0f;
        int bassCount = 0, midCount = 0, trebleCount = 0;

        for (int b = 0; b < AudioSpectrumFrame.BandCount; b++)
        {
            float f0 = MathF.Pow(10f, logMin + (logMax - logMin) * (b / (float)AudioSpectrumFrame.BandCount));
            float f1 = MathF.Pow(10f, logMin + (logMax - logMin) * ((b + 1) / (float)AudioSpectrumFrame.BandCount));

            int bin0 = Math.Clamp((int)(f0 / binHz), 1, halfN - 1);
            int bin1 = Math.Clamp((int)(f1 / binHz), bin0, halfN - 1);

            float maxMag = 0f;
            for (int k = bin0; k <= bin1; k++)
            {
                float mag = MathF.Sqrt(_real[k] * _real[k] + _imag[k] * _imag[k]) / (FftSize * 0.25f);
                if (mag > maxMag) maxMag = mag;
            }

            // Perceptual loudness shaping (boost highs slightly, normalize smoothly)
            float bandBoost = 1f + (b / (float)AudioSpectrumFrame.BandCount) * 1.4f;
            float normalized = Math.Clamp(MathF.Log10(1f + maxMag * 18f * bandBoost), 0f, 1f);

            // Fast attack, smooth decay
            float prev = _smoothedBands[b];
            float smoothed = normalized > prev
                ? prev + (normalized - prev) * 0.65f
                : prev * 0.84f;

            _smoothedBands[b] = smoothed;
            target.Bands[b] = smoothed;

            if (b < 7) { bassSum += smoothed; bassCount++; }
            else if (b < 19) { midSum += smoothed; midCount++; }
            else { trebleSum += smoothed; trebleCount++; }
        }

        float bass = bassCount > 0 ? bassSum / bassCount : 0f;
        float mid = midCount > 0 ? midSum / midCount : 0f;
        float treble = trebleCount > 0 ? trebleSum / trebleCount : 0f;

        // Adaptive beat detection
        _rollingBassAvg = _rollingBassAvg * 0.94f + bass * 0.06f;
        if (bass > _rollingBassAvg * 1.32f && bass > 0.16f)
        {
            _beatDecay = Math.Clamp((bass - _rollingBassAvg) * 3.2f, 0.35f, 1f);
        }
        else
        {
            _beatDecay *= 0.86f;
        }

        target.BassEnergy = bass;
        target.MidEnergy = mid;
        target.TrebleEnergy = treble;
        target.BeatIntensity = _beatDecay;
        target.PeakLevel = peak;
        target.IsActive = peak > 0.0005f;
    }

    private static void ExecuteRadix2Fft(float[] real, float[] imag)
    {
        int n = real.Length;

        // Bit-reversal permutation
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
            int bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }
            j |= bit;
        }

        // Butterfly stages
        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * MathF.PI / len;
            float wLenCos = MathF.Cos(ang);
            float wLenSin = MathF.Sin(ang);
            int half = len >> 1;

            for (int i = 0; i < n; i += len)
            {
                float wCos = 1f;
                float wSin = 0f;
                for (int k = 0; k < half; k++)
                {
                    int u = i + k;
                    int v = u + half;

                    float tReal = real[v] * wCos - imag[v] * wSin;
                    float tImag = real[v] * wSin + imag[v] * wCos;

                    real[v] = real[u] - tReal;
                    imag[v] = imag[u] - tImag;
                    real[u] += tReal;
                    imag[u] += tImag;

                    float nextCos = wCos * wLenCos - wSin * wLenSin;
                    wSin = wCos * wLenSin + wSin * wLenCos;
                    wCos = nextCos;
                }
            }
        }
    }
}
