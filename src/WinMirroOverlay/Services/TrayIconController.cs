using System.Drawing;
using System.Windows.Forms;

namespace WinMirroOverlay.Services;

/// <summary>托盘图标与右键菜单（左键点击也会弹出同一菜单）。</summary>
internal sealed class TrayIconController : IDisposable
{
    public static readonly int[] ZoomPresets = { 25, 50, 75, 100, 125, 150, 175, 200, 225, 250 };

    public static readonly int[] OpacityPresets = { 100, 90, 75, 50, 25 };

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _iconImage;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _topmostItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly Dictionary<int, ToolStripMenuItem> _zoomItems = new();
    private readonly Dictionary<int, ToolStripMenuItem> _opacityItems = new();

    public TrayIconController()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/app.ico"));
        _iconImage = new Icon(resource!.Stream);

        _menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ShowImageMargin = false,
        };

        _topmostItem = new ToolStripMenuItem("置顶显示");
        _topmostItem.Click += (_, _) => TopmostToggled?.Invoke(!_topmostItem.Checked);

        _clickThroughItem = new ToolStripMenuItem("点击穿透");
        _clickThroughItem.Click += (_, _) => ClickThroughToggled?.Invoke(!_clickThroughItem.Checked);

        var zoomMenu = new ToolStripMenuItem("缩放显示");
        foreach (int percent in ZoomPresets)
        {
            var item = new ToolStripMenuItem($"{percent}%") { Tag = percent };
            item.Click += (_, _) => ZoomSelected?.Invoke(percent);
            _zoomItems[percent] = item;
            zoomMenu.DropDownItems.Add(item);
        }

        var opacityMenu = new ToolStripMenuItem("透明度");
        foreach (int percent in OpacityPresets)
        {
            var item = new ToolStripMenuItem($"{percent}%") { Tag = percent };
            item.Click += (_, _) => OpacitySelected?.Invoke(percent);
            _opacityItems[percent] = item;
            opacityMenu.DropDownItems.Add(item);
        }

        var reselectItem = new ToolStripMenuItem("重新选择源窗口");
        reselectItem.Click += (_, _) => ReselectRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _topmostItem,
            _clickThroughItem,
            new ToolStripSeparator(),
            zoomMenu,
            opacityMenu,
            new ToolStripSeparator(),
            reselectItem,
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

    public event Action<int>? ZoomSelected;

    public event Action<int>? OpacitySelected;

    public event Action? ReselectRequested;

    public event Action? ExitRequested;

    public void SetTopmost(bool topmost) => _topmostItem.Checked = topmost;

    public void SetClickThrough(bool enabled) => _clickThroughItem.Checked = enabled;

    public void SetZoom(int percent)
    {
        foreach (var (key, item) in _zoomItems)
        {
            item.Checked = key == percent;
        }
    }

    public void SetOpacity(int percent)
    {
        foreach (var (key, item) in _opacityItems)
        {
            item.Checked = key == percent;
        }
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
}
