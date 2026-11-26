using LuminaCore.Core.Engine;
using LuminaCore.Core.Models;

namespace LuminaCore.Core.Interfaces;

public interface IHardwareController : IDisposable
{
    HardwareConnectionState ConnectionState { get; }
    event EventHandler<HardwareConnectionState>? ConnectionStateChanged;

    Task<bool> ConnectAsync(string host, int port, int preferredDeviceIndex = -1, CancellationToken ct = default);
    void Disconnect();
    void SelectDevice(int deviceIndex);
    void PushZoneColors(int zoneIndex, ReadOnlySpan<RgbColor> colors);
    void SetAllBlackout(IReadOnlyList<ZoneLayout> zones);
    void SetChannelColorOrder(int channelIndex, ChannelColorOrder order);
    void SetCalibrationPreview(int channelIndex, int litLedCount);
    void StopCalibrationPreview();
    HidDiagnosticsInfo GetHidDiagnostics();
}

public interface IScreenCaptureService : IDisposable
{
    bool IsRunning { get; }
    string StatusMessage { get; }
    void Start();
    void Stop();
    void CopyLatestGrid(ScreenGridFrame target);
}

public interface IAudioCaptureService : IDisposable
{
    bool IsRunning { get; }
    string StatusMessage { get; }
    void Start();
    void Stop();
    void CopyLatestSpectrum(AudioSpectrumFrame target);
}

public interface ITelemetryService : IDisposable
{
    bool IsRunning { get; }
    void Start(int pollIntervalMs = 1000);
    void Stop();
    HardwareTelemetrySnapshot GetLatestSnapshot();
}

public interface IProfileRepository
{
    string DataDirectory { get; }
    string ProfilesFilePath { get; }
    string SettingsFilePath { get; }

    List<LightingProfile> LoadProfiles();
    void SaveProfiles(IEnumerable<LightingProfile> profiles);
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
}

public interface IProcessWatcherService : IDisposable
{
    bool IsRunning { get; }
    string? ActiveMatchedProcess { get; }
    string? ActiveMatchedProfileId { get; }
    event EventHandler<ProcessMatchEventArgs>? ProcessMatchChanged;

    void UpdateProfiles(IReadOnlyList<LightingProfile> profiles);
    void Start();
    void Stop();
}

public sealed class ProcessMatchEventArgs : EventArgs
{
    public string? ProcessName { get; init; }
    public string? ProfileId { get; init; }
}

public interface ILightEffect
{
    EffectType Type { get; }
    string DisplayName { get; }
    string Subtitle { get; }
    string AccentHex { get; }

    void Render(
        CanvasSurface surface,
        double elapsedSeconds,
        double deltaSeconds,
        EffectParameters parameters,
        ScreenGridFrame screenGrid,
        AudioSpectrumFrame audioSpectrum,
        HardwareTelemetrySnapshot telemetry);
}
