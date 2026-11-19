using System.Drawing;
using System.Windows.Forms;
using WinMirroOverlay.Common;

namespace WinMirroOverlay.Services;

/// <summary>托盘图标与右键菜单（左键点击也会弹出同一菜单）。</summary>
internal sealed class TrayIconController : IDisposable
{
    private const double MinZoom = 0.10;
    private const double MaxZoom = 10.0;
    private const int MinOpacityPercent = 10;

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _iconImage;
    private readonly ContextMenuStrip _menu;

    private readonly ToolStripMenuItem _modeTopmost;
    private readonly ToolStripMenuItem _modeTopmostThrough;
    private readonly ToolStripMenuItem _modeNone;
    private readonly ToolStripMenuItem _allDesktopsItem;

    private readonly ToolStripMenuItem _zoomHeaderItem;
    private readonly TrackBar _zoomTrack;

    private readonly ToolStripMenuItem _opacityHeaderItem;
    private readonly TrackBar _opacityTrack;

    private bool _updatingZoom;
    private bool _updatingOpacity;

    public TrayIconController()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/app.ico"));
        _iconImage = new Icon(resource!.Stream);

        // ShowImageMargin 必须为 true，勾选标记才会显示出来。
        _menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ShowImageMargin = true,
        };

        _modeTopmost = new ToolStripMenuItem("置顶");
        _modeTopmost.Click += (_, _) => ModeSelected?.Invoke(OverlayMode.Topmost);

        _modeTopmostThrough = new ToolStripMenuItem("置顶（点击穿透）");
        _modeTopmostThrough.Click += (_, _) => ModeSelected?.Invoke(OverlayMode.TopmostClickThrough);

        _modeNone = new ToolStripMenuItem("取消置顶");
        _modeNone.Click += (_, _) => ModeSelected?.Invoke(OverlayMode.None);

        _allDesktopsItem = new ToolStripMenuItem("所有桌面显示") { Checked = true };
        _allDesktopsItem.Click += (_, _) => AllDesktopsToggled?.Invoke(!_allDesktopsItem.Checked);

        _zoomHeaderItem = new ToolStripMenuItem("缩放：100%");
        var zoomHost = CreateSliderHost(0, 1000, ZoomToTrack(1.0), out _zoomTrack, out var zoomPanel);
        _zoomTrack.ValueChanged += (_, _) =>
        {
            if (_updatingZoom)
            {
                return;
            }

            double zoom = TrackToZoom(_zoomTrack.Value);
            _zoomHeaderItem.Text = $"缩放：{Math.Round(zoom * 100)}%";
            ZoomChanged?.Invoke(zoom);
        };
        zoomPanel.Controls.Add(_zoomTrack);

        _opacityHeaderItem = new ToolStripMenuItem("透明度：100%");
        var opacityHost = CreateSliderHost(MinOpacityPercent, 100, 100, out _opacityTrack, out var opacityPanel);
        _opacityTrack.ValueChanged += (_, _) =>
        {
            if (_updatingOpacity)
            {
                return;
            }

            _opacityHeaderItem.Text = $"透明度：{_opacityTrack.Value}%";
            OpacityChanged?.Invoke(_opacityTrack.Value / 100.0);
        };
        opacityPanel.Controls.Add(_opacityTrack);

        var fitItem = new ToolStripMenuItem("适应窗口（重置缩放与位置）");
        fitItem.Click += (_, _) => FitRequested?.Invoke();

        var diagItem = new ToolStripMenuItem("显示诊断信息");
        diagItem.Click += (_, _) =>
        {
            diagItem.Checked = !diagItem.Checked;
            DiagnosticsToggled?.Invoke(diagItem.Checked);
        };

        var reselectItem = new ToolStripMenuItem("重新选择源窗口");
        reselectItem.Click += (_, _) => ReselectRequested?.Invoke();

        var logItem = new ToolStripMenuItem("打开日志文件夹");
        logItem.Click += (_, _) => LogFolderRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _modeTopmost,
            _modeTopmostThrough,
            _modeNone,
            new ToolStripSeparator(),
            _allDesktopsItem,
            new ToolStripSeparator(),
            _zoomHeaderItem,
            zoomHost,
            _opacityHeaderItem,
            opacityHost,
            fitItem,
            new ToolStripSeparator(),
            reselectItem,
            logItem,
            diagItem,
            exitItem,
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconImage,
            Text = "Win-Mirro-Overlay",
            ContextMenuStrip = _menu,
            Visible = true,
        };

        // 左键也弹出菜单（需求：左键或右键图标弹出菜单）。
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _menu.Show(Cursor.Position);
            }
        };
    }

    public event Action<OverlayMode>? ModeSelected;

    public event Action<bool>? AllDesktopsToggled;

    /// <summary>缩放比例（0.1 - 10.0）。</summary>
    public event Action<double>? ZoomChanged;

    /// <summary>不透明度（0.1 - 1.0）。</summary>
    public event Action<double>? OpacityChanged;

    public event Action? FitRequested;

    public event Action<bool>? DiagnosticsToggled;

    public event Action? ReselectRequested;

    public event Action? LogFolderRequested;

    public event Action? ExitRequested;

    public void SetMode(OverlayMode mode)
    {
        _modeTopmost.Checked = mode == OverlayMode.Topmost;
        _modeTopmostThrough.Checked = mode == OverlayMode.TopmostClickThrough;
        _modeNone.Checked = mode == OverlayMode.None;
    }

    public void SetAllDesktops(bool enabled) => _allDesktopsItem.Checked = enabled;

    public void SetZoom(double zoom)
    {
        _updatingZoom = true;
        _zoomTrack.Value = Math.Clamp(ZoomToTrack(zoom), _zoomTrack.Minimum, _zoomTrack.Maximum);
        _updatingZoom = false;

        _zoomHeaderItem.Text = $"缩放：{Math.Round(zoom * 100)}%";
    }

    public void SetOpacity(double opacity)
    {
        int percent = Math.Clamp((int)Math.Round(opacity * 100), _opacityTrack.Minimum, _opacityTrack.Maximum);

        _updatingOpacity = true;
        _opacityTrack.Value = percent;
        _updatingOpacity = false;

        _opacityHeaderItem.Text = $"透明度：{percent}%";
    }

    public void SetTooltip(string text)
    {
        _notifyIcon.Text = text.Length > 63 ? text[..60] + "..." : text;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _iconImage.Dispose();
    }

    private static ToolStripControlHost CreateSliderHost(
        int minimum, int maximum, int value, out TrackBar track, out Panel panel)
    {
        panel = new Panel
        {
            Width = 250,
            Height = 34,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        track = new TrackBar
        {
            Minimum = minimum,
            Maximum = maximum,
            SmallChange = Math.Max(1, (maximum - minimum) / 100),
            LargeChange = Math.Max(5, (maximum - minimum) / 10),
            TickStyle = TickStyle.None,
            Dock = DockStyle.Fill,
            Value = value,
        };

        return new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Width = 250,
            Height = 34,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
    }

    private static double TrackToZoom(int value)
    {
        double ratio = MaxZoom / MinZoom;
        return MinZoom * Math.Pow(ratio, value / 1000.0);
    }

    private static int ZoomToTrack(double zoom)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        double ratio = MaxZoom / MinZoom;
        return (int)Math.Round(1000 * Math.Log(zoom / MinZoom) / Math.Log(ratio));
    }
}
