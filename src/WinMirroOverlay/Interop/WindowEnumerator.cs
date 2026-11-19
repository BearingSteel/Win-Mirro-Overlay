using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinMirroOverlay.Common;

namespace WinMirroOverlay.Interop;

/// <summary>枚举当前桌面上“适合被镜像”的顶层窗口。</summary>
internal static class WindowEnumerator
{
    private static readonly string[] SkippedClassNames =
    {
        "Progman",              // 桌面
        "WorkerW",              // 桌面（壁纸层）
        "Shell_TrayWnd",        // 任务栏
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
    };

    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<WindowInfo> Enumerate()
    {
        var result = new List<WindowInfo>();
        uint selfPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsCandidate(hwnd, selfPid))
            {
                return true;
            }

            string title = GetTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName;
            string? exePath = null;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
                exePath = process.MainModule?.FileName;
            }
            catch
            {
                processName = string.Empty;
            }

            result.Add(new WindowInfo(hwnd, title, processName, pid, exePath is null ? null : GetIcon(exePath)));
            return true;
        }, IntPtr.Zero);

        return result
            .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCandidate(IntPtr hwnd, uint selfPid)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        if (NativeMethods.GetWindowTextLength(hwnd) == 0)
        {
            return false;
        }

        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        // 被 DWM 隐藏（cloaked）的窗口不可见，例如挂起的 UWP 应用。
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
        {
            return false;
        }

        var className = GetClassName(hwnd);
        if (SkippedClassNames.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == selfPid)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        return true;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static ImageSource? GetIcon(string exePath)
    {
        if (IconCache.TryGetValue(exePath, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is not null)
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                image = source;
            }
        }
        catch
        {
            // 受保护/权限不足的进程取不到图标是正常的。
        }

        IconCache[exePath] = image;
        return image;
    }

    /// <summary>返回窗口所在显示器的工作区（物理像素）。</summary>
    public static NativeMethods.RECT GetWorkAreaForWindow(IntPtr hwnd)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return info.rcWork;
            }
        }

        return new NativeMethods.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)SystemParameters.PrimaryScreenWidth,
            Bottom = (int)SystemParameters.PrimaryScreenHeight,
        };
    }
}
