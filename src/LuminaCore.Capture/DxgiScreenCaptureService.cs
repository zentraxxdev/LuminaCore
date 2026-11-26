using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LuminaCore.Capture;

/// <summary>
/// Ultra-low-overhead 60 FPS DXGI Desktop Duplication screen capture service.
/// Captures the primary display and downsamples directly to a 16x9 RGB grid (144 cells)
/// with zero per-frame managed heap allocations and automatic recovery on DXGI session reset.
/// </summary>
public sealed class DxgiScreenCaptureService : IScreenCaptureService
{
    private readonly object _gridLock = new();
    private readonly RgbColor[] _latestCells = new RgbColor[ScreenGridFrame.CellCount];
    private bool _hasValidFrame;
    private long _frameSeq;
    private string _statusMessage = "Idle";

    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;
    public string StatusMessage => _statusMessage;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _statusMessage = "Initializing DXGI Desktop Duplication...";
        _cts = new CancellationTokenSource();
        _captureTask = Task.Factory.StartNew(
            () => CaptureLoop(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
        _statusMessage = "Stopped";
    }

    public void CopyLatestGrid(ScreenGridFrame target)
    {
        lock (_gridLock)
        {
            Array.Copy(_latestCells, target.Cells, ScreenGridFrame.CellCount);
            target.HasValidFrame = _hasValidFrame;
            target.FrameSequence = _frameSeq;
            target.StatusText = _statusMessage;
        }
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var tempGrid = new RgbColor[ScreenGridFrame.CellCount];

        while (!ct.IsCancellationRequested)
        {
            IDXGIFactory1? factory = null;
            IDXGIAdapter1? adapter = null;
            IDXGIOutput? output = null;
            IDXGIOutput1? output1 = null;
            ID3D11Device? device = null;
            ID3D11DeviceContext? context = null;
            IDXGIOutputDuplication? duplication = null;
            ID3D11Texture2D? stagingTexture = null;

            try
            {
                factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                factory.EnumAdapters1(0, out adapter).CheckError();
                adapter.EnumOutputs(0, out output).CheckError();
                output1 = output.QueryInterface<IDXGIOutput1>();

                var featureLevels = new[]
                {
                    FeatureLevel.Level_11_1,
                    FeatureLevel.Level_11_0
                };

                D3D11.D3D11CreateDevice(
                    adapter,
                    DriverType.Unknown,
                    DeviceCreationFlags.BgraSupport,
                    featureLevels,
                    out device,
                    out context).CheckError();

                duplication = output1.DuplicateOutput(device);

                var outputDesc = output.Description;
                int width = Math.Max(1, outputDesc.DesktopCoordinates.Right - outputDesc.DesktopCoordinates.Left);
                int height = Math.Max(1, outputDesc.DesktopCoordinates.Bottom - outputDesc.DesktopCoordinates.Top);

                var stagingDesc = new Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                    MiscFlags = ResourceOptionFlags.None
                };
                stagingTexture = device.CreateTexture2D(stagingDesc);
                _statusMessage = $"DXGI Active ({width}x{height} -> 16x9)";

                while (!ct.IsCancellationRequested)
                {
                    var result = duplication.AcquireNextFrame(16, out var frameInfo, out var desktopResource);

                    if (result == Vortice.DXGI.ResultCode.WaitTimeout)
                    {
                        continue;
                    }

                    if (result.Failure)
                    {
                        // DXGI_ERROR_ACCESS_LOST (resolution change / UAC / exclusive fullscreen) -> rebuild
                        _statusMessage = "DXGI session reset — reinitializing...";
                        break;
                    }

                    try
                    {
                        if (desktopResource is not null && frameInfo.LastPresentTime != 0)
                        {
                            using var screenTex = desktopResource.QueryInterface<ID3D11Texture2D>();
                            context.CopyResource(stagingTexture, screenTex);

                            var mapped = context.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                            try
                            {
                                DownsampleBgraTo16x9(mapped.DataPointer, (int)mapped.RowPitch, width, height, tempGrid);
                                lock (_gridLock)
                                {
                                    Array.Copy(tempGrid, _latestCells, ScreenGridFrame.CellCount);
                                    _hasValidFrame = true;
                                    _frameSeq++;
                                }
                            }
                            finally
                            {
                                context.Unmap(stagingTexture, 0);
                            }
                        }
                    }
                    finally
                    {
                        desktopResource?.Dispose();
                        try { duplication.ReleaseFrame(); } catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _statusMessage = $"DXGI unavailable ({ex.Message}) — retrying...";
                if (ct.WaitHandle.WaitOne(1200))
                    break;
            }
            finally
            {
                stagingTexture?.Dispose();
                duplication?.Dispose();
                context?.Dispose();
                device?.Dispose();
                output1?.Dispose();
                output?.Dispose();
                adapter?.Dispose();
                factory?.Dispose();
            }
        }
    }

    /// <summary>
    /// Fast strided unsafe BGRA32 downsampler into 16x9 cells (samples an 8x6 subgrid inside each cell).
    /// </summary>
    private static unsafe void DownsampleBgraTo16x9(IntPtr dataPtr, int rowPitch, int width, int height, RgbColor[] dest)
    {
        byte* basePtr = (byte*)dataPtr;
        const int gw = ScreenGridFrame.GridWidth;
        const int gh = ScreenGridFrame.GridHeight;

        const int subSamplesX = 6;
        const int subSamplesY = 4;

        float cellW = width / (float)gw;
        float cellH = height / (float)gh;

        for (int gy = 0; gy < gh; gy++)
        {
            float yStart = gy * cellH;
            for (int gx = 0; gx < gw; gx++)
            {
                float xStart = gx * cellW;
                int sumR = 0, sumG = 0, sumB = 0;
                int count = 0;

                for (int sy = 0; sy < subSamplesY; sy++)
                {
                    int py = Math.Clamp((int)(yStart + (sy + 0.5f) * (cellH / subSamplesY)), 0, height - 1);
                    byte* row = basePtr + py * rowPitch;

                    for (int sx = 0; sx < subSamplesX; sx++)
                    {
                        int px = Math.Clamp((int)(xStart + (sx + 0.5f) * (cellW / subSamplesX)), 0, width - 1);
                        byte* pixel = row + px * 4;
                        sumB += pixel[0];
                        sumG += pixel[1];
                        sumR += pixel[2];
                        count++;
                    }
                }

                dest[gy * gw + gx] = new RgbColor(
                    (byte)(sumR / count),
                    (byte)(sumG / count),
                    (byte)(sumB / count));
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
