using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;

namespace LuminaCore.Hardware;

/// <summary>
/// Unified multi-vendor user-space hardware controller:
/// 1. Native ASRock Polychrome USB HID (26CE:01A2)
/// 2. Native ASUS Aura / Gigabyte RGB Fusion / MSI Mystic Light USB HID
/// 3. Optional OpenRGB SDK (127.0.0.1:6742) fallback
/// + Optional WLED Wi-Fi UDP sync (port 21324).
/// </summary>
public sealed class SmartHardwareController : IHardwareController
{
    private readonly AsrockUsbHidHardwareController _asrockUsb = new();
    private readonly MultiVendorUsbHidController _multiVendorUsb = new();
    private readonly OpenRgbHardwareController _openRgb = new();
    private readonly WledUdpBroadcaster _wled = new();
    private IHardwareController? _activeBackend;
    private readonly object _lock = new();

    public SmartHardwareController()
    {
        _asrockUsb.ConnectionStateChanged += (_, state) =>
        {
            if (ReferenceEquals(_activeBackend, _asrockUsb))
                ConnectionStateChanged?.Invoke(this, state);
        };
        _multiVendorUsb.ConnectionStateChanged += (_, state) =>
        {
            if (ReferenceEquals(_activeBackend, _multiVendorUsb))
                ConnectionStateChanged?.Invoke(this, state);
        };
        _openRgb.ConnectionStateChanged += (_, state) =>
        {
            if (ReferenceEquals(_activeBackend, _openRgb))
                ConnectionStateChanged?.Invoke(this, state);
        };
    }

    public void ConfigureWled(bool enabled, string ipAddress) => _wled.Configure(enabled, ipAddress);

    public HardwareConnectionState ConnectionState
    {
        get
        {
            lock (_lock)
                return _activeBackend?.ConnectionState ?? _asrockUsb.ConnectionState;
        }
    }

    public event EventHandler<HardwareConnectionState>? ConnectionStateChanged;

    public async Task<bool> ConnectAsync(string host, int port, int preferredDeviceIndex = -1, CancellationToken ct = default)
    {
        // 1. Try Native ASRock Direct USB HID
        if (await _asrockUsb.ConnectAsync(host, port, preferredDeviceIndex, ct).ConfigureAwait(false))
        {
            lock (_lock) _activeBackend = _asrockUsb;
            _multiVendorUsb.Disconnect();
            _openRgb.Disconnect();
            ConnectionStateChanged?.Invoke(this, _asrockUsb.ConnectionState);
            return true;
        }

        // 2. Try Native ASUS / Gigabyte / MSI USB HID
        if (await _multiVendorUsb.ConnectAsync(host, port, preferredDeviceIndex, ct).ConfigureAwait(false))
        {
            lock (_lock) _activeBackend = _multiVendorUsb;
            _openRgb.Disconnect();
            ConnectionStateChanged?.Invoke(this, _multiVendorUsb.ConnectionState);
            return true;
        }

        // 3. Fallback to OpenRGB SDK if running
        if (await _openRgb.ConnectAsync(host, port, preferredDeviceIndex, ct).ConfigureAwait(false))
        {
            lock (_lock) _activeBackend = _openRgb;
            ConnectionStateChanged?.Invoke(this, _openRgb.ConnectionState);
            return true;
        }

        lock (_lock) _activeBackend = _asrockUsb;
        ConnectionStateChanged?.Invoke(this, _asrockUsb.ConnectionState);
        return false;
    }

    public void Disconnect()
    {
        _asrockUsb.Disconnect();
        _multiVendorUsb.Disconnect();
        _openRgb.Disconnect();
    }

    public void SelectDevice(int deviceIndex)
    {
        lock (_lock) _activeBackend?.SelectDevice(deviceIndex);
    }

    public void PushZoneColors(int zoneIndex, ReadOnlySpan<RgbColor> colors)
    {
        IHardwareController? backend;
        lock (_lock) backend = _activeBackend;
        backend?.PushZoneColors(zoneIndex, colors);

        if (zoneIndex == 0 && _wled.IsEnabled)
            _wled.SendStrip(colors);
    }

    public void SetAllBlackout(IReadOnlyList<ZoneLayout> zones)
    {
        IHardwareController? backend;
        lock (_lock) backend = _activeBackend;
        backend?.SetAllBlackout(zones);
    }

    public void SetChannelColorOrder(int channelIndex, ChannelColorOrder order)
    {
        IHardwareController? backend;
        lock (_lock) backend = _activeBackend;
        backend?.SetChannelColorOrder(channelIndex, order);
    }

    public void SetCalibrationPreview(int channelIndex, int litLedCount)
    {
        IHardwareController? backend;
        lock (_lock) backend = _activeBackend;
        backend?.SetCalibrationPreview(channelIndex, litLedCount);
    }

    public void StopCalibrationPreview()
    {
        IHardwareController? backend;
        lock (_lock) backend = _activeBackend;
        backend?.StopCalibrationPreview();
    }

    public HidDiagnosticsInfo GetHidDiagnostics()
    {
        IHardwareController? backend;
        lock (_lock) backend = _activeBackend ?? _asrockUsb;
        return backend.GetHidDiagnostics();
    }

    public void Dispose()
    {
        _asrockUsb.Dispose();
        _multiVendorUsb.Dispose();
        _openRgb.Dispose();
        _wled.Dispose();
    }
}
