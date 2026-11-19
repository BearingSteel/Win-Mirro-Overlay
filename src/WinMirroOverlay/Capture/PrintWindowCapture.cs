using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WinMirroOverlay.Common;
using WinMirroOverlay.Interop;

namespace WinMirroOverlay.Capture;

/// <summary>
/// 兼容通道：用 PrintWindow(PW_RENDERFULLCONTENT) 定时把窗口画到 DIB 上再读回 CPU。
/// 当 Windows.Graphics.Capture 在被测机器上取不到帧时自动启用。
/// </summary>
internal sealed class PrintWindowCapture : IWindowCapture
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    private readonly object _sync = new();
    private readonly CapturedFrame[] _frames = new CapturedFrame[2];
    private readonly bool[] _busy = new bool[2];

    private System.Threading.Timer? _timer;
    private IntPtr _hwnd;
    private Bitmap? _bitmap;
    private Graphics? _graphics;
    private int _width;
    private int _height;
    private int _nextSlot;
    private int _failures;
    private int _frameCount;
    private int _closed;
    private bool _failLogged;
    private bool _disposed;

    public event Action<CapturedFrame>? FrameReady;

    public event Action<int, int>? CapturedSizeChanged;

    public event Action<string>? Failed;

    public event Action? SourceClosed;

    public void Start(IntPtr hwnd)
    {
        _hwnd = hwnd;
        AppLog.Info($"PrintWindow 兼容通道启动：hwnd=0x{hwnd.ToInt64():X}");
        _timer = new System.Threading.Timer(OnTick, null, 0, 33);
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

    private void OnTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        // 上一次抓取还没结束就跳过，避免回调堆积。
        if (!Monitor.TryEnter(_sync))
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                return;
            }

            CaptureOnce();
        }
        catch (Exception ex)
        {
            AppLog.Error("PrintWindow 抓取异常", ex);
            FailOnce(ex.Message);
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    private void CaptureOnce()
    {
        if (!NativeMethods.IsWindow(_hwnd))
        {
            RaiseClosed();
            return;
        }

        if (NativeMethods.IsIconic(_hwnd))
        {
            return;
        }

        var bounds = WindowEnumerator.GetVisibleBounds(_hwnd);
        int width = bounds.Width;
        int height = bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_bitmap is null || width != _width || height != _height)
        {
            EnsureBitmap(width, height);
        }

        int slot = _nextSlot;
        if (_busy[slot])
        {
            return;
        }

        IntPtr hdc = _graphics!.GetHdc();
        bool ok;
        try
        {
            ok = NativeMethods.PrintWindow(_hwnd, hdc, PW_RENDERFULLCONTENT);
        }
        finally
        {
            _graphics.ReleaseHdc(hdc);
        }

        if (!ok)
        {
            _failures++;
            if (_failures >= 20)
            {
                FailOnce("PrintWindow 连续失败，无法抓取该窗口（可能受系统保护）。");
            }

            return;
        }

        _failures = 0;

        var data = _bitmap!.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var frame = _frames[slot];
            for (int y = 0; y < height; y++)
            {
                IntPtr source = data.Stride >= 0
                    ? IntPtr.Add(data.Scan0, y * data.Stride)
                    : IntPtr.Add(data.Scan0, (height - 1 - y) * -data.Stride);

                Marshal.Copy(source, frame.Pixels, y * frame.Stride, frame.Stride);
            }
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }

        _busy[slot] = true;
        _nextSlot ^= 1;

        if (_frameCount++ == 0)
        {
            AppLog.Info($"PrintWindow 首帧：{width}x{height}");
        }

        FrameReady?.Invoke(_frames[slot]);
    }

    private void EnsureBitmap(int width, int height)
    {
        _graphics?.Dispose();
        _bitmap?.Dispose();

        _bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _graphics = Graphics.FromImage(_bitmap);
        _width = width;
        _height = height;

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

        AppLog.Info($"PrintWindow 缓冲区：{width}x{height}");
        CapturedSizeChanged?.Invoke(width, height);
    }

    private void RaiseClosed()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        AppLog.Info("PrintWindow 源窗口已消失");
        _timer?.Dispose();
        _timer = null;
        SourceClosed?.Invoke();
    }

    private void FailOnce(string message)
    {
        if (_failLogged)
        {
            return;
        }

        _failLogged = true;
        AppLog.Warn("PrintWindow 失败：" + message);
        Failed?.Invoke(message);
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
            _timer?.Dispose();
            _timer = null;
            _graphics?.Dispose();
            _graphics = null;
            _bitmap?.Dispose();
            _bitmap = null;

            AppLog.Info($"PrintWindow 已停止：抓取 {_frameCount} 帧");
        }
    }
}
