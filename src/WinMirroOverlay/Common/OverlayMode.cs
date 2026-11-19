namespace WinMirroOverlay.Common;

/// <summary>悬浮窗的三种状态：普通 / 置顶 / 置顶且鼠标穿透。</summary>
internal enum OverlayMode
{
    None,
    Topmost,
    TopmostClickThrough,
}
