using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;
using OpenRGB.NET;
using OpenRgbColor = OpenRGB.NET.Color;

namespace LuminaCore.Hardware;

/// <summary>
/// User-space OpenRGB SDK client (127.0.0.1:6742) targeting the ASRock A620M Pro RS motherboard's
/// 3 independent 5V 3-pin ARGB zones without requiring kernel drivers (safe for Ricochet/Vanguard).
/// </summary>
public sealed class OpenRgbHardwareController : IHardwareController
{
    private readonly object _clientLock = new();
    private OpenRgbClient? _client;
    private Device[] _rawDevices = [];
    private int _activeDeviceIndex = -1;
    private readonly Dictionary<int, OpenRgbColor[]> _zoneBufferPool = new();
    private HardwareConnectionState _state = new();

    public HardwareConnectionState ConnectionState
    {
        get { lock (_clientLock) return _state; }
    }

    public event EventHandler<HardwareConnectionState>? ConnectionStateChanged;

    public Task<bool> ConnectAsync(string host, int port, int preferredDeviceIndex = -1, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            lock (_clientLock)
            {
                DisconnectInternal(raiseEvent: false);

                try
                {
                    var client = new OpenRgbClient(
                        ip: host,
                        port: port,
                        name: "LuminaCore ARGB Engine",
                        autoConnect: false,
                        timeoutMs: 1500);

                    client.Connect();
                    if (!client.Connected)
                    {
                        client.Dispose();
                        SetState(new HardwareConnectionState
                        {
                            IsConnected = false,
                            Host = host,
                            Port = port,
                            StatusMessage = $"OpenRGB Server not reachable at {host}:{port}"
                        });
                        return false;
                    }

                    _client = client;
                    _rawDevices = client.GetAllControllerData() ?? [];

                    // OpenRGB 1.1+ starts its SDK server before HID/USB device detection finishes.
                    // If 0 devices are returned, wait briefly and re-query before giving up on this pass.
                    if (_rawDevices.Length == 0)
                    {
                        Thread.Sleep(1500);
                        _rawDevices = client.GetAllControllerData() ?? [];
                    }

                    var discovered = MapDevices(_rawDevices);
                    _activeDeviceIndex = ResolveTargetDeviceIndex(_rawDevices, preferredDeviceIndex);

                    if (_activeDeviceIndex >= 0 && _activeDeviceIndex < _rawDevices.Length)
                    {
                        TryEnableDirectMode(client, _activeDeviceIndex, _rawDevices[_activeDeviceIndex]);
                    }

                    var activeInfo = discovered.FirstOrDefault(d => d.DeviceIndex == _activeDeviceIndex);
                    string msg = activeInfo is not null
                        ? $"Connected: {activeInfo.Name} ({activeInfo.Zones.Count} zones)"
                        : $"Connected to OpenRGB ({discovered.Count} device(s), no ARGB motherboard detected)";

                    SetState(new HardwareConnectionState
                    {
                        IsConnected = true,
                        Host = host,
                        Port = port,
                        StatusMessage = msg,
                        ActiveDevice = activeInfo,
                        DiscoveredDevices = discovered
                    });

                    return true;
                }
                catch (Exception ex)
                {
                    DisconnectInternal(raiseEvent: false);
                    SetState(new HardwareConnectionState
                    {
                        IsConnected = false,
                        Host = host,
                        Port = port,
                        StatusMessage = $"Waiting for OpenRGB SDK ({host}:{port}) — {ex.Message}"
                    });
                    return false;
                }
            }
        }, ct);
    }

    public void Disconnect()
    {
        lock (_clientLock)
        {
            DisconnectInternal(raiseEvent: true);
        }
    }

    public void SelectDevice(int deviceIndex)
    {
        lock (_clientLock)
        {
            if (_client is not { Connected: true }) return;
            if (deviceIndex < 0 || deviceIndex >= _rawDevices.Length) return;

            _activeDeviceIndex = deviceIndex;
            TryEnableDirectMode(_client, _activeDeviceIndex, _rawDevices[_activeDeviceIndex]);

            var activeInfo = _state.DiscoveredDevices.FirstOrDefault(d => d.DeviceIndex == deviceIndex);
            SetState(new HardwareConnectionState
            {
                IsConnected = true,
                Host = _state.Host,
                Port = _state.Port,
                StatusMessage = activeInfo is not null
                    ? $"Connected: {activeInfo.Name} ({activeInfo.Zones.Count} zones)"
                    : _state.StatusMessage,
                ActiveDevice = activeInfo,
                DiscoveredDevices = _state.DiscoveredDevices
            });
        }
    }

    public void PushZoneColors(int zoneIndex, ReadOnlySpan<RgbColor> colors)
    {
        if (colors.IsEmpty) return;

        lock (_clientLock)
        {
            if (_client is not { Connected: true } || _activeDeviceIndex < 0 || _activeDeviceIndex >= _rawDevices.Length)
                return;

            var device = _rawDevices[_activeDeviceIndex];
            if (device.Zones is null || zoneIndex < 0 || zoneIndex >= device.Zones.Length)
                return;

            try
            {
                var hwZone = device.Zones[zoneIndex];
                int targetCount = (int)hwZone.LedCount;
                if (targetCount <= 0)
                    targetCount = colors.Length;

                if (!_zoneBufferPool.TryGetValue(zoneIndex, out var buffer) || buffer.Length != targetCount)
                {
                    buffer = new OpenRgbColor[targetCount];
                    _zoneBufferPool[zoneIndex] = buffer;
                }

                // Map virtual LED strip onto physical zone LED count cleanly (1:1 or linear resample)
                if (targetCount == colors.Length)
                {
                    for (int i = 0; i < targetCount; i++)
                    {
                        var c = colors[i];
                        buffer[i] = new OpenRgbColor(c.R, c.G, c.B);
                    }
                }
                else
                {
                    for (int i = 0; i < targetCount; i++)
                    {
                        int srcIdx = targetCount <= 1 ? 0 : (int)MathF.Round(i * (colors.Length - 1) / (float)(targetCount - 1));
                        var c = colors[Math.Clamp(srcIdx, 0, colors.Length - 1)];
                        buffer[i] = new OpenRgbColor(c.R, c.G, c.B);
                    }
                }

                _client.UpdateZoneLeds(_activeDeviceIndex, zoneIndex, buffer);
            }
            catch (Exception ex)
            {
                // Connection dropped (OpenRGB closed or restarted) -> mark disconnected for auto-reconnect
                DisconnectInternal(raiseEvent: false);
                SetState(new HardwareConnectionState
                {
                    IsConnected = false,
                    Host = _state.Host,
                    Port = _state.Port,
                    StatusMessage = $"OpenRGB connection lost ({ex.Message}) — auto-reconnecting..."
                });
            }
        }
    }

    public void SetAllBlackout(IReadOnlyList<ZoneLayout> zones)
    {
        Span<RgbColor> black = stackalloc RgbColor[1];
        black[0] = RgbColor.Black;

        foreach (var zone in zones)
        {
            int count = Math.Clamp(zone.LedCount, 1, 256);
            var zeros = new RgbColor[count];
            PushZoneColors(zone.ZoneIndex, zeros);
        }
    }

    public void SetChannelColorOrder(int channelIndex, ChannelColorOrder order) { }
    public void SetCalibrationPreview(int channelIndex, int litLedCount) { }
    public void StopCalibrationPreview() { }
    public HidDiagnosticsInfo GetHidDiagnostics() => new()
    {
        IsConnected = ConnectionState.IsConnected,
        ControllerName = ConnectionState.ActiveDevice?.Name ?? "OpenRGB SDK",
        VidPid = "OPENRGB-TCP",
        ChannelCount = ConnectionState.ActiveDevice?.Zones.Count ?? 0,
        DiagnosticReport = $"OpenRGB SDK ({ConnectionState.Host}:{ConnectionState.Port}) — {ConnectionState.StatusMessage}"
    };

    private static int ResolveTargetDeviceIndex(Device[] devices, int preferredIndex)
    {
        if (devices.Length == 0) return -1;
        if (preferredIndex >= 0 && preferredIndex < devices.Length)
            return preferredIndex;

        // 1. Exact ASRock / A620M Pro RS motherboard match
        for (int i = 0; i < devices.Length; i++)
        {
            string name = devices[i].Name ?? string.Empty;
            if (devices[i].Type == DeviceType.Motherboard &&
                (name.Contains("ASRock", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("A620", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Polychrome", StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        // 2. Any motherboard with >= 3 zones
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].Type == DeviceType.Motherboard && (devices[i].Zones?.Length ?? 0) >= 3)
                return i;
        }

        // 3. Any motherboard
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].Type == DeviceType.Motherboard)
                return i;
        }

        // 4. Fallback to first device with zones
        for (int i = 0; i < devices.Length; i++)
        {
            if ((devices[i].Zones?.Length ?? 0) > 0)
                return i;
        }

        return 0;
    }

    private static void TryEnableDirectMode(OpenRgbClient client, int deviceIndex, Device device)
    {
        try
        {
            if (device.Modes is null) return;
            for (int m = 0; m < device.Modes.Length; m++)
            {
                if (string.Equals(device.Modes[m].Name, "Direct", StringComparison.OrdinalIgnoreCase))
                {
                    client.UpdateMode(deviceIndex, m);
                    return;
                }
            }
            client.SetCustomMode(deviceIndex);
        }
        catch
        {
            // Some ASRock firmware revisions already default to Direct mode or reject SetCustomMode
        }
    }

    private static List<HardwareDeviceInfo> MapDevices(Device[] devices)
    {
        var list = new List<HardwareDeviceInfo>(devices.Length);
        for (int i = 0; i < devices.Length; i++)
        {
            var d = devices[i];
            var zones = new List<HardwareZoneInfo>();
            if (d.Zones is not null)
            {
                for (int z = 0; z < d.Zones.Length; z++)
                {
                    zones.Add(new HardwareZoneInfo
                    {
                        Index = z,
                        Name = d.Zones[z].Name ?? $"Zone {z}",
                        LedCount = (int)d.Zones[z].LedCount,
                        MinLeds = (int)d.Zones[z].LedsMin,
                        MaxLeds = (int)d.Zones[z].LedsMax
                    });
                }
            }

            list.Add(new HardwareDeviceInfo
            {
                DeviceIndex = i,
                Name = string.IsNullOrWhiteSpace(d.Name) ? $"OpenRGB Device #{i}" : d.Name,
                DeviceType = d.Type.ToString(),
                TotalLeds = d.Leds?.Length ?? 0,
                Zones = zones,
                IsMotherboard = d.Type == DeviceType.Motherboard
            });
        }
        return list;
    }

    private void DisconnectInternal(bool raiseEvent)
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
        _rawDevices = [];
        _activeDeviceIndex = -1;
        _zoneBufferPool.Clear();

        if (raiseEvent)
        {
            SetState(new HardwareConnectionState
            {
                IsConnected = false,
                Host = _state.Host,
                Port = _state.Port,
                StatusMessage = "Disconnected from OpenRGB Server"
            });
        }
    }

    private void SetState(HardwareConnectionState next)
    {
        _state = next;
        ConnectionStateChanged?.Invoke(this, next);
    }

    public void Dispose()
    {
        Disconnect();
    }
}
