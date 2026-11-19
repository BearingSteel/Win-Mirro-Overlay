using System.Windows;
using System.Windows.Media.Imaging;
using WinMirroOverlay.Capture;
using WinMirroOverlay.Common;
using WinMirroOverlay.Views;

namespace WinMirroOverlay;

public partial class App : Application
{
    private PickerWindow? _picker;
    private OverlayWindow? _overlay;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // NotifyIcon 的右键菜单需要视觉样式，否则是经典灰色外观。
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        Common.Loc.Apply(Services.AppSettings.Load().Language);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            MessageBox.Show(Common.Loc.NeedWin10,
                Common.Loc.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (!WindowCaptureService.IsSupported)
        {
            MessageBox.Show(Common.Loc.NoCaptureSupport,
                Common.Loc.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        OpenPicker();
    }

    /// <summary>打开“选择窗口”窗口；若已打开则激活它。</summary>
    internal void OpenPicker()
    {
        if (_exiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_picker is not null)
        {
            _picker.Activate();
            return;
        }

        var picker = new PickerWindow();
        _picker = picker;

        picker.WindowSelected += (_, info) =>
        {
            _picker = null;

            // 先建好悬浮窗，再关闭选择窗口，避免 Closed 回调误判为“用户取消”而退出。
            if (_overlay is null)
            {
                var overlay = new OverlayWindow(info);
                overlay.Closed += OnOverlayClosed;
                _overlay = overlay;
                overlay.Show();
            }
            else
            {
                _overlay.Retarget(info);
            }

            picker.Close();
        };

        picker.Closed += (_, _) =>
        {
            if (ReferenceEquals(_picker, picker))
            {
                _picker = null;
            }

            // 选择阶段就被关掉 = 用户取消；此时若没有悬浮窗则退出程序。
            if (_overlay is null && _picker is null)
            {
                Shutdown();
            }
        };

        picker.Show();
        picker.Activate();
    }

    internal void ReselectSource()
    {
        OpenPicker();
    }

    internal void ExitApp()
    {
        _exiting = true;
        Shutdown();
    }

    private void OnOverlayClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_overlay, sender))
        {
            _overlay = null;
        }

        if (_exiting)
        {
            return;
        }

        // 关闭窗口 2 后回到窗口 1；在 Closed 回调里同步开窗容易出问题，放到下一轮消息循环。
        Dispatcher.BeginInvoke(new Action(OpenPicker));
    }

    internal static BitmapImage LoadAppIconImage()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/assets/app.ico", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
