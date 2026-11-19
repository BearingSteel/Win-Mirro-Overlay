using System.Globalization;

namespace WinMirroOverlay.Common;

internal enum AppLanguage
{
    Auto = 0,
    English = 1,
    SimplifiedChinese = 2,
    TraditionalChinese = 3,
}

/// <summary>三语本地化（英语 / 简体中文 / 繁体中文）。</summary>
internal static class Loc
{
    private enum Culture
    {
        En,
        ZhCn,
        ZhTw,
    }

    private static Culture _current = Resolve(AppLanguage.Auto);

    public static AppLanguage Language { get; private set; } = AppLanguage.Auto;

    public static void SetLanguage(AppLanguage language)
    {
        Language = language;
        _current = Resolve(language);
    }

    public static void Apply(int language) => SetLanguage((AppLanguage)language);

    private static Culture Resolve(AppLanguage language)
    {
        switch (language)
        {
            case AppLanguage.English:
                return Culture.En;
            case AppLanguage.SimplifiedChinese:
                return Culture.ZhCn;
            case AppLanguage.TraditionalChinese:
                return Culture.ZhTw;
        }

        string name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            bool traditional = name.Contains("TW", StringComparison.OrdinalIgnoreCase)
                || name.Contains("HK", StringComparison.OrdinalIgnoreCase)
                || name.Contains("MO", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Hant", StringComparison.OrdinalIgnoreCase);
            return traditional ? Culture.ZhTw : Culture.ZhCn;
        }

        return Culture.En;
    }

    private static string T(string en, string zhCn, string zhTw) => _current switch
    {
        Culture.En => en,
        Culture.ZhTw => zhTw,
        _ => zhCn,
    };

    // ---- picker ----
    public static string PickerTitle => T(
        "Select a window to mirror",
        "选择需要镜像显示的窗口",
        "選擇需要鏡像顯示的視窗");

    public static string Refresh => T("Refresh", "刷新列表", "重新整理");

    public static string CountFormat => T(
        "{0} window(s) can be mirrored",
        "共 {0} 个可镜像窗口",
        "共 {0} 個可鏡像視窗");

    public static string OtherDesktopSuffix => T(
        "other desktop",
        "其他虚拟桌面",
        "其他虛擬桌面");

    // ---- overlay ----
    public static string MirrorWindow => T("Mirror", "镜像窗口", "鏡像視窗");

    public static string CompatSuffix => T(" · compatibility mode", " · 兼容模式", " · 相容模式");

    public static string OtherDesktopSuffixTitle => T(
        " · other desktop",
        " · 其他桌面",
        " · 其他桌面");

    public static string ConnectingTitle => T("Connecting…", "正在连接窗口…", "正在連線視窗…");

    public static string ConnectingDetail => T(
        "Windows Graphics Capture started, waiting for the first frame…",
        "已启动 Windows Graphics Capture，等待首帧…",
        "已啟動 Windows Graphics Capture，等待首幀…");

    public static string CaptureErrorTitle => T("Capture error", "采集出错", "擷取錯誤");

    public static string CannotStartTitle => T("Cannot start mirroring", "无法开始镜像", "無法開始鏡像");

    public static string CompatDetail => T(
        "Windows Graphics Capture delivered no frames; switched to compatibility mode.",
        "Windows Graphics Capture 未收到画面，已切换为兼容模式。",
        "Windows Graphics Capture 未收到畫面，已切換為相容模式。");

    public static string NoFrameTitle => T("No frames captured", "未能抓取到画面", "未能擷取到畫面");

    public static string NoFrameDetail => T(
        "Neither Windows Graphics Capture nor PrintWindow returned frames.",
        "WGC 和 PrintWindow 都没有拿到画面。",
        "WGC 和 PrintWindow 都沒有取得畫面。");

    public static string SourceClosedTitle => T("Source window closed", "源窗口已关闭", "來源視窗已關閉");

    public static string SourceClosedDetail => T(
        "Choose another window from the tray menu.",
        "可从托盘菜单选择「重新选择源窗口」。",
        "可從托盤選單選擇「重新選擇來源視窗」。");

    public static string MinimizedTitle => T("Source window minimized", "源窗口已最小化", "來源視窗已最小化");

    public static string MinimizedDetail => T(
        "Restore it and the picture will resume.",
        "还原源窗口后画面会自动恢复。",
        "還原來源視窗後畫面會自動恢復。");

    public static string UnsupportedHint => T(
        "Note: DRM-protected video and some exclusive-fullscreen games cannot be captured; the picture pauses while the source window is minimized.",
        "提示：受 DRM 保护的视频、部分独占全屏游戏无法被捕获；源窗口最小化时画面会暂停。",
        "提示：受 DRM 保護的影片、部分獨佔全螢幕遊戲無法被擷取；來源視窗最小化時畫面會暫停。");

    public static string OtherDesktopHint => T(
        "This window is on another virtual desktop; Windows usually does not allow capturing across desktops. Switch to that desktop first.",
        "该窗口位于其它虚拟桌面，系统通常不允许跨桌面抓取；请先切换到它所在的桌面再试。",
        "該視窗位於其他虛擬桌面，系統通常不允許跨桌面擷取；請先切換到它所在的桌面再試。");

    // ---- pin hint ----
    public static string PinToastTitle => T(
        "Pinned with click-through",
        "已置顶并开启点击穿透",
        "已置頂並開啟點擊穿透");

    public static string PinToastDetail => T(
        "Mouse clicks now pass through this window. To undo or exit: right-click the tray icon at the bottom-right corner.",
        "鼠标事件会穿过悬浮窗。取消穿透 / 退出：右键右下角的托盘图标。",
        "滑鼠事件會穿過懸浮視窗。取消穿透 / 結束：右鍵右下角的托盤圖示。");

    // ---- tray ----
    public static string TrayTopmost => T("Always on top", "置顶", "置頂");

    public static string TrayTopmostThrough => T(
        "Always on top (click-through)",
        "置顶（点击穿透）",
        "置頂（點擊穿透）");

    public static string TrayNoTopmost => T("Not always on top", "取消置顶", "取消置頂");

    public static string TrayAllDesktops => T("Show on all desktops", "所有桌面显示", "所有桌面顯示");

    public static string TrayZoomFormat => T("Zoom: {0}%", "缩放：{0}%", "縮放：{0}%");

    public static string TrayOpacityFormat => T("Opacity: {0}%", "透明度：{0}%", "透明度：{0}%");

    public static string TrayFit => T(
        "Fit to window (reset zoom and position)",
        "适应窗口（重置缩放与位置）",
        "適應視窗（重設縮放與位置）");

    public static string TrayReselect => T("Choose another window", "重新选择源窗口", "重新選擇來源視窗");

    public static string TrayExit => T("Exit", "退出", "結束");

    public static string TrayLanguage => T("Language", "语言", "語言");

    public static string LangAuto => T("System default", "跟随系统", "跟隨系統");

    // ---- pin tooltip ----
    public static string PinTipThrough => T(
        "Pinned + click-through (use the tray menu to undo)",
        "置顶 + 点击穿透（用托盘菜单取消）",
        "置頂 + 點擊穿透（用托盤選單取消）");

    public static string PinTipTopmost => T(
        "Pinned; click to switch to click-through",
        "置顶中，点击切换为点击穿透",
        "置頂中，點擊切換為點擊穿透");

    public static string PinTipNone => T(
        "Click to pin and enable click-through",
        "点击置顶并开启穿透",
        "點擊置頂並開啟穿透");

    // ---- app ----
    public static string NeedWin10 => T(
        "This app requires Windows 10 1809 (build 17763) or later.",
        "本程序需要 Windows 10 1809 (build 17763) 或更高版本。",
        "本程式需要 Windows 10 1809 (build 17763) 或更高版本。");

    public static string NoCaptureSupport => T(
        "Windows.Graphics.Capture is not available on this system.",
        "当前系统不支持 Windows.Graphics.Capture，无法采集窗口内容。",
        "目前系統不支援 Windows.Graphics.Capture，無法擷取視窗內容。");

    public static string AppTitle => "Win-Mirro-Overlay";
}
