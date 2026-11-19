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
    private readonly AppSettings _settings;

    private ContextMenuStrip _menu;
    private ToolStripMenuItem _modeTopmost = null!;
    private ToolStripMenuItem _modeTopmostThrough = null!;
    private ToolStripMenuItem _modeNone = null!;
    private ToolStripMenuItem _allDesktopsItem = null!;
    private ToolStripMenuItem _zoomHeaderItem = null!;
    private ToolStripMenuItem _opacityHeaderItem = null!;
    private TrackBar _zoomTrack = null!;
    private TrackBar _opacityTrack = null!;

    private OverlayMode _mode = OverlayMode.Topmost;
    private bool _allDesktops;
    private double _zoom = 1.0;
    private double _opacity = 1.0;
    private bool _updatingSlider;

    public TrayIconController(AppSettings settings)
    {
        _settings = settings;
        _allDesktops = settings.ShowOnAllDesktops;

        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/app.ico"));
        _iconImage = new Icon(resource!.Stream);

        _menu = BuildMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconImage,
            Text = Loc.AppTitle,
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

    public event Action? ReselectRequested;

    public event Action? ExitRequested;

    public event Action? LanguageChanged;

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            // ShowImageMargin 必须为 true，勾选标记才会显示出来。
            ShowImageMargin = true,
        };

        _modeTopmost = new ToolStripMenuItem(Loc.TrayTopmost);
        _modeTopmost.Click += (_, _) => ModeSelected?.Invoke(OverlayMode.Topmost);

        _modeTopmostThrough = new ToolStripMenuItem(Loc.TrayTopmostThrough);
        _modeTopmostThrough.Click += (_, _) => ModeSelected?.Invoke(OverlayMode.TopmostClickThrough);

        _modeNone = new ToolStripMenuItem(Loc.TrayNoTopmost);
        _modeNone.Click += (_, _) => ModeSelected?.Invoke(OverlayMode.None);

        _allDesktopsItem = new ToolStripMenuItem(Loc.TrayAllDesktops);
        _allDesktopsItem.Click += (_, _) => AllDesktopsToggled?.Invoke(!_allDesktopsItem.Checked);

        _zoomHeaderItem = new ToolStripMenuItem(string.Format(Loc.TrayZoomFormat, Math.Round(_zoom * 100)));
        var zoomHost = CreateSliderHost(0, 1000, ZoomToTrack(_zoom), out _zoomTrack, out var zoomPanel);
        _zoomTrack.ValueChanged += (_, _) => OnZoomTrackChanged();
        zoomPanel.Controls.Add(_zoomTrack);

        _opacityHeaderItem = new ToolStripMenuItem(string.Format(Loc.TrayOpacityFormat, Math.Round(_opacity * 100)));
        var opacityHost = CreateSliderHost(MinOpacityPercent, 100, (int)Math.Round(_opacity * 100), out _opacityTrack, out var opacityPanel);
        _opacityTrack.ValueChanged += (_, _) => OnOpacityTrackChanged();
        opacityPanel.Controls.Add(_opacityTrack);

        var fitItem = new ToolStripMenuItem(Loc.TrayFit);
        fitItem.Click += (_, _) => FitRequested?.Invoke();

        var reselectItem = new ToolStripMenuItem(Loc.TrayReselect);
        reselectItem.Click += (_, _) => ReselectRequested?.Invoke();

        var languageItem = new ToolStripMenuItem(Loc.TrayLanguage);
        languageItem.DropDownItems.Add(BuildLanguageItem(Loc.LangAuto, AppLanguage.Auto));
        languageItem.DropDownItems.Add(new ToolStripSeparator());
        languageItem.DropDownItems.Add(BuildLanguageItem("English", AppLanguage.English));
        languageItem.DropDownItems.Add(BuildLanguageItem("简体中文", AppLanguage.SimplifiedChinese));
        languageItem.DropDownItems.Add(BuildLanguageItem("繁體中文", AppLanguage.TraditionalChinese));

        var exitItem = new ToolStripMenuItem(Loc.TrayExit);
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.AddRange(new ToolStripItem[]
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
            languageItem,
            exitItem,
        });

        ApplyChecks();
        return menu;
    }

    private ToolStripMenuItem BuildLanguageItem(string text, AppLanguage language)
    {
        var item = new ToolStripMenuItem(text) { Tag = language, Checked = Loc.Language == language };
        item.Click += (_, _) => ApplyLanguage(language);
        return item;
    }

    private void ApplyChecks()
    {
        _modeTopmost.Checked = _mode == OverlayMode.Topmost;
        _modeTopmostThrough.Checked = _mode == OverlayMode.TopmostClickThrough;
        _modeNone.Checked = _mode == OverlayMode.None;
        _allDesktopsItem.Checked = _allDesktops;
    }

    private void ApplyLanguage(AppLanguage language)
    {
        _settings.Language = (int)language;
        _settings.Save();

        Loc.SetLanguage(language);
        LanguageChanged?.Invoke();

        // 菜单项要在点击回调结束后再重建，避免销毁正在处理事件的菜单。
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(new Action(RebuildMenu));
        }
        else
        {
            RebuildMenu();
        }
    }

    private void RebuildMenu()
    {
        var old = _menu;
        _menu = BuildMenu();
        _notifyIcon.ContextMenuStrip = _menu;
        old.Dispose();
    }

    private void OnZoomTrackChanged()
    {
        if (_updatingSlider)
        {
            return;
        }

        double zoom = TrackToZoom(_zoomTrack.Value);
        _zoom = zoom;
        _zoomHeaderItem.Text = string.Format(Loc.TrayZoomFormat, Math.Round(zoom * 100));
        ZoomChanged?.Invoke(zoom);
    }

    private void OnOpacityTrackChanged()
    {
        if (_updatingSlider)
        {
            return;
        }

        _opacity = _opacityTrack.Value / 100.0;
        _opacityHeaderItem.Text = string.Format(Loc.TrayOpacityFormat, _opacityTrack.Value);
        OpacityChanged?.Invoke(_opacity);
    }

    public void SetMode(OverlayMode mode)
    {
        _mode = mode;
        ApplyChecks();
    }

    public void SetAllDesktops(bool enabled)
    {
        _allDesktops = enabled;
        ApplyChecks();
    }

    public void SetZoom(double zoom)
    {
        _zoom = zoom;

        _updatingSlider = true;
        _zoomTrack.Value = Math.Clamp(ZoomToTrack(zoom), _zoomTrack.Minimum, _zoomTrack.Maximum);
        _updatingSlider = false;

        _zoomHeaderItem.Text = string.Format(Loc.TrayZoomFormat, Math.Round(zoom * 100));
    }

    public void SetOpacity(double opacity)
    {
        _opacity = opacity;
        int percent = Math.Clamp((int)Math.Round(opacity * 100), _opacityTrack.Minimum, _opacityTrack.Maximum);

        _updatingSlider = true;
        _opacityTrack.Value = percent;
        _updatingSlider = false;

        _opacityHeaderItem.Text = string.Format(Loc.TrayOpacityFormat, percent);
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
            Value = Math.Clamp(value, minimum, maximum),
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
