using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WinMirroOverlay.Common;
using WinMirroOverlay.Interop;

namespace WinMirroOverlay.Views;

/// <summary>窗口 1：列出当前可镜像的顶层窗口，单击某项即开始镜像。</summary>
public partial class PickerWindow : Window
{
    private readonly ObservableCollection<WindowInfo> _all = new();
    private readonly ObservableCollection<WindowInfo> _filtered = new();
    private bool _selected;

    public PickerWindow()
    {
        InitializeComponent();

        Icon = App.LoadAppIconImage();
        WindowList.ItemsSource = _filtered;

        Loaded += (_, _) => Refresh();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event EventHandler<WindowInfo>? WindowSelected;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar();
    }

    /// <summary>尽量把系统标题栏也变成深色，和窗口内容协调。</summary>
    private void ApplyDarkTitleBar()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int enabled = 1;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 2004+），19 是更早版本的同一属性。
        if (NativeMethods.DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
        {
            NativeMethods.DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
    }

    private void Refresh()
    {
        var windows = WindowEnumerator.Enumerate();

        _all.Clear();
        foreach (var window in windows)
        {
            _all.Add(window);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string keyword = SearchBox.Text.Trim();

        _filtered.Clear();
        foreach (var window in _all)
        {
            if (keyword.Length == 0
                || window.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || window.ProcessName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _filtered.Add(window);
            }
        }

        CountText.Text = keyword.Length == 0
            ? $"共 {_filtered.Count} 个可镜像窗口"
            : $"匹配 {_filtered.Count} / {_all.Count} 个窗口";

        if (_filtered.Count > 0 && WindowList.SelectedIndex < 0)
        {
            WindowList.SelectedIndex = 0;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool empty = SearchBox.Text.Length == 0;
        SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void WindowList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(WindowList, source) is ListBoxItem { DataContext: WindowInfo info })
        {
            Select(info);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when WindowList.SelectedItem is WindowInfo info:
                Select(info);
                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            case Key.F5:
                Refresh();
                e.Handled = true;
                break;
        }
    }

    private void Select(WindowInfo info)
    {
        if (_selected)
        {
            return;
        }

        // 防止 WGC 不支持该窗口时反复触发。
        _selected = true;
        WindowSelected?.Invoke(this, info);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
}
