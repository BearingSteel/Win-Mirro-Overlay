using System.Windows.Media;

namespace WinMirroOverlay.Common;

/// <summary>一个可被镜像的顶层窗口。</summary>
public sealed class WindowInfo
{
    public WindowInfo(IntPtr handle, string title, string processName, uint processId, ImageSource? icon)
    {
        Handle = handle;
        Title = title;
        ProcessName = processName;
        ProcessId = processId;
        Icon = icon;
    }

    public IntPtr Handle { get; }

    public string Title { get; }

    public string ProcessName { get; }

    public uint ProcessId { get; }

    public ImageSource? Icon { get; }

    public string Subtitle => string.IsNullOrEmpty(ProcessName) ? $"PID {ProcessId}" : $"{ProcessName} · PID {ProcessId}";
}
