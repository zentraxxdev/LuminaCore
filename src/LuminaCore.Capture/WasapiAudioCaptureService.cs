using System.Runtime.InteropServices;
using LuminaCore.Core.Engine;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;
using NAudio.Wave;

namespace LuminaCore.Capture;

/// <summary>
/// Captures Windows system audio via NAudio WasapiLoopbackCapture and runs a zero-allocation FFT
/// to drive real-time 32-band spectrum bars, bass pulses, and beat-synced ARGB shifts.
/// </summary>
public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    private readonly object _lock = new();
    private readonly FftAnalyzer _fft = new();
    private readonly AudioSpectrumFrame _latestFrame = new();
    private WasapiLoopbackCapture? _capture;
    private int _sampleRate = 48000;
    private int _channels = 2;
    private volatile bool _isRunning;
    private string _statusMessage = "Idle";

    public bool IsRunning => _isRunning;
    public string StatusMessage => _statusMessage;

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning) return;

            try
            {
                var capture = new WasapiLoopbackCapture();
                _sampleRate = capture.WaveFormat.SampleRate;
                _channels = capture.WaveFormat.Channels;
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();

                _capture = capture;
                _isRunning = true;
                _statusMessage = $"WASAPI Loopback ({_sampleRate} Hz, {_channels}ch)";
                _latestFrame.DeviceName = _statusMessage;
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _statusMessage = $"Audio loopback unavailable: {ex.Message}";
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning && _capture is null) return;
            _isRunning = false;

            if (_capture is not null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                try { _capture.StopRecording(); } catch { }
                try { _capture.Dispose(); } catch { }
                _capture = null;
            }

            Array.Clear(_latestFrame.Bands);
            _latestFrame.BassEnergy = 0;
            _latestFrame.MidEnergy = 0;
            _latestFrame.TrebleEnergy = 0;
            _latestFrame.BeatIntensity = 0;
            _latestFrame.PeakLevel = 0;
            _latestFrame.IsActive = false;
            _statusMessage = "Stopped";
        }
    }

    public void CopyLatestSpectrum(AudioSpectrumFrame target)
    {
        lock (_lock)
        {
            Array.Copy(_latestFrame.Bands, target.Bands, AudioSpectrumFrame.BandCount);
            target.BassEnergy = _latestFrame.BassEnergy;
            target.MidEnergy = _latestFrame.MidEnergy;
            target.TrebleEnergy = _latestFrame.TrebleEnergy;
            target.BeatIntensity = _latestFrame.BeatIntensity;
            target.PeakLevel = _latestFrame.PeakLevel;
            target.IsActive = _latestFrame.IsActive;
            target.DeviceName = _latestFrame.DeviceName;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        lock (_lock)
        {
            if (!_isRunning) return;

            // WASAPI loopback always outputs 32-bit IEEE float PCM on Windows
            var floatSpan = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
            _fft.PushSamples(floatSpan, _channels);
            _fft.ComputeSpectrum(_sampleRate, _latestFrame);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _statusMessage = $"WASAPI stopped: {e.Exception.Message}";
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
