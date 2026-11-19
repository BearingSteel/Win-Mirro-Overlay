using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinMirroOverlay.Common;

namespace WinMirroOverlay.Capture;

/// <summary>
/// 用 Windows.Graphics.Capture 实时抓取某个顶层窗口的内容，
/// 通过 D3D11 staging 纹理读到 CPU，交给 UI 线程绘制。
/// </summary>
internal sealed class WindowCaptureService : IWindowCapture
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33); // ~30fps

    private readonly object _sync = new();
    private readonly CapturedFrame[] _frames = new CapturedFrame[2];
    private readonly bool[] _busy = new bool[2];

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _staging;

    private int _width;
    private int _height;
    private int _nextSlot;
    private int _frameCount;
    private int _deliveredCount;
    private long _lastDeliveryTicks;
    private bool _loggedFirstFrame;
    private bool _loggedQiFailure;
    private bool _loggedNullFrame;
    private volatile bool _disposed;

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public event Action<CapturedFrame>? FrameReady;

    public event Action<int, int>? CapturedSizeChanged;

    public event Action<string>? Failed;

    public event Action? SourceClosed;

    public void Start(IntPtr hwnd)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("当前系统不支持 Windows.Graphics.Capture（需要 Windows 10 1809 及以上）。");
        }

        AppLog.Info($"WGC 启动：hwnd=0x{hwnd.ToInt64():X}");

        try
        {
            _device = D3D11.D3D11CreateDevice(
                DriverType.Hardware, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0 });
            AppLog.Info("WGC D3D11 设备已创建（硬件）");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"硬件设备创建失败，改用 WARP：{ex.Message}");
            _device = D3D11.D3D11CreateDevice(
                DriverType.Warp, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0 });
        }

        _context = _device.ImmediateContext;

        using (var dxgiDevice = _device.QueryInterface<IDXGIDevice>())
        {
            int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr inspectable);
            if (hr < 0)
            {
                throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice 失败：hr=0x{hr:X8}");
            }

            // FromAbi 接管 inspectable 的引用（CreateDirect3D11DeviceFromDXGIDevice 返回时引用计数为 1），
            // 不能再 Marshal.Release，否则会提前释放。
            _winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }

        _item = CaptureItemFactory.CreateForWindow(hwnd);
        _item.Closed += OnItemClosed;

        SizeInt32 size = _item.Size;
        AppLog.Info($"WGC 捕获项已创建：size={size.Width}x{size.Height} name={_item.DisplayName}");

        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new InvalidOperationException("目标窗口当前没有可捕获的尺寸。");
        }

        lock (_sync)
        {
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            _pool.FrameArrived += OnFrameArrived;

            ApplySizeLocked(size.Width, size.Height);
        }

        AppLog.Info("WGC 帧池已创建（自由线程，2 缓冲）");

        _session = _pool!.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;
        _session.StartCapture();

        AppLog.Info("WGC 会话已启动，等待首帧");
    }

    public void Release(int slot)
    {
        lock (_sync)
        {
            if (slot >= 0 && slot < _busy.Length)
            {
                _busy[slot] = false;
            }
        }
    }

    private void ApplySizeLocked(int width, int height)
    {
        if (width == _width && height == _height && _staging is not null)
        {
            return;
        }

        _width = width;
        _height = height;

        _staging?.Dispose();
        _staging = null;

        if (_device is null || width <= 0 || height <= 0)
        {
            return;
        }

        var description = new Texture2DDescription
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
            MiscFlags = ResourceOptionFlags.None,
        };

        _staging = _device.CreateTexture2D(description);

        int stride = width * 4;
        int total = stride * height;
        for (int i = 0; i < _frames.Length; i++)
        {
            var frame = _frames[i] ??= new CapturedFrame { Slot = i };
            if (frame.Pixels.Length != total)
            {
                frame.Pixels = new byte[total];
            }

            frame.Width = width;
            frame.Height = height;
            frame.Stride = stride;
        }

        AppLog.Info($"WGC staging 纹理：{width}x{height} stride={stride}");
        CapturedSizeChanged?.Invoke(width, height);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                HandleFrameLocked(sender);
            }
            catch (Exception ex)
            {
                AppLog.Error("WGC 取帧异常", ex);
                Failed?.Invoke(ex.Message);
            }
        }
    }

    /// <summary>
    /// 主动从帧池取一帧（用于 FrameArrived 事件在个别机器上不触发时的兜底轮询）。
    /// 返回 true 表示成功拷贝并交付了一帧。
    /// </summary>
    public bool PollOnce()
    {
        if (_disposed)
        {
            return false;
        }

        lock (_sync)
        {
            if (_disposed || _pool is null)
            {
                return false;
            }

            try
            {
                return HandleFrameLocked(_pool);
            }
            catch (Exception ex)
            {
                AppLog.Error("WGC 轮询取帧异常", ex);
                Failed?.Invoke(ex.Message);
                return false;
            }
        }
    }

    private bool HandleFrameLocked(Direct3D11CaptureFramePool sender)
    {
        _frameCount++;

        using Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            if (!_loggedNullFrame)
            {
                _loggedNullFrame = true;
                AppLog.Warn($"WGC FrameArrived 触发但 TryGetNextFrame 返回 null（第 {_frameCount} 次）");
            }

            return false;
        }

        SizeInt32 contentSize = frame.ContentSize;
        if (!_loggedFirstFrame)
        {
            _loggedFirstFrame = true;
            AppLog.Info($"WGC 首帧：content={contentSize.Width}x{contentSize.Height} staging={_width}x{_height}");
        }

        if (contentSize.Width <= 0 || contentSize.Height <= 0)
        {
            return false;
        }

        if (contentSize.Width != _width || contentSize.Height != _height)
        {
            AppLog.Info($"WGC 源窗口尺寸变化：{_width}x{_height} -> {contentSize.Width}x{contentSize.Height}");
            ApplySizeLocked(contentSize.Width, contentSize.Height);
            sender.Recreate(_winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, contentSize);
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        long minTicks = FrameInterval.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
        if (now - _lastDeliveryTicks < minTicks)
        {
            return false;
        }

        int slot = _nextSlot;
        if (_busy[slot])
        {
            return false; // UI 还没画完上一帧，丢弃这一帧
        }

        if (!CopyToBufferLocked(frame, _frames[slot]))
        {
            return false;
        }

        _busy[slot] = true;
        _lastDeliveryTicks = now;
        _nextSlot ^= 1;

        if (_deliveredCount == 0)
        {
            AppLog.Info("WGC 首帧已拷到 CPU，交给 UI");
        }

        _deliveredCount++;
        FrameReady?.Invoke(_frames[slot]);
        return true;
    }

    private bool CopyToBufferLocked(Direct3D11CaptureFrame frame, CapturedFrame target)
    {
        if (_context is null || _staging is null)
        {
            return false;
        }

        var surface = frame.Surface;
        if (surface is not WinRT.IWinRTObject winrtObject)
        {
            if (!_loggedQiFailure)
            {
                _loggedQiFailure = true;
                AppLog.Warn($"WGC 表面对象类型异常：{surface?.GetType().FullName ?? "null"}");
            }

            return false;
        }

        // QI 到 ID3D11Texture2D，得到 +1 引用；后续由 Vortice 包装对象接管并释放。
        int hr = winrtObject.NativeObject.TryAs(typeof(ID3D11Texture2D).GUID, out IntPtr texturePointer);
        if (hr < 0 || texturePointer == IntPtr.Zero)
        {
            if (!_loggedQiFailure)
            {
                _loggedQiFailure = true;
                AppLog.Warn($"WGC QI ID3D11Texture2D 失败：hr=0x{hr:X8} ptr=0x{texturePointer.ToInt64():X}");
            }

            return false;
        }

        using var texture = new ID3D11Texture2D(texturePointer);

        _context.CopyResource(_staging, texture);

        MappedSubresource mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowPitch = (int)mapped.RowPitch;
            for (int y = 0; y < target.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * rowPitch), target.Pixels, y * target.Stride, target.Stride);
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }

        return true;
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        AppLog.Info("WGC 源窗口已关闭（Item.Closed）");
        SourceClosed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _session?.Dispose();
            }
            catch
            {
                // 会话可能已被源窗口销毁，忽略。
            }

            try
            {
                if (_pool is not null)
                {
                    _pool.FrameArrived -= OnFrameArrived;
                    _pool.Dispose();
                }

                if (_item is not null)
                {
                    _item.Closed -= OnItemClosed;
                }
            }
            catch
            {
                // 同上。
            }

            _staging?.Dispose();
            _staging = null;
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;

            AppLog.Info($"WGC 已停止：收到帧回调 {_frameCount} 次，交付 UI {_deliveredCount} 帧");
        }
    }

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
