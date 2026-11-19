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
    private WindowCaptureService? _capture;
    private WriteableBitmap? _bitmap;
    private HwndSource? _hwndSource;

    private double _zoom = 1.0;
    private bool _topmost = true;
    private bool _clickThrough;
    private bool _sourceAlive = true;
    private bool _live;

    public OverlayWindow(WindowInfo source)
    {
        InitializeComponent();

        _source = source;
        Icon = App.LoadAppIconImage();

        _tray = new TrayIconController();
        _tray.TopmostToggled += SetTopmost;
        _tray.ClickThroughToggled += SetClickThrough;
        _tray.ZoomSelected += percent => SetZoom(percent / 100.0);
        _tray.OpacitySelected += percent => SetOpacity(percent / 100.0);
        _tray.ReselectRequested += OnReselectRequested;
        _tray.ExitRequested += () => Application.Current?.Shutdown();

        _tray.SetTopmost(_topmost);
        _tray.SetOpacity(100);
        _tray.SetZoom(100);

        _stateTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnStateTimer, Dispatcher);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyZoomSize();
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
        _bitmap = null;
        FrameImage.Source = null;

        TitleText.Text = string.IsNullOrWhiteSpace(info.Title) ? "镜像窗口" : info.Title;
        _tray.SetTooltip($"Win-Mirro-Overlay · {info.Title}");
        SetLiveIndicator(false);
        ShowStatus("正在连接窗口…", info.Title);

        try
        {
            var capture = new WindowCaptureService();
            capture.FrameReady += frame => OnFrameReady(capture, frame);
            capture.CapturedSizeChanged += OnCapturedSizeChanged;
            capture.SourceClosed += OnSourceClosed;
            capture.Failed += OnCaptureFailed;
            _capture = capture;

            capture.Start(info.Handle);
        }
        catch (Exception ex)
        {
            StopCapture();
            ShowStatus("无法开始镜像", ex.Message + "\n\n" + UnsupportedHint);
        }
    }

    private void StopCapture()
    {
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

    private void OnFrameReady(WindowCaptureService capture, CapturedFrame frame)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            capture.Release(frame.Slot);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => RenderFrame(capture, frame)));
    }

    private void RenderFrame(WindowCaptureService capture, CapturedFrame frame)
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
                ApplyZoomSize();
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Stride, 0);

            if (!_live)
            {
                _live = true;
                SetLiveIndicator(true);
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

        Dispatcher.BeginInvoke(new Action(ApplyZoomSize));
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
            ShowStatus("采集出错", message + "\n\n" + UnsupportedHint);
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
        "提示：受 DRM 保护的视频、部分独占全屏游戏无法被捕获；源窗口最小化时画面会暂停。";

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

    private void Pin_Click(object sender, RoutedEventArgs e) => SetTopmost(!_topmost);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => TryDrag();

    private void ContentHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => TryDrag();

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

    private void SetTopmost(bool topmost)
    {
        _topmost = topmost;
        Topmost = topmost;
        _tray.SetTopmost(topmost);
        UpdatePinVisual();
    }

    private void UpdatePinVisual()
    {
        PinButton.Foreground = _topmost ? PinOnBrush : PinOffBrush;
        PinButton.ToolTip = _topmost ? "取消置顶" : "置顶显示";
    }

    private void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        _tray.SetClickThrough(enabled);

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

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.1, 4.0);
        ApplyZoomSize();
        _tray.SetZoom((int)Math.Round(_zoom * 100));
    }

    private void SetOpacity(double opacity)
    {
        Opacity = Math.Clamp(opacity, 0.05, 1.0);
        _tray.SetOpacity((int)Math.Round(Opacity * 100));
    }

    private void ApplyZoomSize()
    {
        UpdateZoomText();

        if (_bitmap is null || FrameImage.Source is null)
        {
            return;
        }

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0)
        {
            dpi = 1.0;
        }

        FrameImage.Width = _bitmap.PixelWidth * _zoom / dpi;
        FrameImage.Height = _bitmap.PixelHeight * _zoom / dpi;
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

        // 默认 1:1（最多 100%），只有放不下时才等比缩小。
        // 这里用显式倒序循环，避免 array.Reverse() 在部分 SDK 上绑定到返回 void 的 Span 扩展方法。
        double zoom = 0;
        int[] presets = TrayIconController.ZoomPresets;
        for (int i = presets.Length - 1; i >= 0; i--)
        {
            int percent = presets[i];
            if (percent > 100)
            {
                continue;
            }

            double candidate = percent / 100.0;
            if (sourceWidth * candidate <= workWidth * 0.9
                && sourceHeight * candidate + TitleBarHeight <= workHeight * 0.9)
            {
                zoom = candidate;
                break;
            }
        }

        if (zoom <= 0)
        {
            zoom = Math.Min(workWidth * 0.9 / sourceWidth, (workHeight - TitleBarHeight) * 0.9 / sourceHeight);
            zoom = Math.Clamp(zoom, 0.15, 1.0);
        }

        _zoom = zoom;

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

        _tray.SetZoom((int)Math.Round(zoom * 100));
        ApplyZoomSize();
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
