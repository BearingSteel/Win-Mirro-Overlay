using System.Runtime.InteropServices;

namespace WinMirroOverlay.Interop;

/// <summary>公开的虚拟桌面接口（Windows 10 1607+）。</summary>
internal static class VirtualDesktopApi
{
    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private class VirtualDesktopManagerClass
    {
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    public static IVirtualDesktopManager? TryCreateManager()
    {
        try
        {
            return (IVirtualDesktopManager)new VirtualDesktopManagerClass();
        }
        catch (Exception ex)
        {
            Common.AppLog.Warn("虚拟桌面接口不可用：" + ex.Message);
            return null;
        }
    }
}
