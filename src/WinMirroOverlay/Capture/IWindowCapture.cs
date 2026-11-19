namespace WinMirroOverlay.Capture;

/// <summary>窗口采集器的统一接口：WGC 主通道与 PrintWindow 兼容通道都实现它。</summary>
internal interface IWindowCapture : IDisposable
{
    event Action<CapturedFrame>? FrameReady;

    event Action<int, int>? CapturedSizeChanged;

    event Action<string>? Failed;

    event Action? SourceClosed;

    void Start(IntPtr hwnd);

    /// <summary>UI 线程绘制完一帧后归还槽位。</summary>
    void Release(int slot);
}
