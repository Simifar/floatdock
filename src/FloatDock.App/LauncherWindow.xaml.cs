using System.Diagnostics;
using FloatDock.Core;
using FloatDock.Windows;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace FloatDock.App;

/// <summary>What the app menu needs from the dock that owns it.</summary>
internal interface ILauncherHost
{
    DockLanguage Language { get; }
    /// <summary>Whether open and close are animated (the Reduce motion setting and Windows' own animation setting).</summary>
    bool Animations { get; }
    /// <summary>Space to keep free at the bottom of the screen for the dock, in device-independent pixels.</summary>
    double BottomInset { get; }
    bool IsPinned(AppEntry app);
    Task PinAsync(AppEntry app);
    Task UnpinAsync(AppEntry app);
    /// <summary>The order of the whole list while nothing is typed.</summary>
    LauncherSort Sort { get; }
    /// <summary>When each app was first seen, see <see cref="AppFirstSeen"/>.</summary>
    IReadOnlyDictionary<string, long> FirstSeen { get; }
    Task HideAsync(AppEntry app);
    void Launch(AppEntry app, bool asAdministrator);
    /// <summary>An app is being dragged out of the menu; the dock accepts it as a drop until <see cref="DragEnded"/>.</summary>
    void DragStarted(AppEntry app);
    void DragEnded();
}

/// <summary>
/// Full-screen overlay with every installed app: search first, a "Frequent" row, then the whole list.
/// The window is created once and shown and hidden after that, so it opens fast.
/// </summary>
internal sealed partial class LauncherWindow : Window
{
    private const double ItemWidth = 128;
    private const int FrequentCount = 6;
    private const int MaxColumns = 10;
    private readonly ILauncherHost _host;
    private readonly IconService _icons;
    private readonly LaunchCounts _counts;
    private readonly nint _hwnd;
    private readonly Dictionary<string, LauncherItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AppEntry> _apps = [];
    private Storyboard? _board;
    private Stopwatch? _opening;
    private bool _open;
    private bool _closed;

    public LauncherWindow(ILauncherHost host, IconService icons, LaunchCounts counts)
    {
        _host = host;
        _icons = icons;
        _counts = counts;
        InitializeComponent();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        // Blurs the desktop behind the whole screen, so labels stay legible over busy windows.
        SystemBackdrop = new DesktopAcrylicBackdrop();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!WindowChrome.RemoveBorder(_hwnd)) LocalLog.Write("Warning", "dwm-border-unavailable");
        WindowChrome.HideFromSwitchers(_hwnd);
        if (!WindowChrome.EnableClientTransparency(_hwnd)) LocalLog.Write("Warning", "dwm-transparency-unavailable");
        Activated += (_, args) =>
        {
            // Clicking the dock, another window or another monitor leaves the menu.
            if (args.WindowActivationState == WindowActivationState.Deactivated && _open) Dismiss(byDeactivation: true);
        };
        Closed += (_, _) => { _closed = true; _open = false; };
        SearchBox.TextChanged += (_, _) => UpdateResults();
        SearchBox.PreviewKeyDown += OnSearchKeyDown;
        Root.PreviewKeyDown += OnPreviewKeyDown;
        Root.Tapped += OnRootTapped;
        Stage.SizeChanged += (_, _) => FitColumns();
        foreach (var grid in new[] { FrequentGrid, AllGrid })
        {
            grid.ItemClick += (_, args) => { if (args.ClickedItem is LauncherItem item) Launch(item, asAdministrator: false); };
            grid.ContainerContentChanging += OnContainerChanging;
            grid.ContextRequested += OnContextRequested;
            grid.PreviewKeyDown += OnGridKeyDown;
            grid.DragItemsStarting += OnDragStarting;
            grid.DragItemsCompleted += (_, _) => _host.DragEnded();
        }
    }

    public bool IsOpen => _open;

    /// <summary>Raised when the menu closes; true when it lost the focus instead of being closed on purpose.</summary>
    public event Action<bool>? Dismissed;

    private string T(string key) => UiText.Get(key, _host.Language);

    /// <param name="opening">Started by whoever asked for the menu, so the log shows the time the user waited.</param>
    public void Open(RectInt32 bounds, IReadOnlyList<AppEntry> apps, Stopwatch opening)
    {
        if (_closed) return;
        _opening = opening;
        _board?.Stop();
        SetApps(apps);
        Title = "FloatDock " + T("apps");
        SearchBox.PlaceholderText = T("appsSearch");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SearchBox, T("appsSearch"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AllGrid, T("allApps"));
        FrequentHeader.Text = T("frequent");
        AllHeader.Text = T("allApps");
        EmptyText.Text = T("noApps");
        Stage.Padding = new Thickness(32, 72, 32, _host.BottomInset);
        SearchBox.Text = string.Empty;
        UpdateResults();
        AppWindow.MoveAndResize(bounds);
        Root.Opacity = 0;
        _open = true;
        AppWindow.Show();
        Activate();
        WindowChrome.Focus(_hwnd);
        SearchBox.Focus(FocusState.Programmatic);
        Play(opening: true);
        CompositionTarget.Rendering += OnFirstFrame;
    }

    /// <summary>Takes a new app list (an app was installed or removed) without losing the query.</summary>
    public void UpdateApps(IReadOnlyList<AppEntry> apps)
    {
        SetApps(apps);
        if (_open) UpdateResults();
    }

    public void Dismiss(bool byDeactivation = false)
    {
        if (!_open) return;
        _open = false;
        Dismissed?.Invoke(byDeactivation);
        Play(opening: false);
    }

    private void OnFirstFrame(object? sender, object args)
    {
        CompositionTarget.Rendering -= OnFirstFrame;
        if (_opening is not { } timer) return;
        timer.Stop();
        _opening = null;
        LocalLog.Write("Info", $"launcher-open-{timer.ElapsedMilliseconds}ms");
    }

    private void SetApps(IReadOnlyList<AppEntry> apps)
    {
        _apps = apps;
        var next = new Dictionary<string, LauncherItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
            next[app.Id] = _items.TryGetValue(app.Id, out var existing) && existing.App == app ? existing : new LauncherItem(app);
        _items.Clear();
        foreach (var (id, item) in next) _items[id] = item;
    }

    /// <summary>
    /// Sizes the lists to a whole number of columns, so the headers line up with the first icon and the grid is centered.
    /// </summary>
    private void FitColumns()
    {
        var columns = Math.Clamp((int)((Stage.ActualWidth - Stage.Padding.Left - Stage.Padding.Right) / ItemWidth), 1, MaxColumns);
        var width = columns * ItemWidth + 8;
        FrequentPanel.Width = AllHeader.Width = AllGrid.Width = width;
    }

    private void UpdateResults()
    {
        var query = SearchBox.Text;
        var searching = !string.IsNullOrWhiteSpace(query);
        var counts = _counts.Snapshot();
        var firstSeen = _host.FirstSeen;
        var now = DateTimeOffset.UtcNow;
        var newLabel = T("newBadge");
        foreach (var item in _items.Values)
        {
            item.NewLabel = newLabel;
            item.IsNew = AppFirstSeen.IsNew(firstSeen, item.App.Id, now);
        }
        var results = AppSearch.Search(_apps, query, counts, _host.Sort, firstSeen);
        var frequent = searching ? [] : AppSearch.Frequent(_apps, counts, FrequentCount);
        FrequentGrid.ItemsSource = frequent.Select(app => _items[app.Id]).ToList();
        FrequentPanel.Visibility = frequent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AllHeader.Visibility = frequent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AllGrid.ItemsSource = results.Select(app => _items[app.Id]).ToList();
        // The best match is the one Enter starts.
        AllGrid.SelectedIndex = searching && results.Count > 0 ? 0 : -1;
        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Launch(LauncherItem item, bool asAdministrator)
    {
        Dismiss();
        _host.Launch(item.App, asAdministrator);
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            var item = AllGrid.SelectedItem as LauncherItem ?? AllGrid.Items.FirstOrDefault() as LauncherItem;
            if (item is not null) Launch(item, asAdministrator: false);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Down)
        {
            FocusGrid(FrequentPanel.Visibility == Visibility.Visible ? FrequentGrid : AllGrid);
            args.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        args.Handled = true;
        if (SearchBox.Text.Length > 0) SearchBox.Text = string.Empty;
        else Dismiss();
    }

    private void OnGridKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var grid = (GridView)sender;
        if (args.Key is VirtualKey.Up or VirtualKey.Down)
        {
            var index = FocusedIndex(grid);
            var columns = Columns(grid);
            if (args.Key == VirtualKey.Up && index >= 0 && index < columns)
            {
                if (grid == AllGrid && FrequentPanel.Visibility == Visibility.Visible) FocusGrid(FrequentGrid);
                else FocusSearch();
                args.Handled = true;
            }
            else if (args.Key == VirtualKey.Down && grid == FrequentGrid && index >= 0 && index + columns >= grid.Items.Count)
            {
                FocusGrid(AllGrid);
                args.Handled = true;
            }
        }
        else if (IsTextKey(args.Key) && !IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Menu))
        {
            // Typing anywhere searches: the character that follows this key press then lands in the box.
            FocusSearch();
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsTextKey(VirtualKey key) =>
        key is VirtualKey.Back or VirtualKey.Delete or (>= VirtualKey.Number0 and <= VirtualKey.Z) or (>= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9)
        || (int)key is (>= 186 and <= 192) or (>= 219 and <= 222);

    private void FocusSearch()
    {
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectionStart = SearchBox.Text.Length;
    }

    private static void FocusGrid(GridView grid)
    {
        grid.UpdateLayout();
        var index = Math.Max(0, grid.SelectedIndex);
        if (grid.ContainerFromIndex(index) is Control container) container.Focus(FocusState.Keyboard);
        else grid.Focus(FocusState.Keyboard);
    }

    private int FocusedIndex(GridView grid)
    {
        var element = FocusManager.GetFocusedElement(Content.XamlRoot) as DependencyObject;
        while (element is not null and not GridViewItem) element = VisualTreeHelper.GetParent(element);
        return element is GridViewItem container ? grid.IndexFromContainer(container) : -1;
    }

    private static int Columns(GridView grid) =>
        grid.ItemsPanelRoot is ItemsWrapGrid panel ? Math.Max(1, (int)(panel.ActualWidth / ItemWidth)) : 1;

    /// <summary>A click on the dimmed screen, between the icons, closes the menu.</summary>
    private void OnRootTapped(object sender, TappedRoutedEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is GridViewItem or TextBox or Microsoft.UI.Xaml.Controls.Primitives.ScrollBar) return;
        Dismiss();
    }

    private void OnContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is LauncherItem item) _ = LoadIconAsync(item);
    }

    private async Task LoadIconAsync(LauncherItem item)
    {
        if (item.IconRequested) return;
        item.IconRequested = true;
        try
        {
            var needed = 56 * (Content.XamlRoot?.RasterizationScale ?? 1);
            var size = new[] { 32, 48, 64, 96, 128, 256 }.FirstOrDefault(candidate => candidate >= needed, 256);
            var bytes = await _icons.GetAsync(IconSources(item.App), size);
            if (bytes is not null) item.Icon = await IconImages.FromPngAsync(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            LocalLog.Write("Warning", "icon-load", ex);
        }
    }

    private static IEnumerable<string?> IconSources(AppEntry app)
    {
        yield return IconService.AppsFolderItem(app.Id);
        yield return app.ExecutablePath;
        if (app.Target.Kind == LaunchTargetKind.Executable) yield return app.Id;
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var element = args.OriginalSource as DependencyObject;
        while (element is not null and not GridViewItem) element = VisualTreeHelper.GetParent(element);
        if (element is not GridViewItem container || ((GridView)sender).ItemFromContainer(container) is not LauncherItem item) return;
        args.Handled = true;
        var menu = CreateMenu(item);
        if (args.TryGetPosition(sender, out var point)) menu.ShowAt(sender, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        else menu.ShowAt(container);
    }

    private MenuFlyout CreateMenu(LauncherItem item)
    {
        var app = item.App;
        var menu = new MenuFlyout();
        var pinned = _host.IsPinned(app);
        var pin = new MenuFlyoutItem { Text = pinned ? T("unpin") : T("pin") };
        pin.Click += async (_, _) => await (pinned ? _host.UnpinAsync(app) : _host.PinAsync(app));
        menu.Items.Add(pin);
        // Packaged apps and shortcuts without a plain executable cannot be elevated or located as a file.
        if (!app.IsPackaged && app.ExecutablePath is not null)
        {
            var admin = new MenuFlyoutItem { Text = T("runAsAdmin") };
            admin.Click += (_, _) => Launch(item, asAdministrator: true);
            menu.Items.Add(admin);
            var location = new MenuFlyoutItem { Text = T("openLocation") };
            location.Click += (_, _) =>
            {
                Dismiss();
                ShellActions.RevealInExplorer(app.ExecutablePath);
            };
            menu.Items.Add(location);
        }
        var hide = new MenuFlyoutItem { Text = T("hideApp") };
        hide.Click += async (_, _) => await _host.HideAsync(app);
        menu.Items.Add(hide);
        var uninstall = new MenuFlyoutItem { Text = T("uninstall") };
        uninstall.Click += (_, _) =>
        {
            Dismiss();
            ShellActions.OpenInstalledApps();
        };
        menu.Items.Add(uninstall);
        return menu;
    }

    private void OnDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (args.Items.FirstOrDefault() is not LauncherItem item)
        {
            args.Cancel = true;
            return;
        }
        args.Data.SetText(item.Name);
        args.Data.RequestedOperation = DataPackageOperation.Copy;
        _host.DragStarted(item.App);
    }

    private void Play(bool opening)
    {
        _board?.Stop();
        _board = null;
        if (!_host.Animations)
        {
            Root.Opacity = opening ? 1 : 0;
            StageScale.ScaleX = StageScale.ScaleY = 1;
            if (!opening) AppWindow.Hide();
            return;
        }
        var board = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(opening ? 200 : 140);
        Animate(board, Root, "Opacity", opening ? 0 : 1, opening ? 1 : 0, duration);
        foreach (var axis in new[] { "ScaleX", "ScaleY" })
            Animate(board, StageScale, axis, opening ? 0.97 : 1, opening ? 1 : 0.98, duration);
        board.Completed += (_, _) =>
        {
            if (ReferenceEquals(_board, board) && !_open && !_closed) AppWindow.Hide();
        };
        _board = board;
        board.Begin();
    }

    private static void Animate(Storyboard board, DependencyObject target, string property, double from, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = duration, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
    }
}
