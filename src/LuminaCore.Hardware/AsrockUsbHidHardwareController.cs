using System.Diagnostics;
using System.Text;
using HidSharp;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;

namespace LuminaCore.Hardware;

/// <summary>
/// Direct user-space USB HID driver for ASRock Polychrome USB motherboards (VID 0x26CE, PID 0x01A2).
/// Communicates straight with the motherboard LED microcontroller over standard Windows HID —
/// zero OpenRGB, zero PawnIO, zero kernel drivers, and 100% anti-cheat safe.
/// </summary>
public sealed class AsrockUsbHidHardwareController : IHardwareController
{
    public const int AsrockVendorId = 0x26CE;
    public const int PolychromeProductId = 0x01A2;

    private const byte CmdSetZone = 0x10;
    private const byte CmdCommit = 0x12;
    private const byte CmdReadHeader = 0x14;
    private const byte CmdWriteHeader = 0x15;
    private const byte CmdInitialChunk = 0xE3;
    private const byte CmdSendChunk = 0xE4;

    private const byte CfgLedCount = 0x02;
    private const byte CfgRgSwap = 0x03;
    private const byte ZoneUnavailable = 0x1E;
    private const byte ModeDirect = 0x0F;
    private const long MinFlushIntervalMs = 33; // ~30 Hz max for Polychrome USB MCU stability

    private static readonly string[] ZoneNames =
    [
        "RGB LED 1 Header",
        "RGB LED 2 Header",
        "Addressable Header 1",
        "Addressable Header 2",
        "PCH",
        "IO Cover",
        "PCB",
        "Addressable Header 3 / Audio"
    ];

    private sealed class HidZoneSlot
    {
        public int ZoneIndex { get; init; }
        public byte ZoneType { get; init; }
        public string Name { get; init; } = string.Empty;
        public int LedCount { get; set; }
        public ChannelColorOrder ColorOrder { get; set; } = ChannelColorOrder.GRB;
        public RgbColor[] FrameColors { get; set; } = [];
    }

    private readonly object _lock = new();
    private readonly object _ioLock = new();
    private HidDevice? _hidDevice;
    private HidStream? _stream;
    private readonly List<HidZoneSlot> _slots = [];
    private readonly byte[] _writeBuf = new byte[65];
    private readonly byte[] _readBuf = new byte[65];
    private byte[] _combinedGrbBuffer = [];
    private byte[] _lastSentGrbBuffer = [];
    private byte[] _pendingGrbBuffer = [];
    private volatile bool _hasPendingFrame;
    private readonly AutoResetEvent _flushSignal = new(false);
    private CancellationTokenSource? _flushCts;
    private Task? _flushTask;
    private long _lastFlushTimestamp;
    private int _maxLedSum;
    private int _dirtyZoneMask;
    private double _lastPacketLatencyMs;
    private double _measuredHwFps;
    private int _hwFrameCount;
    private long _hwFpsWindowStartMs;
    private bool _isCalibrating;
    private int _calibrationChannel = -1;
    private int _calibrationLitLeds;
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

                try
                {
                    var hidDev = DeviceList.Local.GetHidDevices(AsrockVendorId, PolychromeProductId)
                        .FirstOrDefault(d => d.GetMaxOutputReportLength() >= 65 && d.GetMaxInputReportLength() >= 64);

                    if (hidDev is null)
                    {
                        SetState(new HardwareConnectionState
                        {
                            IsConnected = false,
                            Host = "USB-HID",
                            Port = 0,
                            StatusMessage = "No ASRock Polychrome USB HID (26CE:01A2) detected"
                        });
                        return false;
                    }

                    if (!hidDev.TryOpen(out var stream))
                    {
                        SetState(new HardwareConnectionState
                        {
                            IsConnected = false,
                            Host = "USB-HID",
                            Port = 0,
                            StatusMessage = "ASRock USB HID busy (close OpenRGB/Polychrome if running)"
                        });
                        return false;
                    }

                    stream.ReadTimeout = 250;
                    stream.WriteTimeout = 250;

                    _hidDevice = hidDev;
                    _stream = stream;

                    InitializeControllerAndDiscoverZones();

                    var zoneInfos = _slots.Select(s => new HardwareZoneInfo
                    {
                        Index = s.ZoneIndex,
                        Name = s.Name,
                        LedCount = s.LedCount,
                        MinLeds = 1,
                        MaxLeds = 100
                    }).ToList();

                    string friendly = string.IsNullOrWhiteSpace(hidDev.GetProductName())
                        ? "ASRock Polychrome USB"
                        : hidDev.GetProductName().Trim();

                    var deviceInfo = new HardwareDeviceInfo
                    {
                        DeviceIndex = 0,
                        Name = $"{friendly} (Direct USB HID)",
                        DeviceType = "Motherboard (Native HID)",
                        TotalLeds = _slots.Sum(s => s.LedCount),
                        Zones = zoneInfos,
                        IsMotherboard = true
                    };

                    SetState(new HardwareConnectionState
                    {
                        IsConnected = true,
                        Host = "USB-HID",
                        Port = 0,
                        StatusMessage = $"Direct USB HID: {friendly} ({zoneInfos.Count} zones)",
                        ActiveDevice = deviceInfo,
                        DiscoveredDevices = [deviceInfo]
                    });

                    return true;
                }
                catch (Exception ex)
                {
                    DisconnectInternal(raiseEvent: false);
                    SetState(new HardwareConnectionState
                    {
                        IsConnected = false,
                        Host = "USB-HID",
                        Port = 0,
                        StatusMessage = $"USB HID error: {ex.Message}"
                    });
                    return false;
                }
            }
        }, ct);
    }

    private void InitializeControllerAndDiscoverZones()
    {
        _slots.Clear();

        // 1. Read LED counts for all 8 internal Polychrome channels
        // Note: In HidSharp, InLen=65 includes Report ID at index 0, subcmd echo at index 4, data at index 5..12.
        byte[] ledCounts = new byte[8];
        if (SendAndReceive(CmdReadHeader, CfgLedCount))
        {
            Array.Copy(_readBuf, 5, ledCounts, 0, 8);
        }

        // 2. Read active zone bitmask (header1) at index 5
        byte activeMask = 0xFF;
        if (SendAndReceive(CmdReadHeader, 0x01))
        {
            activeMask = _readBuf[5];
        }

        // 3. Disable RGSwap so Direct Mode does not flash on each frame (OpenRGB SetRGSwap(false))
        Array.Clear(_writeBuf);
        _writeBuf[1] = CmdWriteHeader;
        _writeBuf[3] = CfgRgSwap;
        _writeBuf[4] = 0x00;
        WriteAndDrain();

        int zoneIdx = 0;
        _maxLedSum = 0;

        for (int ch = 0; ch < 8; ch++)
        {
            bool isPresent = ledCounts[ch] != ZoneUnavailable && ((activeMask >> ch) & 1) == 1;
            if (!isPresent || ledCounts[ch] == 0) continue;

            int count = ledCounts[ch];
            _slots.Add(new HidZoneSlot
            {
                ZoneIndex = zoneIdx++,
                ZoneType = (byte)ch,
                Name = ZoneNames[ch],
                LedCount = count,
                FrameColors = new RgbColor[count]
            });

            _maxLedSum += count;
        }

        // Fallback if firmware reports all 0s
        if (_slots.Count == 0)
        {
            for (int i = 0; i < 3; i++)
            {
                byte ch = i switch { 0 => (byte)2, 1 => (byte)3, _ => (byte)7 };
                _slots.Add(new HidZoneSlot
                {
                    ZoneIndex = i,
                    ZoneType = ch,
                    Name = ZoneNames[ch],
                    LedCount = 24,
                    FrameColors = new RgbColor[24]
                });
            }
            _maxLedSum = 72;
        }

        // 4. Switch every active zone into Direct Mode (0x0F) so hardware effects stop fighting
        foreach (var slot in _slots)
        {
            Array.Clear(_writeBuf);
            _writeBuf[0] = 0x00;
            _writeBuf[1] = CmdSetZone;
            _writeBuf[3] = slot.ZoneType;
            _writeBuf[4] = ModeDirect;
            _writeBuf[8] = 0xE0; // Default speed
            _writeBuf[9] = 0xFF; // Max brightness
            WriteAndDrain();
        }

        int bufLen = (_maxLedSum + 8) * 3;
        _combinedGrbBuffer = new byte[bufLen];
        _lastSentGrbBuffer = new byte[bufLen];
        _pendingGrbBuffer = new byte[bufLen];
        _hasPendingFrame = false;
        _lastFlushTimestamp = 0;
        _dirtyZoneMask = 0;

        _flushCts = new CancellationTokenSource();
        var token = _flushCts.Token;
        _flushTask = Task.Factory.StartNew(() => HidFlushWorker(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void PushZoneColors(int zoneIndex, ReadOnlySpan<RgbColor> colors)
    {
        if (colors.IsEmpty) return;

        lock (_lock)
        {
            if (_stream is null || _slots.Count == 0) return;

            // Map virtual zones (0, 1, 2) onto the real motherboard slots:
            // If motherboard has 5 slots (RGB1, ARGB1, ARGB2, PCB, ARGB3), map 0->ARGB1(+RGB1), 1->ARGB2, 2->ARGB3(+PCB)
            if (_slots.Count >= 5)
            {
                switch (zoneIndex)
                {
                    case 0:
                        CopyResampled(colors, _slots[0].FrameColors); // RGB1
                        CopyResampled(colors, _slots[1].FrameColors); // ARGB1
                        break;
                    case 1:
                        CopyResampled(colors, _slots[2].FrameColors); // ARGB2
                        break;
                    case 2:
                        CopyResampled(colors, _slots[3].FrameColors); // PCB
                        CopyResampled(colors, _slots[4].FrameColors); // ARGB3
                        break;
                    default:
                        if (zoneIndex >= 0 && zoneIndex < _slots.Count)
                            CopyResampled(colors, _slots[zoneIndex].FrameColors);
                        break;
                }
            }
            else
            {
                int targetSlot = Math.Clamp(zoneIndex, 0, _slots.Count - 1);
                CopyResampled(colors, _slots[targetSlot].FrameColors);
            }

            _dirtyZoneMask |= 1 << Math.Clamp(zoneIndex, 0, 30);

            // Signal background USB HID worker without blocking the 60 FPS render loop
            bool readyToFlush = zoneIndex >= 2 || (_slots.Count <= 3 && zoneIndex == _slots.Count - 1);
            if (readyToFlush)
            {
                _dirtyZoneMask = 0;
                long nowMs = Environment.TickCount64;
                if (nowMs - _lastFlushTimestamp >= MinFlushIntervalMs)
                {
                    _lastFlushTimestamp = nowMs;
                    PackSlotsIntoPendingBuffer();
                    _hasPendingFrame = true;
                    _flushSignal.Set();
                }
            }
        }
    }

    private static void CopyResampled(ReadOnlySpan<RgbColor> src, RgbColor[] dst)
    {
        int targetCount = dst.Length;
        if (targetCount == 0) return;

        if (targetCount == src.Length)
        {
            src.CopyTo(dst);
            return;
        }

        for (int i = 0; i < targetCount; i++)
        {
            int srcIdx = targetCount <= 1 ? 0 : (int)MathF.Round(i * (src.Length - 1) / (float)(targetCount - 1));
            dst[i] = src[Math.Clamp(srcIdx, 0, src.Length - 1)];
        }
    }

    private void PackSlotsIntoPendingBuffer()
    {
        Array.Clear(_pendingGrbBuffer);
        int byteCursor = 0;
        for (int s = 0; s < _slots.Count; s++)
        {
            var slot = _slots[s];
            var frame = slot.FrameColors;
            var order = slot.ColorOrder;
            bool calActive = _isCalibrating;

            for (int i = 0; i < frame.Length; i++)
            {
                if (byteCursor + 2 >= _pendingGrbBuffer.Length) break;

                RgbColor c;
                if (calActive)
                {
                    bool isTargetSlot = MatchesCalibrationChannel(s, _calibrationChannel);
                    c = (isTargetSlot && i < _calibrationLitLeds)
                        ? new RgbColor(0, 255, 68)
                        : RgbColor.Black;
                }
                else
                {
                    c = frame[i];
                }

                // Zero-allocation per-channel color order mapping
                switch (order)
                {
                    case ChannelColorOrder.RGB:
                        _pendingGrbBuffer[byteCursor++] = c.R;
                        _pendingGrbBuffer[byteCursor++] = c.G;
                        _pendingGrbBuffer[byteCursor++] = c.B;
                        break;
                    case ChannelColorOrder.BRG:
                        _pendingGrbBuffer[byteCursor++] = c.B;
                        _pendingGrbBuffer[byteCursor++] = c.R;
                        _pendingGrbBuffer[byteCursor++] = c.G;
                        break;
                    case ChannelColorOrder.RBG:
                        _pendingGrbBuffer[byteCursor++] = c.R;
                        _pendingGrbBuffer[byteCursor++] = c.B;
                        _pendingGrbBuffer[byteCursor++] = c.G;
                        break;
                    case ChannelColorOrder.GRB:
                    default:
                        _pendingGrbBuffer[byteCursor++] = c.G;
                        _pendingGrbBuffer[byteCursor++] = c.R;
                        _pendingGrbBuffer[byteCursor++] = c.B;
                        break;
                }
            }
        }
    }

    private bool MatchesCalibrationChannel(int slotIndex, int channelIndex)
    {
        if (_slots.Count >= 5)
        {
            return channelIndex switch
            {
                0 => slotIndex is 0 or 1,
                1 => slotIndex == 2,
                2 => slotIndex is 3 or 4,
                _ => slotIndex == channelIndex
            };
        }
        return slotIndex == channelIndex;
    }

    private void HidFlushWorker(CancellationToken ct)
    {
        _hwFpsWindowStartMs = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            _flushSignal.WaitOne(50);
            if (ct.IsCancellationRequested) break;
            if (!_hasPendingFrame) continue;

            // Hold _lock ONLY for the fast memcpy (<0.005 ms) so the 60 FPS RenderPipeline never blocks!
            bool shouldSend = false;
            lock (_lock)
            {
                if (_stream is null || !_hasPendingFrame) continue;
                _hasPendingFrame = false;
                if (!_pendingGrbBuffer.AsSpan().SequenceEqual(_lastSentGrbBuffer))
                {
                    Buffer.BlockCopy(_pendingGrbBuffer, 0, _combinedGrbBuffer, 0, _combinedGrbBuffer.Length);
                    Buffer.BlockCopy(_pendingGrbBuffer, 0, _lastSentGrbBuffer, 0, _lastSentGrbBuffer.Length);
                    shouldSend = true;
                }
            }

            if (shouldSend)
            {
                long t0 = Stopwatch.GetTimestamp();
                lock (_ioLock)
                {
                    SendCombinedBufferOverHid();
                }
                _lastPacketLatencyMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                _hwFrameCount++;
                long now = Environment.TickCount64;
                long span = now - _hwFpsWindowStartMs;
                if (span >= 1000)
                {
                    _measuredHwFps = _hwFrameCount * 1000.0 / span;
                    _hwFrameCount = 0;
                    _hwFpsWindowStartMs = now;
                }
            }
        }
    }

    public void SetChannelColorOrder(int channelIndex, ChannelColorOrder order)
    {
        lock (_lock)
        {
            for (int s = 0; s < _slots.Count; s++)
            {
                if (MatchesCalibrationChannel(s, channelIndex) || s == channelIndex)
                    _slots[s].ColorOrder = order;
            }
            Array.Clear(_lastSentGrbBuffer);
            PackSlotsIntoPendingBuffer();
            _hasPendingFrame = true;
            _flushSignal.Set();
        }
    }

    public void SetCalibrationPreview(int channelIndex, int litLedCount)
    {
        lock (_lock)
        {
            _isCalibrating = true;
            _calibrationChannel = channelIndex;
            _calibrationLitLeds = Math.Max(0, litLedCount);
            Array.Clear(_lastSentGrbBuffer);
            PackSlotsIntoPendingBuffer();
            _hasPendingFrame = true;
            _flushSignal.Set();
        }
    }

    public void StopCalibrationPreview()
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
            _isCalibrating = false;
            _calibrationChannel = -1;
            Array.Clear(_lastSentGrbBuffer);
            PackSlotsIntoPendingBuffer();
            _hasPendingFrame = true;
            _flushSignal.Set();
        }
    }

    public HidDiagnosticsInfo GetHidDiagnostics()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== LuminaCore Native USB HID Diagnostic Report ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:O}");
            sb.AppendLine($"Connected: {_state.IsConnected}");
            sb.AppendLine($"Device: {_state.ActiveDevice?.Name ?? "None"}");
            sb.AppendLine($"VID:PID: {AsrockVendorId:X4}:{PolychromeProductId:X4}");
            sb.AppendLine($"Channels: {_slots.Count} (Total LEDs: {_slots.Sum(s => s.LedCount)})");
            sb.AppendLine($"Packet Latency: {_lastPacketLatencyMs:F2} ms | Hardware Push Rate: {_measuredHwFps:F1} Hz");
            foreach (var s in _slots)
                sb.AppendLine($"  - Slot {s.ZoneIndex} (Ch {s.ZoneType}): {s.Name} | {s.LedCount} LEDs | Order={s.ColorOrder}");

            return new HidDiagnosticsInfo
            {
                IsConnected = _state.IsConnected,
                ControllerName = _state.ActiveDevice?.Name ?? "ASRock Polychrome USB HID",
                VidPid = $"{AsrockVendorId:X4}:{PolychromeProductId:X4}",
                ChannelCount = _slots.Count,
                PacketLatencyMs = Math.Round(_lastPacketLatencyMs, 2),
                HardwareFps = Math.Round(_measuredHwFps, 1),
                IsCalibrating = _isCalibrating,
                CalibrationChannel = _calibrationChannel,
                CalibrationLitLeds = _calibrationLitLeds,
                DiagnosticReport = sb.ToString()
            };
        }
    }

    private void SendCombinedBufferOverHid()
    {
        if (_stream is null) return;

        try
        {
            // Stream full padded buffer in 65-byte HID chunks (0xE3 initial, 0xE4 continuation)
            int totalBytesToSend = _combinedGrbBuffer.Length;
            int ledByteIdx = 0;
            bool isFirst = true;

            while (ledByteIdx < totalBytesToSend)
            {
                Array.Clear(_writeBuf);
                _writeBuf[0] = 0x00; // Report ID
                _writeBuf[1] = CmdSetZone;
                _writeBuf[3] = 0xFF;
                _writeBuf[4] = isFirst ? CmdInitialChunk : CmdSendChunk;
                if (isFirst) _writeBuf[7] = 0xFF;
                _writeBuf[0x40] = 0x65;

                int offset = isFirst ? 9 : 5;
                int capacity = isFirst ? 54 : 57;

                for (int b = 0; b < capacity && ledByteIdx < totalBytesToSend; b++)
                {
                    _writeBuf[offset + b] = _combinedGrbBuffer[ledByteIdx++];
                }

                WriteAndDrain();
                isFirst = false;
            }
        }
        catch (Exception ex)
        {
            DisconnectInternal(raiseEvent: false);
            SetState(new HardwareConnectionState
            {
                IsConnected = false,
                Host = "USB-HID",
                Port = 0,
                StatusMessage = $"USB HID disconnected ({ex.Message})"
            });
        }
    }

    public void SetAllBlackout(IReadOnlyList<ZoneLayout> zones)
    {
        lock (_lock)
        {
            foreach (var slot in _slots)
                Array.Fill(slot.FrameColors, RgbColor.Black);
            PackSlotsIntoPendingBuffer();
            _hasPendingFrame = true;
            _flushSignal.Set();
        }
    }

    public void SelectDevice(int deviceIndex) { }

    public void Disconnect()
    {
        lock (_lock)
        {
            DisconnectInternal(raiseEvent: true);
        }
    }

    private bool SendAndReceive(byte cmd, byte subCmd)
    {
        Array.Clear(_writeBuf);
        _writeBuf[0] = 0x00;
        _writeBuf[1] = cmd;
        _writeBuf[3] = subCmd;
        return WriteAndDrain();
    }

    private bool WriteAndDrain()
    {
        if (_stream is null) return false;
        _stream.Write(_writeBuf, 0, _writeBuf.Length);
        Array.Clear(_readBuf);
        try
        {
            _stream.Read(_readBuf, 0, _readBuf.Length);
        }
        catch (TimeoutException)
        {
            // Some firmware revisions don't ACK every chunk
        }
        return true;
    }

    private void DisconnectInternal(bool raiseEvent)
    {
        try { _flushCts?.Cancel(); } catch { }
        _flushSignal.Set();
        _flushCts?.Dispose();
        _flushCts = null;
        _flushTask = null;

        try { _stream?.Dispose(); } catch { }
        _stream = null;
        _hidDevice = null;
        _slots.Clear();

        if (raiseEvent)
        {
            SetState(new HardwareConnectionState
            {
                IsConnected = false,
                Host = "USB-HID",
                Port = 0,
                StatusMessage = "USB HID Disconnected"
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
