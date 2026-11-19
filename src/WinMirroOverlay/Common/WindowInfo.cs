using System.Windows.Media;

namespace WinMirroOverlay.Common;

/// <summary>一个可被镜像的顶层窗口。</summary>
public sealed class WindowInfo
{
    public WindowInfo(IntPtr handle, string title, string processName, uint processId, ImageSource? icon, bool onOtherDesktop = false)
    {
        Handle = handle;
        Title = title;
        ProcessName = processName;
        ProcessId = processId;
        Icon = icon;
        OnOtherDesktop = onOtherDesktop;
    }

    public IntPtr Handle { get; }

    public string Title { get; }

    public string ProcessName { get; }

    public uint ProcessId { get; }

    public ImageSource? Icon { get; }

    /// <summary>该窗口位于其它虚拟桌面（被 DWM shell cloak）。</summary>
    public bool OnOtherDesktop { get; }

    public string Subtitle
    {
        get
        {
            string head = string.IsNullOrEmpty(ProcessName) ? $"PID {ProcessId}" : $"{ProcessName} · PID {ProcessId}";
            return OnOtherDesktop ? head + " · " + Loc.OtherDesktopSuffix : head;
        }
    }
}
