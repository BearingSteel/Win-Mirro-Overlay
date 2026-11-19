using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace WinMirroOverlay.Capture;

/// <summary>
/// 通过 RoGetActivationFactory 取到 GraphicsCaptureItem 的激活工厂，
/// 再 QI 出 IGraphicsCaptureItemInterop 来按 HWND 创建捕获项。
/// </summary>
internal static class CaptureItemFactory
{
    private const string GraphicsCaptureItemClass = "Windows.Graphics.Capture.GraphicsCaptureItem";

    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        int hr = WindowsCreateString(GraphicsCaptureItemClass, GraphicsCaptureItemClass.Length, out IntPtr hstring);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            Guid interopIid = IID_IGraphicsCaptureItemInterop;
            hr = RoGetActivationFactory(hstring, ref interopIid, out IntPtr factory);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
                Guid itemIid = IID_IGraphicsCaptureItem;
                IntPtr itemPtr = interop.CreateForWindow(hwnd, ref itemIid);
                if (itemPtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("CreateForWindow 返回空指针，无法捕获该窗口。");
                }

                // CsWinRT 的 FromAbi 会接管（adopt）传入的引用，不能再 Marshal.Release，
                // 否则引用计数归零，后续使用捕获项会崩溃。
                return GraphicsCaptureItem.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }
}
