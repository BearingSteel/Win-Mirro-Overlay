using System.Windows.Threading;
using WinMirroOverlay.Common;
using WinMirroOverlay.Interop;

namespace WinMirroOverlay.Services;

/// <summary>
/// 让悬浮窗跟随虚拟桌面切换：发现窗口不在当前桌面时，把它移动到当前桌面。
/// 用的是公开的 IVirtualDesktopManager（MoveWindowToDesktop），不做 Undocumented 调用。
/// </summary>
internal sealed class VirtualDesktopKeeper : IDisposable
{
    private readonly VirtualDesktopApi.IVirtualDesktopManager? _manager;
    private readonly DispatcherTimer _timer;
    private IntPtr _hwnd;
    private bool _enabled;
    private DateTime _lastWarn = DateTime.MinValue;

    public VirtualDesktopKeeper()
    {
        _manager = VirtualDesktopApi.TryCreateManager();
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            UpdateTimer();
        }
    }

    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (_enabled && _manager is not null && _hwnd != IntPtr.Zero)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_enabled || _manager is null || _hwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hwnd))
        {
            return;
        }

        try
        {
            if (_manager.IsWindowOnCurrentVirtualDesktop(_hwnd, out int onCurrent) != 0 || onCurrent != 0)
            {
                return;
            }

            if (!TryGetCurrentDesktopId(out Guid current))
            {
                return;
            }

            int hr = _manager.MoveWindowToDesktop(_hwnd, ref current);
            if (hr != 0)
            {
                WarnOnce($"MoveWindowToDesktop 失败 hr=0x{hr:X8}");
            }
            else
            {
                AppLog.Info("虚拟桌面已切换，悬浮窗跟随到当前桌面");
            }
        }
        catch (Exception ex)
        {
            WarnOnce("虚拟桌面跟随异常：" + ex.Message);
        }
    }

    /// <summary>
    /// 取当前桌面 ID：优先用前台窗口，其次找任意一个当前桌面上可见、未被 cloak 的窗口。
    /// （没有任何窗口的空桌面无法用公开 API 判断，只能跳过。）
    /// </summary>
    private bool TryGetCurrentDesktopId(out Guid desktopId)
    {
        desktopId = Guid.Empty;

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != _hwnd
            && _manager!.GetWindowDesktopId(foreground, out Guid fromForeground) == 0
            && fromForeground != Guid.Empty)
        {
            desktopId = fromForeground;
            return true;
        }

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (hwnd == _hwnd || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetWindowTextLength(hwnd) == 0)
            {
                return true;
            }

            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0)
            {
                return true;
            }

            found = hwnd;
            return false;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        if (found != IntPtr.Zero
            && _manager!.GetWindowDesktopId(found, out Guid fromWindow) == 0
            && fromWindow != Guid.Empty)
        {
            desktopId = fromWindow;
            return true;
        }

        return false;
    }

    private void WarnOnce(string message)
    {
        if ((DateTime.UtcNow - _lastWarn).TotalSeconds < 30)
        {
            return;
        }

        _lastWarn = DateTime.UtcNow;
        AppLog.Warn(message);
    }

    public void Dispose()
    {
        _timer.Stop();
        _hwnd = IntPtr.Zero;
    }
}
