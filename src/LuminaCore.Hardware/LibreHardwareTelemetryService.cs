using LibreHardwareMonitor.Hardware;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;

namespace LuminaCore.Hardware;

/// <summary>
/// Polls CPU (Ryzen 5 7600X / AMD/Intel) and GPU (RX 9060 XT / AMD/NVIDIA) temperatures
/// using LibreHardwareMonitorLib on a lightweight background timer.
/// </summary>
public sealed class LibreHardwareTelemetryService : ITelemetryService
{
    private readonly object _lock = new();
    private Computer? _computer;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private HardwareTelemetrySnapshot _latest = new();
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    public void Start(int pollIntervalMs = 1000)
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();
        int interval = Math.Clamp(pollIntervalMs, 250, 5000);

        _pollTask = Task.Run(() => PollLoopAsync(interval, _cts.Token));
    }

    public void Stop()
    {
        _isRunning = false;
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;

        lock (_lock)
        {
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }

    public HardwareTelemetrySnapshot GetLatestSnapshot()
    {
        lock (_lock)
        {
            return new HardwareTelemetrySnapshot
            {
                CpuTempC = _latest.CpuTempC,
                GpuTempC = _latest.GpuTempC,
                CpuLoadPercent = _latest.CpuLoadPercent,
                GpuLoadPercent = _latest.GpuLoadPercent,
                SystemFanRpm = _latest.SystemFanRpm,
                CpuName = _latest.CpuName,
                GpuName = _latest.GpuName,
                IsLiveSensorData = _latest.IsLiveSensorData,
                StatusMessage = _latest.StatusMessage,
                LastUpdatedUtc = _latest.LastUpdatedUtc
            };
        }
    }

    private async Task PollLoopAsync(int intervalMs, CancellationToken ct)
    {
        bool opened = TryInitializeComputer();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

        try
        {
            // Immediate first sample
            SampleSensors(opened);

            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (!opened)
                    opened = TryInitializeComputer();

                SampleSensors(opened);
            }
        }
        catch (OperationCanceledException)
        {
            // Clean stop
        }
    }

    private bool TryInitializeComputer()
    {
        lock (_lock)
        {
            try
            {
                _computer?.Close();
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = false,
                    IsMemoryEnabled = false,
                    IsControllerEnabled = false,
                    IsNetworkEnabled = false,
                    IsStorageEnabled = false
                };
                _computer.Open();
                return true;
            }
            catch (Exception ex)
            {
                _latest = new HardwareTelemetrySnapshot
                {
                    CpuTempC = 44f,
                    GpuTempC = 41f,
                    CpuName = "AMD Ryzen 5 7600X",
                    GpuName = "AMD Radeon RX 9060 XT",
                    IsLiveSensorData = false,
                    StatusMessage = $"Sensor access limited ({ex.Message}) — run elevated for full CPU MSR access",
                    LastUpdatedUtc = DateTime.UtcNow
                };
                return false;
            }
        }
    }

    private void SampleSensors(bool computerReady)
    {
        if (!computerReady) return;

        lock (_lock)
        {
            if (_computer is null) return;

            float? cpuTemp = null;
            float? gpuTemp = null;
            float? cpuLoad = null;
            float? gpuLoad = null;
            int? fanRpm = null;
            string cpuName = _latest.CpuName;
            string gpuName = _latest.GpuName;

            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    hw.Update();

                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        if (!string.IsNullOrWhiteSpace(hw.Name)) cpuName = hw.Name;
                        cpuTemp = ExtractBestCpuTemp(hw);
                        cpuLoad = ExtractBestLoad(hw, "Total");
                    }
                    else if (hw.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel)
                    {
                        if (!string.IsNullOrWhiteSpace(hw.Name)) gpuName = hw.Name;
                        gpuTemp ??= ExtractBestGpuTemp(hw);
                        gpuLoad ??= ExtractBestLoad(hw, "Core");
                        fanRpm ??= ExtractBestFanRpm(hw);
                    }
                }

                bool hasLive = cpuTemp.HasValue || gpuTemp.HasValue;
                _latest = new HardwareTelemetrySnapshot
                {
                    CpuTempC = cpuTemp ?? _latest.CpuTempC,
                    GpuTempC = gpuTemp ?? _latest.GpuTempC,
                    CpuLoadPercent = cpuLoad ?? _latest.CpuLoadPercent,
                    GpuLoadPercent = gpuLoad ?? _latest.GpuLoadPercent,
                    SystemFanRpm = fanRpm ?? _latest.SystemFanRpm,
                    CpuName = cpuName,
                    GpuName = gpuName,
                    IsLiveSensorData = hasLive,
                    StatusMessage = hasLive
                        ? "Live Hardware Sensors Active"
                        : "Sensors detected (waiting for thermal readout)",
                    LastUpdatedUtc = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _latest.StatusMessage = $"Telemetry read error: {ex.Message}";
                _latest.LastUpdatedUtc = DateTime.UtcNow;
            }
        }
    }

    private static float? ExtractBestCpuTemp(IHardware cpu)
    {
        float? preferred = null;
        float? fallback = null;

        foreach (var s in cpu.Sensors)
        {
            if (s.SensorType != SensorType.Temperature || !s.Value.HasValue || s.Value.Value <= 0f)
                continue;

            string name = s.Name ?? string.Empty;
            if (name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase))
            {
                preferred = s.Value.Value;
                break;
            }

            fallback ??= s.Value.Value;
        }

        return preferred ?? fallback;
    }

    private static float? ExtractBestGpuTemp(IHardware gpu)
    {
        float? core = null;
        float? hotspot = null;
        float? fallback = null;

        foreach (var s in gpu.Sensors)
        {
            if (s.SensorType != SensorType.Temperature || !s.Value.HasValue || s.Value.Value <= 0f)
                continue;

            string name = s.Name ?? string.Empty;
            if (name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Edge", StringComparison.OrdinalIgnoreCase))
            {
                core = s.Value.Value;
            }
            else if (name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase))
            {
                hotspot = s.Value.Value;
            }
            else
            {
                fallback ??= s.Value.Value;
            }
        }

        return core ?? hotspot ?? fallback;
    }

    private static float? ExtractBestLoad(IHardware hw, string preferredKeyword)
    {
        float? preferred = null;
        float? fallback = null;
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType != SensorType.Load || !s.Value.HasValue) continue;
            if ((s.Name ?? string.Empty).Contains(preferredKeyword, StringComparison.OrdinalIgnoreCase))
            {
                preferred = s.Value.Value;
                break;
            }
            fallback ??= s.Value.Value;
        }
        return preferred ?? fallback;
    }

    private static int? ExtractBestFanRpm(IHardware hw)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Fan && s.Value.HasValue && s.Value.Value > 100f)
                return (int)MathF.Round(s.Value.Value);
        }
        return null;
    }

    public void Dispose()
    {
        Stop();
    }
}
