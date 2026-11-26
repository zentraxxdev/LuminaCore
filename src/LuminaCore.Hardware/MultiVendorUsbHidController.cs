using System.Net.Sockets;
using HidSharp;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;

namespace LuminaCore.Hardware;

/// <summary>
/// User-space USB HID controllers for ASUS Aura, MSI Mystic Light, and Gigabyte RGB Fusion 2.0
/// plus optional WLED Wi-Fi UDP (DRGB port 21324) sync for global multi-vendor support.
/// </summary>
public sealed class MultiVendorUsbHidController : IHardwareController
{
    private enum VendorKind { None, AsusAura, MsiMystic, GigabyteFusion }

    private static readonly int[] AsusPids = [0x18F3, 0x1939, 0x19AF, 0x1AA6, 0x18A5];
    private static readonly int[] GigabytePids = [0x5702, 0x5711, 0x8297];

    private readonly object _lock = new();
    private HidStream? _stream;
    private VendorKind _vendor = VendorKind.None;
    private string _vidPid = "0000:0000";
    private string _deviceName = "None";
    private int _ledCount = 24;
    private readonly RgbColor[] _frameBuffer = new RgbColor[60];
    private readonly byte[] _packet = new byte[192];
    private long _lastFlushMs;
    private HardwareConnectionState _state = new();

    public HardwareConnectionState ConnectionState
    {
        get { lock (_lock) return _state; }
    }

    public event EventHandler<HardwareConnectionState>? ConnectionStateChanged;

    public Task<bool> ConnectAsync(string host, int port, int preferredDeviceIndex = -1, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                DisconnectInternal(raiseEvent: false);

                // 1. ASUS Aura USB (0x0B05)
                var asus = DeviceList.Local.GetHidDevices(0x0B05)
                    .FirstOrDefault(d => AsusPids.Contains(d.ProductID) && d.GetMaxOutputReportLength() >= 65);
                if (asus is not null && asus.TryOpen(out var asusStream))
                {
                    ConfigureConnected(asus, asusStream, VendorKind.AsusAura, "ASUS Aura USB Motherboard", 30);
                    return true;
                }

                // 2. Gigabyte RGB Fusion 2.0 USB (0x048D ITE)
                var giga = DeviceList.Local.GetHidDevices(0x048D)
                    .FirstOrDefault(d => GigabytePids.Contains(d.ProductID) && d.GetMaxFeatureReportLength() >= 64);
                if (giga is not null && giga.TryOpen(out var gigaStream))
                {
                    ConfigureConnected(giga, gigaStream, VendorKind.GigabyteFusion, "Gigabyte RGB Fusion 2.0 USB", 24);
                    return true;
                }

                // 3. MSI Mystic Light USB (0x1462)
                var msi = DeviceList.Local.GetHidDevices(0x1462)
                    .FirstOrDefault(d => d.ProductID >= 0x7B00 && d.ProductID <= 0x7FFF && d.GetMaxFeatureReportLength() >= 64);
                if (msi is not null && msi.TryOpen(out var msiStream))
                {
                    ConfigureConnected(msi, msiStream, VendorKind.MsiMystic, "MSI Mystic Light USB", 24);
                    return true;
                }

                return false;
            }
        }, ct);
    }

    private void ConfigureConnected(HidDevice dev, HidStream stream, VendorKind vendor, string fallbackName, int leds)
    {
        stream.WriteTimeout = 200;
        stream.ReadTimeout = 200;
        _stream = stream;
        _vendor = vendor;
        _vidPid = $"{dev.VendorID:X4}:{dev.ProductID:X4}";
        _deviceName = string.IsNullOrWhiteSpace(dev.GetProductName()) ? fallbackName : dev.GetProductName().Trim();
        _ledCount = leds;

        var zones = new List<HardwareZoneInfo>
        {
            new() { Index = 0, Name = "Channel 1", LedCount = leds, MinLeds = 1, MaxLeds = 60 },
            new() { Index = 1, Name = "Channel 2", LedCount = leds, MinLeds = 1, MaxLeds = 60 },
            new() { Index = 2, Name = "Channel 3", LedCount = leds, MinLeds = 1, MaxLeds = 60 }
        };

        var info = new HardwareDeviceInfo
        {
            DeviceIndex = 0,
            Name = $"{_deviceName} (Native HID)",
            DeviceType = "Motherboard",
            TotalLeds = leds * 3,
            Zones = zones,
            IsMotherboard = true
        };

        SetState(new HardwareConnectionState
        {
            IsConnected = true,
            Host = "USB-HID",
            Port = 0,
            StatusMessage = $"Direct USB HID: {_deviceName}",
            ActiveDevice = info,
            DiscoveredDevices = [info]
        });
    }

    public void PushZoneColors(int zoneIndex, ReadOnlySpan<RgbColor> colors)
    {
        if (colors.IsEmpty) return;

        lock (_lock)
        {
            if (_stream is null || _vendor == VendorKind.None) return;

            if (zoneIndex == 0)
            {
                int count = Math.Min(_ledCount, colors.Length);
                colors.Slice(0, count).CopyTo(_frameBuffer);
            }

            if (zoneIndex < 2) return;

            long now = Environment.TickCount64;
            if (now - _lastFlushMs < 33) return;
            _lastFlushMs = now;

            try
            {
                Array.Clear(_packet);
                if (_vendor == VendorKind.AsusAura)
                {
                    // ASUS Aura Direct RGB packet (0xEC, 0x40)
                    _packet[0] = 0xEC;
                    _packet[1] = 0x40;
                    _packet[2] = 0x80;
                    _packet[4] = (byte)Math.Min(20, _ledCount);
                    for (int i = 0; i < Math.Min(20, _ledCount); i++)
                    {
                        var c = _frameBuffer[i];
                        _packet[5 + i * 3] = c.R;
                        _packet[6 + i * 3] = c.G;
                        _packet[7 + i * 3] = c.B;
                    }
                    _stream.Write(_packet, 0, 65);
                }
                else if (_vendor == VendorKind.GigabyteFusion)
                {
                    var c = _frameBuffer[0];
                    _packet[0] = 0xCC;
                    _packet[1] = 0x20;
                    _packet[11] = 0x01; // Static/Direct
                    _packet[13] = 0x64; // Brightness
                    _packet[14] = c.B;
                    _packet[15] = c.G;
                    _packet[16] = c.R;
                    _stream.SetFeature(_packet, 0, 64);
                }
                else if (_vendor == VendorKind.MsiMystic)
                {
                    var c = _frameBuffer[0];
                    _packet[0] = 0x52;
                    _packet[1] = 0x01; // Static
                    _packet[2] = c.R;
                    _packet[3] = c.G;
                    _packet[4] = c.B;
                    _packet[5] = 0x28;
                    _packet[6] = 0x64;
                    _stream.SetFeature(_packet, 0, 185);
                }
            }
            catch
            {
                DisconnectInternal(raiseEvent: true);
            }
        }
    }

    public void SetAllBlackout(IReadOnlyList<ZoneLayout> zones)
    {
        lock (_lock)
        {
            Array.Clear(_frameBuffer);
            PushZoneColors(2, _frameBuffer.AsSpan(0, _ledCount));
        }
    }

    public void Disconnect()
    {
        lock (_lock) DisconnectInternal(raiseEvent: true);
    }

    public void SelectDevice(int deviceIndex) { }
    public void SetChannelColorOrder(int channelIndex, ChannelColorOrder order) { }
    public void SetCalibrationPreview(int channelIndex, int litLedCount) { }
    public void StopCalibrationPreview() { }

    public HidDiagnosticsInfo GetHidDiagnostics()
    {
        lock (_lock)
        {
            return new HidDiagnosticsInfo
            {
                IsConnected = _state.IsConnected,
                ControllerName = _deviceName,
                VidPid = _vidPid,
                ChannelCount = 3,
                DiagnosticReport = $"{_deviceName} ({_vidPid})"
            };
        }
    }

    private void DisconnectInternal(bool raiseEvent)
    {
        try { _stream?.Dispose(); } catch { }
        _stream = null;
        _vendor = VendorKind.None;
        if (raiseEvent)
        {
            SetState(new HardwareConnectionState { IsConnected = false, StatusMessage = "Disconnected" });
        }
    }

    private void SetState(HardwareConnectionState next)
    {
        _state = next;
        ConnectionStateChanged?.Invoke(this, next);
    }

    public void Dispose() => Disconnect();
}

/// <summary>
/// Lightweight WLED UDP DRGB (port 21324) transmitter for syncing Wi-Fi LED strips with the PC.
/// </summary>
public sealed class WledUdpBroadcaster : IDisposable
{
    private readonly object _lock = new();
    private UdpClient? _udp;
    private string _targetIp = string.Empty;
    private byte[] _packet = new byte[2 + 60 * 3];
    private long _lastSendMs;

    public bool IsEnabled { get; private set; }

    public void Configure(bool enabled, string ipAddress)
    {
        lock (_lock)
        {
            IsEnabled = enabled && !string.IsNullOrWhiteSpace(ipAddress);
            if (!IsEnabled)
            {
                _udp?.Dispose();
                _udp = null;
                return;
            }

            if (!string.Equals(_targetIp, ipAddress.Trim(), StringComparison.Ordinal))
            {
                _targetIp = ipAddress.Trim();
                _udp?.Dispose();
                _udp = new UdpClient();
            }
        }
    }

    public void SendStrip(ReadOnlySpan<RgbColor> colors)
    {
        if (!IsEnabled || colors.IsEmpty) return;

        long now = Environment.TickCount64;
        if (now - _lastSendMs < 33) return; // 30 Hz WLED UDP sync
        _lastSendMs = now;

        lock (_lock)
        {
            if (!IsEnabled || _udp is null) return;
            try
            {
                int count = Math.Min(colors.Length, 120);
                int len = 2 + count * 3;
                if (_packet.Length < len) _packet = new byte[len];
                _packet[0] = 2; // DRGB protocol
                _packet[1] = 2; // 2-second timeout

                for (int i = 0; i < count; i++)
                {
                    var c = colors[i];
                    _packet[2 + i * 3] = c.R;
                    _packet[3 + i * 3] = c.G;
                    _packet[4 + i * 3] = c.B;
                }

                _udp.Send(_packet, len, _targetIp, 21324);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _udp?.Dispose();
            _udp = null;
        }
    }
}
