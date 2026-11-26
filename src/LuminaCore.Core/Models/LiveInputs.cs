namespace LuminaCore.Core.Models;

/// <summary>
/// Downsampled 16x9 screen grid produced by DXGI Desktop Duplication.
/// </summary>
public sealed class ScreenGridFrame
{
    public const int GridWidth = 16;
    public const int GridHeight = 9;
    public const int CellCount = GridWidth * GridHeight;

    public RgbColor[] Cells { get; } = new RgbColor[CellCount];
    public bool HasValidFrame { get; set; }
    public long FrameSequence { get; set; }
    public string StatusText { get; set; } = "Idle";
}

/// <summary>
/// 32-band real-time FFT spectrum and beat data produced by WASAPI loopback capture.
/// </summary>
public sealed class AudioSpectrumFrame
{
    public const int BandCount = 32;

    public float[] Bands { get; } = new float[BandCount];
    public float BassEnergy { get; set; }
    public float MidEnergy { get; set; }
    public float TrebleEnergy { get; set; }
    public float BeatIntensity { get; set; }
    public float PeakLevel { get; set; }
    public bool IsActive { get; set; }
    public string DeviceName { get; set; } = "Default Output";
}

/// <summary>
/// Live CPU and GPU temperature telemetry snapshot from LibreHardwareMonitor.
/// </summary>
public sealed class HardwareTelemetrySnapshot
{
    public float CpuTempC { get; set; } = 42f;
    public float GpuTempC { get; set; } = 39f;
    public float CpuLoadPercent { get; set; } = 14f;
    public float GpuLoadPercent { get; set; } = 8f;
    public int SystemFanRpm { get; set; } = 1120;
    public string CpuName { get; set; } = "AMD Ryzen 5 7600X";
    public string GpuName { get; set; } = "AMD Radeon RX 9060 XT";
    public bool IsLiveSensorData { get; set; }
    public string StatusMessage { get; set; } = "Waiting for telemetry...";
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class HardwareZoneInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public int LedCount { get; init; }
    public int MinLeds { get; init; }
    public int MaxLeds { get; init; }
}

public sealed class HardwareDeviceInfo
{
    public int DeviceIndex { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;
    public int TotalLeds { get; init; }
    public List<HardwareZoneInfo> Zones { get; init; } = [];
    public bool IsMotherboard { get; init; }
}

public sealed class HardwareConnectionState
{
    public bool IsConnected { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 6742;
    public string StatusMessage { get; init; } = "Disconnected";
    public HardwareDeviceInfo? ActiveDevice { get; init; }
    public IReadOnlyList<HardwareDeviceInfo> DiscoveredDevices { get; init; } = [];
}

public sealed class HidDiagnosticsInfo
{
    public bool IsConnected { get; init; }
    public string ControllerName { get; init; } = "No HID Device";
    public string VidPid { get; init; } = "26CE:01A2";
    public int ChannelCount { get; init; }
    public double PacketLatencyMs { get; init; }
    public double HardwareFps { get; init; }
    public bool IsCalibrating { get; init; }
    public int CalibrationChannel { get; init; } = -1;
    public int CalibrationLitLeds { get; init; }
    public string DiagnosticReport { get; init; } = string.Empty;
}

public sealed class RenderDiagnostics
{
    public double CurrentFps { get; init; }
    public double AverageFrameMs { get; init; }
    public double WorkingSetMb { get; init; }
    public bool IsBlackout { get; init; }
    public string ActiveEffectName { get; init; } = string.Empty;
    public string ActiveProfileName { get; init; } = string.Empty;
    public int TotalSampledLeds { get; init; }
}
