namespace WinMirroOverlay.Capture;

/// <summary>一帧 BGRA32 像素数据；槽位由采集服务复用，渲染完成后必须调用 Release。</summary>
internal sealed class CapturedFrame
{
    public byte[] Pixels = Array.Empty<byte>();

    public int Width;

    public int Height;

    public int Stride;

    public int Slot;
}
