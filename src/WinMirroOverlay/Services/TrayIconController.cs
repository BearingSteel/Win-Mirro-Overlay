using System.Drawing;
using System.Windows.Forms;

namespace WinMirroOverlay.Services;

/// <summary>托盘图标与右键菜单（左键点击也会弹出同一菜单）。</summary>
internal sealed class TrayIconController : IDisposable
{
    public static readonly int[] OpacityPresets = { 100, 90, 75, 50, 25 };

    private const double MinZoom = 0.10;
    private const double MaxZoom = 10.0;

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _iconImage;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _topmostItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _zoomHeaderItem;
    private readonly ToolStripMenuItem _opacityItem;
    private readonly TrackBar _zoomTrack;
    private readonly Dictionary<int, ToolStripMenuItem> _opacityItems = new();
    private bool _updatingZoom;

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

        _topmostItem = new ToolStripMenuItem("置顶显示：关");
        _topmostItem.Click += (_, _) => TopmostToggled?.Invoke(!_topmostItem.Checked);

        _clickThroughItem = new ToolStripMenuItem("点击穿透：关");
        _clickThroughItem.Click += (_, _) => ClickThroughToggled?.Invoke(!_clickThroughItem.Checked);

        _zoomHeaderItem = new ToolStripMenuItem("缩放：100%");

        var zoomPanel = new Panel
        {
            Width = 250,
            Height = 34,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        _zoomTrack = new TrackBar
        {
            Minimum = 0,
            Maximum = 1000,
            SmallChange = 10,
            LargeChange = 100,
            TickStyle = TickStyle.None,
            Dock = DockStyle.Fill,
            Value = ZoomToTrack(1.0),
        };

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

        var zoomHost = new ToolStripControlHost(zoomPanel)
        {
            AutoSize = false,
            Width = 250,
            Height = 34,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        _opacityItem = new ToolStripMenuItem("透明度：100%");
        foreach (int percent in OpacityPresets)
        {
            var item = new ToolStripMenuItem($"{percent}%") { Tag = percent };
            item.Click += (_, _) => OpacitySelected?.Invoke(percent);
            _opacityItems[percent] = item;
            _opacityItem.DropDownItems.Add(item);
        }

        var diagItem = new ToolStripMenuItem("显示诊断信息");
        diagItem.Click += (_, _) =>
        {
            diagItem.Checked = !diagItem.Checked;
            DiagnosticsToggled?.Invoke(diagItem.Checked);
        };

        var fitItem = new ToolStripMenuItem("适应窗口（重置缩放与位置）");
        fitItem.Click += (_, _) => FitRequested?.Invoke();

        var reselectItem = new ToolStripMenuItem("重新选择源窗口");
        reselectItem.Click += (_, _) => ReselectRequested?.Invoke();

        var logItem = new ToolStripMenuItem("打开日志文件夹");
        logItem.Click += (_, _) => LogFolderRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _topmostItem,
            _clickThroughItem,
            new ToolStripSeparator(),
            _zoomHeaderItem,
            zoomHost,
            fitItem,
            diagItem,
            _opacityItem,
            new ToolStripSeparator(),
            reselectItem,
            logItem,
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

    public event Action<bool>? TopmostToggled;

    public event Action<bool>? ClickThroughToggled;

    /// <summary>缩放比例（0.1 - 10.0）。</summary>
    public event Action<double>? ZoomChanged;

    public event Action<int>? OpacitySelected;

    public event Action? FitRequested;

    public event Action<bool>? DiagnosticsToggled;

    public event Action? ReselectRequested;

    public event Action? LogFolderRequested;

    public event Action? ExitRequested;

    public void SetTopmost(bool topmost)
    {
        _topmostItem.Checked = topmost;
        _topmostItem.Text = topmost ? "置顶显示：开" : "置顶显示：关";
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThroughItem.Checked = enabled;
        _clickThroughItem.Text = enabled ? "点击穿透：开" : "点击穿透：关";
    }

    public void SetZoom(double zoom)
    {
        _updatingZoom = true;
        _zoomTrack.Value = Math.Clamp(ZoomToTrack(zoom), _zoomTrack.Minimum, _zoomTrack.Maximum);
        _updatingZoom = false;

        _zoomHeaderItem.Text = $"缩放：{Math.Round(zoom * 100)}%";
    }

    public void SetOpacity(int percent)
    {
        foreach (var (key, item) in _opacityItems)
        {
            item.Checked = key == percent;
        }

        _opacityItem.Text = $"透明度：{percent}%";
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
