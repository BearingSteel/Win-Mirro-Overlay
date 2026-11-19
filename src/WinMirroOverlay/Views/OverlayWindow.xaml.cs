using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinMirroOverlay.Capture;
using WinMirroOverlay.Common;
using WinMirroOverlay.Interop;
using WinMirroOverlay.Services;

namespace WinMirroOverlay.Views;

/// <summary>
/// 窗口 2：置顶 / 可缩放 / 可拖动 / 可选鼠标穿透的半透明镜像悬浮窗。
/// </summary>
public partial class OverlayWindow : Window
{
    private const int ResizeBorder = 6;
    private const double TitleBarHeight = 34;

    private static readonly Brush PinOnBrush = CreateBrush("#2DD4EF");
    private static readonly Brush PinOffBrush = CreateBrush("#C7D0DE");
    private static readonly Brush LiveBrush = CreateBrush("#48D597");
    private static readonly Brush IdleBrush = CreateBrush("#8E9AAE");

    private readonly TrayIconController _tray;
    private readonly DispatcherTimer _stateTimer;

    private WindowInfo _source;
    private IWindowCapture? _capture;
    private DispatcherTimer? _watchdog;
    private System.Threading.Timer? _pollTimer;
    private bool _fallbackActive;
    private WriteableBitmap? _bitmap;
    private HwndSource? _hwndSource;

    private double _zoom = 1.0;
    private double _panX;
    private double _panY;
    private bool _panning;
    private Point _panStartPoint;
    private Vector _panStartValue;
    private OverlayMode _mode = OverlayMode.Topmost;
    private bool _clickThrough;
    private bool _sourceAlive = true;
    private bool _live;

    public OverlayWindow(WindowInfo source)
    {
        InitializeComponent();

        _source = source;
        Icon = App.LoadAppIconImage();

        _tray = new TrayIconController();
        _tray.ModeSelected += SetMode;
        _tray.ZoomChanged += zoom => SetZoom(zoom);
        _tray.OpacityChanged += SetOpacity;
        _tray.FitRequested += FitToWindow;
        _tray.DiagnosticsToggled += OnDiagnosticsToggled;
        _tray.ReselectRequested += OnReselectRequested;
        _tray.LogFolderRequested += OnLogFolderRequested;
        _tray.ExitRequested += () => (Application.Current as App)?.ExitApp();

        _tray.SetMode(_mode);
        _tray.SetOpacity(1.0);
        _tray.SetZoom(1.0);

        _stateTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnStateTimer, Dispatcher);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.D0:
            case Key.NumPad0:
                SetZoom(1.0);
                e.Handled = true;
                return;

            case Key.OemPlus:
            case Key.Add:
                SetZoom(_zoom * 1.15);
                e.Handled = true;
                return;

            case Key.OemMinus:
            case Key.Subtract:
                SetZoom(_zoom / 1.15);
                e.Handled = true;
                return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateContentLayout();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);

        UpdatePinVisual();
        ConfigureInitialGeometry();
        StartCapture(_source);
        _stateTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _stateTimer.Stop();

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        StopCapture();
        _tray.Dispose();
    }

    public void Retarget(WindowInfo info)
    {
        StartCapture(info);
        ConfigureInitialGeometry();
        Activate();
    }

    // ---------------------------------------------------------------- 采集

    private void StartCapture(WindowInfo info)
    {
        StopCapture();

        _source = info;
        _sourceAlive = true;
        _live = false;
        _fallbackActive = false;
        _bitmap = null;
        _panX = 0;
        _panY = 0;
        FrameImage.Source = null;

        TitleText.Text = BaseTitle(info);
        _tray.SetTooltip($"Win-Mirro-Overlay · {info.Title}");
        SetLiveIndicator(false);
        ShowStatus("正在连接窗口…", "已启动 Windows Graphics Capture，等待首帧…");

        AppLog.Info($"开始镜像：{info.Title}（hwnd=0x{info.Handle.ToInt64():X}, pid={info.ProcessId}）");

        try
        {
            var capture = new WindowCaptureService();
            WireCapture(capture);
            _capture = capture;
            capture.Start(info.Handle);
            StartWatchdog();
        }
        catch (Exception ex)
        {
            AppLog.Error("WGC 启动失败，尝试兼容通道", ex);
            StartFallback(info, DesktopHint(info) + "Windows Graphics Capture 启动失败：" + ex.Message);
        }
    }

    private void WireCapture(IWindowCapture capture)
    {
        capture.FrameReady += frame => OnFrameReady(capture, frame);
        capture.CapturedSizeChanged += OnCapturedSizeChanged;
        capture.SourceClosed += OnSourceClosed;
        capture.Failed += OnCaptureFailed;
    }

    private void StopCapture()
    {
        StopWatchdog();
        StopPolling();

        if (_capture is null)
        {
            return;
        }

        // FrameReady 用 lambda 订阅，Dispose 时服务内部会解绑，这里只需解绑其余事件。
        _capture.CapturedSizeChanged -= OnCapturedSizeChanged;
        _capture.SourceClosed -= OnSourceClosed;
        _capture.Failed -= OnCaptureFailed;
        _capture.Dispose();
        _capture = null;
    }

    /// <summary>WGC 在部分机器/窗口上会"会话已启动但永远不来帧"，超时后切到兼容通道。</summary>
    private void StartWatchdog()
    {
        StopWatchdog();
        _watchdog = new DispatcherTimer(
            TimeSpan.FromMilliseconds(2500), DispatcherPriority.Background, OnWatchdog, Dispatcher);
        _watchdog.Start();
    }

    private void StopWatchdog()
    {
        _watchdog?.Stop();
        _watchdog = null;
    }

    private void OnWatchdog(object? sender, EventArgs e)
    {
        StopWatchdog();

        if (_live || !_sourceAlive)
        {
            return;
        }

        if (_fallbackActive)
        {
            AppLog.Error("兼容通道同样没有画面");
            ShowStatus("未能抓取到画面",
                "WGC 和 PrintWindow 都没有拿到画面。\n\n请把日志发给开发者：菜单 → 打开日志文件夹。" + Environment.NewLine + UnsupportedHint);
            return;
        }

        // 事件没触发不代表帧池里没有帧：先主动取一次，能取到就改成轮询。
        if (_capture is WindowCaptureService wgc && wgc.PollOnce())
        {
            AppLog.Warn("FrameArrived 事件未触发，但帧池可取到帧：改用轮询取帧");
            StartPolling(wgc);
            return;
        }

        AppLog.Warn("WGC 2.5 秒内没有交付任何帧，切换到 PrintWindow 兼容通道");
        StartFallback(_source, "Windows Graphics Capture 未收到画面，已切换为兼容模式。");
    }

    private void StartFallback(WindowInfo info, string? reason)
    {
        StopCapture();

        _source = info;
        _sourceAlive = true;
        _live = false;
        _fallbackActive = true;

        try
        {
            var capture = new PrintWindowCapture();
            WireCapture(capture);
            _capture = capture;
            capture.Start(info.Handle);

            TitleText.Text = BaseTitle(info) + " · 兼容模式";
            ShowStatus("正在抓取窗口…", reason ?? "已切换为 PrintWindow 兼容模式。");
            StartWatchdog();
        }
        catch (Exception ex)
        {
            AppLog.Error("PrintWindow 启动失败", ex);
            ShowStatus("无法开始镜像", DesktopHint(info) + ex.Message + "\n\n" + UnsupportedHint);
        }
    }

    private static string BaseTitle(WindowInfo info)
    {
        string title = string.IsNullOrWhiteSpace(info.Title) ? "镜像窗口" : info.Title;
        return info.OnOtherDesktop ? title + " · 其他桌面" : title;
    }

    private static string DesktopHint(WindowInfo info)
    {
        return info.OnOtherDesktop
            ? "该窗口位于其它虚拟桌面，系统通常不允许跨桌面抓取；请先切换到它所在的桌面再试。\n\n"
            : string.Empty;
    }

    private void StartPolling(WindowCaptureService capture)
    {
        StopPolling();
        _pollTimer = new System.Threading.Timer(_ => capture.PollOnce(), null, 0, 33);
    }

    private void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void OnLogFolderRequested()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", AppLog.Folder);
        }
        catch (Exception ex)
        {
            AppLog.Error("打开日志目录失败", ex);
        }
    }

    private void OnFrameReady(IWindowCapture capture, CapturedFrame frame)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            capture.Release(frame.Slot);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => RenderFrame(capture, frame)));
    }

    private void RenderFrame(IWindowCapture capture, CapturedFrame frame)
    {
        try
        {
            // 已经切换到别的源窗口（或已停止采集）时丢弃旧帧。
            if (!ReferenceEquals(capture, _capture))
            {
                return;
            }

            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                FrameImage.Source = _bitmap;
                UpdateContentLayout();
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Stride, 0);

            if (!_live)
            {
                _live = true;
                SetLiveIndicator(true);
                AppLog.Info($"首帧已上屏：{frame.Width}x{frame.Height}（{(_fallbackActive ? "PrintWindow" : "WGC")}）");
            }

            if (_sourceAlive && !NativeMethods.IsIconic(_source.Handle))
            {
                HideStatus();
            }
        }
        finally
        {
            capture.Release(frame.Slot);
        }
    }

    private void OnCapturedSizeChanged(int width, int height)
    {
        // 该事件可能来自采集线程，必须切回 UI 线程再动控件。
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(UpdateContentLayout));
    }

    private void OnSourceClosed()
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _sourceAlive = false;
            _live = false;
            SetLiveIndicator(false);
            ShowStatus("源窗口已关闭", "可从托盘菜单选择“重新选择源窗口”。");
        }));
    }

    private void OnCaptureFailed(string message)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _live = false;
            SetLiveIndicator(false);
            ShowStatus("采集出错", message + "\n\n可把日志发给开发者：托盘菜单 → 打开日志文件夹。" + Environment.NewLine + UnsupportedHint);
        }));
    }

    private void OnReselectRequested()
    {
        if (Application.Current is App app)
        {
            app.OpenPicker();
        }
    }

    private const string UnsupportedHint =
        "提示：受 DRM 保护的视频、部分独占全屏游戏无法被捕获；源窗口最小化时画面会暂停。若一直看不到画面，请在托盘菜单打开日志文件夹。";

    // ---------------------------------------------------------------- 状态检测

    private void OnStateTimer(object? sender, EventArgs e)
    {
        if (!_sourceAlive)
        {
            return;
        }

        if (!NativeMethods.IsWindow(_source.Handle))
        {
            _sourceAlive = false;
            _live = false;
            SetLiveIndicator(false);
            ShowStatus("源窗口已关闭", "可从托盘菜单选择“重新选择源窗口”。");
            return;
        }

        if (NativeMethods.IsIconic(_source.Handle))
        {
            ShowStatus("源窗口已最小化", "还原源窗口后画面会自动恢复。");
            _live = false;
            SetLiveIndicator(false);
        }
        else if (_live)
        {
            HideStatus();
        }
    }

    // ---------------------------------------------------------------- 交互

    // 钉子按钮 = 直接切到「置顶（点击穿透）」。
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        SetMode(_mode == OverlayMode.TopmostClickThrough ? OverlayMode.None : OverlayMode.TopmostClickThrough);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => TryDrag();

    // 画面内拖拽 = 平移画面（不是移动窗口）。
    private void ContentHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough || _bitmap is null)
        {
            return;
        }

        _panning = true;
        _panStartPoint = e.GetPosition(ContentHost);
        _panStartValue = new Vector(_panX, _panY);

        if (ContentHost.CaptureMouse())
        {
            e.Handled = true;
        }
    }

    private void ContentHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        Point current = e.GetPosition(ContentHost);
        Vector delta = current - _panStartPoint;
        _panX = _panStartValue.X + delta.X;
        _panY = _panStartValue.Y + delta.Y;
        UpdateContentLayout();
    }

    private void ContentHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndPan();

    private void ContentHost_LostMouseCapture(object sender, MouseEventArgs e) => EndPan();

    private void EndPan()
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;

        if (ContentHost.IsMouseCaptured)
        {
            ContentHost.ReleaseMouseCapture();
        }
    }

    private void ContentHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateContentLayout();

    private void ContentHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        double factor = Math.Pow(1.15, e.Delta / 120.0);
        SetZoom(_zoom * factor, e.GetPosition(ContentHost));
    }

    private void TryDrag()
    {
        if (_clickThrough)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标在 DragMove 之前已经抬起，忽略。
        }
    }

    private void SetMode(OverlayMode mode)
    {
        _mode = mode;

        Topmost = mode != OverlayMode.None;
        ApplyClickThrough(mode == OverlayMode.TopmostClickThrough);

        _tray.SetMode(mode);
        UpdatePinVisual();
    }

    private void UpdatePinVisual()
    {
        bool active = _mode != OverlayMode.None;
        PinButton.Foreground = active ? PinOnBrush : PinOffBrush;
        PinButton.ToolTip = _mode switch
        {
            OverlayMode.TopmostClickThrough => "置顶 + 点击穿透（用托盘菜单取消）",
            OverlayMode.Topmost => "置顶中，点击切换为点击穿透",
            _ => "点击置顶并开启穿透",
        };
    }

    private void ApplyClickThrough(bool enabled)
    {
        _clickThrough = enabled;

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        long exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        long newStyle = enabled
            ? exStyle | NativeMethods.WS_EX_TRANSPARENT
            : exStyle & ~NativeMethods.WS_EX_TRANSPARENT;

        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(newStyle));
    }

    /// <summary>把缩放调回「刚好放下 + 居中」的状态。</summary>
    private void FitToWindow()
    {
        if (_bitmap is null)
        {
            return;
        }

        double viewW = ContentHost.ActualWidth;
        double viewH = ContentHost.ActualHeight;
        if (viewW < 1 || viewH < 1)
        {
            return;
        }

        double dpi = Math.Max(0.1, VisualTreeHelper.GetDpi(this).DpiScaleX);
        double fit = Math.Min(
            1.0,
            Math.Min(viewW * dpi / _bitmap.PixelWidth, viewH * dpi / _bitmap.PixelHeight));

        _zoom = Math.Clamp(fit, 0.1, 10.0);
        _panX = 0;
        _panY = 0;
        UpdateContentLayout();
        _tray.SetZoom(_zoom);
    }

    private void SetZoom(double zoom, Point? anchor = null)
    {
        double target = Math.Clamp(zoom, 0.1, 10.0);
        if (Math.Abs(target - _zoom) < 0.0001)
        {
            return;
        }

        if (anchor is Point point)
        {
            // 以光标为锚点缩放：保持光标下的画面位置不变。
            double ratio = target / _zoom;
            double cx = ContentHost.ActualWidth / 2;
            double cy = ContentHost.ActualHeight / 2;
            _panX = point.X - cx - (point.X - cx - _panX) * ratio;
            _panY = point.Y - cy - (point.Y - cy - _panY) * ratio;
        }

        _zoom = target;
        UpdateContentLayout();
        _tray.SetZoom(_zoom);
    }

    private void SetOpacity(double opacity)
    {
        Opacity = Math.Clamp(opacity, 0.1, 1.0);
        _tray.SetOpacity(Opacity);
    }

    private void UpdateContentLayout()
    {
        UpdateZoomText();

        if (_bitmap is null || FrameImage.Source is null)
        {
            return;
        }

        double viewW = ContentHost.ActualWidth;
        double viewH = ContentHost.ActualHeight;

        // 个别情况下 Grid 的 ActualSize 可能还是 0（尚未完成布局），退回窗口尺寸。
        if (viewW < 1)
        {
            viewW = Math.Max(1, ActualWidth - 2);
        }

        if (viewH < 1)
        {
            viewH = Math.Max(1, ActualHeight - TitleBarHeight - 2);
        }

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0)
        {
            dpi = 1.0;
        }

        double width = _bitmap.PixelWidth * _zoom / dpi;
        double height = _bitmap.PixelHeight * _zoom / dpi;

        // 内容比视口小时居中；比视口大时把平移量钳制在"边缘对齐"范围内。
        double maxX = Math.Max(0, (width - viewW) / 2);
        double maxY = Math.Max(0, (height - viewH) / 2);
        _panX = Math.Clamp(_panX, -maxX, maxX);
        _panY = Math.Clamp(_panY, -maxY, maxY);

        double left = (viewW - width) / 2 + _panX;
        double top = (viewH - height) / 2 + _panY;

        // 再对最终位置做一次兜底钳制：大图不允许露出底色，小图强制居中。
        left = width >= viewW ? Math.Clamp(left, viewW - width, 0) : (viewW - width) / 2;
        top = height >= viewH ? Math.Clamp(top, viewH - height, 0) : (viewH - height) / 2;

        // 用布局位置（Margin）而不是 RenderTransform 定位：分层窗口在高倍率下
        // 对超大元素的变换渲染容易出空洞，布局定位没有这个问题。
        FrameImage.Width = width;
        FrameImage.Height = height;
        FrameImage.HorizontalAlignment = HorizontalAlignment.Left;
        FrameImage.VerticalAlignment = VerticalAlignment.Top;
        FrameImage.Margin = new Thickness(left, top, 0, 0);

        _panX = left - (viewW - width) / 2;
        _panY = top - (viewH - height) / 2;

        UpdateDiagnosticsText();
        LogLayout(viewW, viewH, dpi, width, height, maxX, maxY);
    }

    private DateTime _lastLayoutLog = DateTime.MinValue;
    private bool _showDiagnostics;

    private void OnDiagnosticsToggled(bool enabled)
    {
        _showDiagnostics = enabled;
        DiagBadge.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateDiagnosticsText();
    }

    private void UpdateDiagnosticsText()
    {
        if (!_showDiagnostics || _bitmap is null)
        {
            return;
        }

        DiagText.Text =
            $"bitmap {_bitmap.PixelWidth}x{_bitmap.PixelHeight}  view {ContentHost.ActualWidth:0}x{ContentHost.ActualHeight:0}\n" +
            $"zoom {_zoom * 100:0.#}%  pan {_panX:0.#},{_panY:0.#}  dpi {VisualTreeHelper.GetDpi(this).DpiScaleX:0.##}";
    }

    private void LogLayout(double viewW, double viewH, double dpi, double width, double height, double maxX, double maxY)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastLayoutLog).TotalSeconds < 1)
        {
            return;
        }

        _lastLayoutLog = now;
        AppLog.Info(
            $"布局：视口={viewW:0}x{viewH:0}dip dpi={dpi:0.##} 位图={_bitmap!.PixelWidth}x{_bitmap.PixelHeight} " +
            $"缩放={_zoom:0.###} 内容={width:0}x{height:0} 平移上限={maxX:0}x{maxY:0} 平移={_panX:0}x{_panY:0}");
    }

    private void UpdateZoomText()
    {
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
    }

    // ---------------------------------------------------------------- 布局

    private void ConfigureInitialGeometry()
    {
        if (!NativeMethods.GetWindowRect(_source.Handle, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0)
        {
            dpi = 1.0;
        }

        double sourceWidth = rect.Width / dpi;
        double sourceHeight = rect.Height / dpi;

        var work = WindowEnumerator.GetWorkAreaForWindow(_source.Handle);
        double workWidth = work.Width / dpi;
        double workHeight = work.Height / dpi;

        // 默认 1:1，只有在工作区放不下时才等比缩小（最低 10%）。
        double zoom = Math.Min(1.0, Math.Min(
            workWidth * 0.9 / sourceWidth,
            (workHeight - TitleBarHeight) * 0.9 / sourceHeight));
        zoom = Math.Clamp(zoom, 0.1, 1.0);

        _zoom = zoom;
        _panX = 0;
        _panY = 0;

        double width = Math.Max(MinWidth, sourceWidth * zoom);
        double height = Math.Max(MinHeight, sourceHeight * zoom + TitleBarHeight);

        double left = (rect.Left + rect.Width / 2.0) / dpi - width / 2.0;
        double top = (rect.Top + rect.Height / 2.0) / dpi - height / 2.0;

        left = Math.Clamp(left, work.Left / dpi, Math.Max(work.Left / dpi, work.Right / dpi - width));
        top = Math.Clamp(top, work.Top / dpi, Math.Max(work.Top / dpi, work.Bottom / dpi - height));

        Left = left;
        Top = top;
        Width = width;
        Height = height;

        _tray.SetZoom(zoom);
        UpdateContentLayout();
    }

    // ---------------------------------------------------------------- 无边框窗口命中测试

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_NCHITTEST || _clickThrough)
        {
            return IntPtr.Zero;
        }

        long packed = lParam.ToInt64();
        int screenX = (short)(packed & 0xFFFF);
        int screenY = (short)((packed >> 16) & 0xFFFF);

        Point local;
        try
        {
            local = PointFromScreen(new Point(screenX, screenY));
        }
        catch (InvalidOperationException)
        {
            return IntPtr.Zero;
        }

        bool left = local.X <= ResizeBorder;
        bool right = local.X >= ActualWidth - ResizeBorder;
        bool top = local.Y <= ResizeBorder;
        bool bottom = local.Y >= ActualHeight - ResizeBorder;

        int? hit = (left, right, top, bottom) switch
        {
            (true, _, true, _) => NativeMethods.HTTOPLEFT,
            (_, true, true, _) => NativeMethods.HTTOPRIGHT,
            (true, _, _, true) => NativeMethods.HTBOTTOMLEFT,
            (_, true, _, true) => NativeMethods.HTBOTTOMRIGHT,
            (true, _, _, _) => NativeMethods.HTLEFT,
            (_, true, _, _) => NativeMethods.HTRIGHT,
            (_, _, true, _) => NativeMethods.HTTOP,
            (_, _, _, true) => NativeMethods.HTBOTTOM,
            _ => null,
        };

        if (hit is int value)
        {
            handled = true;
            return new IntPtr(value);
        }

        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- 视觉状态

    private void SetLiveIndicator(bool live)
    {
        LiveDot.Fill = live ? LiveBrush : IdleBrush;
    }

    private void ShowStatus(string title, string detail)
    {
        StatusTitle.Text = title;
        StatusDetail.Text = detail;
        StatusOverlay.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        if (StatusOverlay.Visibility != Visibility.Collapsed)
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
