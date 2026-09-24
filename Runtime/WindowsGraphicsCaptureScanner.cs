using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRtDirect3DDevice = Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;
using WinRtDirect3DSurface = Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface;

namespace Shigure;

/// <summary>
/// 直接从目标窗口的 DWM 图形表面读取 Fuyutsui 顶部像素，窗口被其他窗口遮挡时仍可工作。
/// </summary>
internal sealed class WindowsGraphicsCaptureScanner : IRuntimeScreenScanner
{
    private const int CaptureStripHeight = 128;
    private static readonly TimeSpan StaleFrameTimeout = TimeSpan.FromSeconds(1);
    private static readonly Guid GraphicsCaptureItemInteropId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Direct3DDxgiInterfaceAccessId = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private const string GraphicsCaptureItemRuntimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";

    private readonly WowProcessLocator _processLocator;
    private readonly TimeSpan _minimumFrameInterval;
    private readonly object _sync = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _deviceContext;
    private WinRtDirect3DDevice? _winRtDevice;
    private ID3D11Texture2D? _stagingTexture;
    private int _stagingWidth;
    private int _stagingHeight;
    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private SizeInt32 _captureSize;
    private nint _targetWindow;
    private long _lastFrameCopiedAt;
    private CapturedTopStrip? _latestFrame;
    private string? _lastError;
    private bool _disposed;

    public WindowsGraphicsCaptureScanner(WowProcessLocator processLocator, TimeSpan logicInterval)
    {
        _processLocator = processLocator;
        _minimumFrameInterval = logicInterval < TimeSpan.FromMilliseconds(50)
            ? TimeSpan.FromMilliseconds(50)
            : logicInterval;
    }

    public ScreenScanResult ScanScreenData()
    {
        var emptyBars = new Dictionary<int, int>();
        var emptyAbsorb = new Dictionary<int, int>();
        var hwnd = _processLocator.FindFrontmostWindow();
        if (hwnd == 0)
        {
            ResetTarget();
            return Failure(
                emptyBars,
                emptyAbsorb,
                $"未找到目标进程的可见窗口（wow_process.txt: {_processLocator.DescribeConfiguredProcesses()}）");
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            return Failure(emptyBars, emptyAbsorb, "目标窗口已最小化，WGC 扫描已暂停");
        }

        CapturedTopStrip? frame;
        string? error;
        lock (_sync)
        {
            if (_disposed)
            {
                return Failure(emptyBars, emptyAbsorb, "WGC 扫描器已停止");
            }

            if (_targetWindow != hwnd || _captureSession is null)
            {
                StartSessionLocked(hwnd);
            }

            frame = _latestFrame;
            error = _lastError;
        }

        if (frame is null || frame.WindowHandle != hwnd)
        {
            return Failure(emptyBars, emptyAbsorb, error ?? "正在等待 WGC 首帧");
        }

        if (Stopwatch.GetElapsedTime(frame.CapturedAt) > StaleFrameTimeout)
        {
            return Failure(emptyBars, emptyAbsorb, "WGC 超过 1 秒未收到新帧，扫描已暂停");
        }

        try
        {
            var pixels = frame.Pixels.AsSpan();
            var rowData = PixelScanDecoder.DecodeTopRow(pixels[..frame.Width]);
            var markerY = PixelScanDecoder.FindCountBarsMarkerY(pixels, frame.Width, frame.Height);
            var barData = markerY is null
                ? emptyBars
                : PixelScanDecoder.DecodeMarkerRow(
                    pixels.Slice(markerY.Value * frame.Width, frame.Width));
            var absorbData = markerY is null
                ? emptyAbsorb
                : PixelScanDecoder.DecodeHealAbsorbGrid(
                    pixels,
                    frame.Width,
                    frame.Height,
                    markerY.Value);
            var result = rowData.Count == 0
                ? new ScreenScanResult(null, barData, absorbData, "未找到有效的状态像素起始标记")
                : new ScreenScanResult(
                    rowData,
                    barData,
                    absorbData,
                    markerY is null ? "未找到 CountBars 标记，层数条和治疗吸收数据未采集" : null);
            return result with { TargetWindowHandle = hwnd };
        }
        catch (Exception ex)
        {
            return Failure(emptyBars, emptyAbsorb, $"WGC 解码失败: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeSessionLocked();
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _winRtDevice?.Dispose();
            _winRtDevice = null;
            _deviceContext?.Dispose();
            _deviceContext = null;
            _device?.Dispose();
            _device = null;
        }
    }

    private static ScreenScanResult Failure(
        IReadOnlyDictionary<int, int> bars,
        IReadOnlyDictionary<int, int> absorb,
        string reason)
        => new(null, bars, absorb, reason);

    private void ResetTarget()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                DisposeSessionLocked();
            }
        }
    }

    private void StartSessionLocked(nint hwnd)
    {
        DisposeSessionLocked();
        _targetWindow = hwnd;
        _lastError = null;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)
                || !GraphicsCaptureSession.IsSupported())
            {
                _lastError = "当前 Windows 版本或图形设备不支持 WGC";
                return;
            }

            EnsureDeviceLocked();
            var item = CreateItemForWindow(hwnd);
            var size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                _lastError = "WGC 返回了无效的目标窗口尺寸";
                return;
            }

            var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winRtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);
            var session = framePool.CreateCaptureSession(item);
            try
            {
                session.IsCursorCaptureEnabled = false;
            }
            catch
            {
                // 较旧系统没有此属性；指针通常不位于编码像素区域，不阻止捕获。
            }

            _captureItem = item;
            _framePool = framePool;
            _captureSession = session;
            _captureSize = size;
            _lastFrameCopiedAt = 0;
            item.Closed += HandleCaptureItemClosed;
            framePool.FrameArrived += HandleFrameArrived;
            session.StartCapture();
            _lastError = "正在等待 WGC 首帧";

        }
        catch (Exception ex)
        {
            DisposeSessionLocked();
            _targetWindow = hwnd;
            _lastError = $"WGC 初始化失败: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void EnsureDeviceLocked()
    {
        if (_device is not null)
        {
            return;
        }

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0
        };
        var result = D3D11.D3D11CreateDevice(
            0,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out var device,
            out _,
            out var context);
        if (result.Failure)
        {
            result = D3D11.D3D11CreateDevice(
                0,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out device,
                out _,
                out context);
        }

        result.CheckError();
        _device = device;
        _deviceContext = context;
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(
            dxgiDevice.NativePointer,
            out var devicePointer));
        try
        {
            _winRtDevice = WinRT.MarshalInterface<WinRtDirect3DDevice>.FromAbi(devicePointer);
        }
        finally
        {
            WinRT.MarshalInterface<WinRtDirect3DDevice>.DisposeAbi(devicePointer);
        }
    }

    private void HandleFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(sender, _framePool) || _targetWindow == 0)
            {
                return;
            }

            Direct3D11CaptureFrame? frame = null;
            try
            {
                frame = sender.TryGetNextFrame();
                if (frame is null)
                {
                    return;
                }

                var now = Stopwatch.GetTimestamp();
                if (_lastFrameCopiedAt != 0
                    && Stopwatch.GetElapsedTime(_lastFrameCopiedAt, now) < _minimumFrameInterval)
                {
                    return;
                }

                var contentSize = frame.ContentSize;
                if (contentSize.Width <= 0 || contentSize.Height <= 0)
                {
                    _latestFrame = null;
                    _lastError = "WGC 收到无效尺寸的帧";
                    return;
                }

                var sizeChanged = contentSize.Width != _captureSize.Width
                    || contentSize.Height != _captureSize.Height;
                var captured = CopyClientTopStrip(frame.Surface, contentSize, _targetWindow, now);
                _latestFrame = captured;
                _lastFrameCopiedAt = now;
                _lastError = null;
                if (sizeChanged)
                {
                    frame.Dispose();
                    frame = null;
                    sender.Recreate(
                        _winRtDevice!,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        contentSize);
                    _captureSize = contentSize;
                }
            }
            catch (Exception ex)
            {
                _latestFrame = null;
                _lastError = $"WGC 读取帧失败: {ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                frame?.Dispose();
            }
        }
    }

    private CapturedTopStrip CopyClientTopStrip(
        WinRtDirect3DSurface surface,
        SizeInt32 contentSize,
        nint hwnd,
        long capturedAt)
    {
        if (!TryGetClientRegion(
                hwnd,
                contentSize,
                out var offsetX,
                out var offsetY,
                out var clientWidth,
                out var clientHeight))
        {
            throw new InvalidOperationException("无法计算目标窗口客户区在捕获帧中的位置");
        }

        var stripHeight = Math.Min(CaptureStripHeight, clientHeight);
        if (offsetX < 0
            || offsetY < 0
            || clientWidth <= 0
            || stripHeight <= 0
            || offsetX + clientWidth > contentSize.Width
            || offsetY + stripHeight > contentSize.Height)
        {
            throw new InvalidOperationException(
                $"客户区超出 WGC 帧范围: offset={offsetX},{offsetY}, client={clientWidth}×{clientHeight}, " +
                $"frame={contentSize.Width}×{contentSize.Height}");
        }

        EnsureStagingTexture(clientWidth, stripHeight);
        using var access = ((WinRT.IWinRTObject)surface).NativeObject.As(Direct3DDxgiInterfaceAccessId);
        var accessVtable = Marshal.ReadIntPtr(access.ThisPtr);
        var getInterfacePointer = Marshal.ReadIntPtr(accessVtable, 3 * IntPtr.Size);
        var getInterface = Marshal.GetDelegateForFunctionPointer<GetInterfaceDelegate>(getInterfacePointer);
        var textureId = typeof(ID3D11Texture2D).GUID;
        Marshal.ThrowExceptionForHR(getInterface(access.ThisPtr, ref textureId, out var texturePointer));
        using (var sourceTexture = new ID3D11Texture2D(texturePointer))
        {
            var sourceBox = new Box(
                offsetX,
                offsetY,
                0,
                offsetX + clientWidth,
                offsetY + stripHeight,
                1);
            _deviceContext!.CopySubresourceRegion(
                _stagingTexture!,
                0,
                0,
                0,
                0,
                sourceTexture,
                0,
                sourceBox);

            _deviceContext.Map(
                _stagingTexture!,
                0,
                MapMode.Read,
                Vortice.Direct3D11.MapFlags.None,
                out var mapped).CheckError();
            try
            {
                var pixels = new int[clientWidth * stripHeight];
                for (var y = 0; y < stripHeight; y++)
                {
                    Marshal.Copy(
                        IntPtr.Add(mapped.DataPointer, checked(y * (int)mapped.RowPitch)),
                        pixels,
                        y * clientWidth,
                        clientWidth);
                }

                return new CapturedTopStrip(hwnd, pixels, clientWidth, stripHeight, capturedAt);
            }
            finally
            {
                _deviceContext.Unmap(_stagingTexture!, 0);
            }
        }
    }

    private void EnsureStagingTexture(int width, int height)
    {
        if (_stagingTexture is not null && _stagingWidth == width && _stagingHeight == height)
        {
            return;
        }

        _stagingTexture?.Dispose();
        _stagingTexture = _device!.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)width,
            (uint)height,
            1,
            1,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read,
            1,
            0,
            ResourceOptionFlags.None));
        _stagingWidth = width;
        _stagingHeight = height;
    }

    private static bool TryGetClientRegion(
        nint hwnd,
        SizeInt32 contentSize,
        out int offsetX,
        out int offsetY,
        out int width,
        out int height)
    {
        offsetX = 0;
        offsetY = 0;
        width = 0;
        height = 0;
        if (!NativeMethods.GetClientRect(hwnd, out var clientRect))
        {
            return false;
        }

        var clientOrigin = new NativeMethods.Point(0, 0);
        if (!NativeMethods.ClientToScreen(hwnd, ref clientOrigin))
        {
            return false;
        }

        width = clientRect.Right - clientRect.Left;
        height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        // 某些无边框窗口的 WGC 内容就是纯客户区，此时无需套用窗口边框偏移。
        if (contentSize.Width == width && contentSize.Height == height)
        {
            return true;
        }

        var hasExtendedBounds = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaExtendedFrameBounds,
            out var extendedBounds,
            Marshal.SizeOf<NativeMethods.Rect>()) >= 0;
        var hasWindowBounds = NativeMethods.GetWindowRect(hwnd, out var windowBounds);
        if (!hasExtendedBounds && !hasWindowBounds)
        {
            return false;
        }

        var frameRect = hasExtendedBounds ? extendedBounds : windowBounds;
        if (hasWindowBounds
            && contentSize.Width == windowBounds.Right - windowBounds.Left
            && contentSize.Height == windowBounds.Bottom - windowBounds.Top)
        {
            frameRect = windowBounds;
        }
        else if (hasExtendedBounds
            && contentSize.Width == extendedBounds.Right - extendedBounds.Left
            && contentSize.Height == extendedBounds.Bottom - extendedBounds.Top)
        {
            frameRect = extendedBounds;
        }

        offsetX = clientOrigin.X - frameRect.Left;
        offsetY = clientOrigin.Y - frameRect.Top;
        return true;
    }

    private void HandleCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(sender, _captureItem))
            {
                return;
            }

            DisposeSessionLocked();
            _lastError = "WGC 目标窗口捕获已关闭";
        }
    }

    private void DisposeSessionLocked()
    {
        _latestFrame = null;
        _lastFrameCopiedAt = 0;
        _targetWindow = 0;

        if (_framePool is not null)
        {
            _framePool.FrameArrived -= HandleFrameArrived;
        }

        if (_captureItem is not null)
        {
            _captureItem.Closed -= HandleCaptureItemClosed;
        }

        _captureSession?.Dispose();
        _captureSession = null;
        _framePool?.Dispose();
        _framePool = null;
        _captureItem = null;
        _captureSize = default;
        _lastError = null;
    }

    private static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(
            GraphicsCaptureItemRuntimeClass,
            GraphicsCaptureItemRuntimeClass.Length,
            out var className));
        try
        {
            var interopId = GraphicsCaptureItemInteropId;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref interopId, out var factory));
            try
            {
                var vtable = Marshal.ReadIntPtr(factory);
                var createForWindowPointer = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowDelegate>(
                    createForWindowPointer);
                var itemId = GraphicsCaptureItemId;
                Marshal.ThrowExceptionForHR(createForWindow(factory, hwnd, ref itemId, out var itemPointer));
                try
                {
                    return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
                }
                finally
                {
                    WinRT.MarshalInterface<GraphicsCaptureItem>.DisposeAbi(itemPointer);
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            _ = WindowsDeleteString(className);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindowDelegate(
        nint @this,
        nint window,
        ref Guid iid,
        out nint result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInterfaceDelegate(nint @this, ref Guid iid, out nint result);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out nint @string);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint @string);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    private sealed record CapturedTopStrip(
        nint WindowHandle,
        int[] Pixels,
        int Width,
        int Height,
        long CapturedAt);
}
