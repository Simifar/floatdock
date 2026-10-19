using FloatDock.Core;
using FloatDock.Windows;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FloatDock.App;

/// <summary>Items that are not apps: Downloads, the Recycle Bin, show desktop, the clock and the quick panels. Each is a Settings switch.</summary>
public sealed partial class DockWindow
{
    private Flyout? _stackFlyout;
    private string? _downloadsPath;
    private bool _downloadsResolved;
    private bool? _binFull;
    private TextBlock? _clockTime;
    private TextBlock? _clockDate;

    private void AppendSystemItems(List<DockItem> wanted)
    {
        if (!_downloadsResolved)
        {
            _downloadsResolved = true;
            _downloadsPath = KnownFolders.DownloadsPath();
        }
        var before = _views.Count;
        var system = new List<DockItem>();
        if (Settings.ShowDownloads && _downloadsPath is { } downloads && Directory.Exists(downloads))
            system.Add(View("downloads", () => CreateDownloadsItem(downloads)));
        if (Settings.ShowRecycleBin) system.Add(View("bin", CreateBinItem));
        if (Settings.ShowDesktopButton)
            system.Add(View("desktop", () => CreateTile("desktop", DockItemKind.ShowDesktop, "", T("showDesktop"), () => ShellActions.ToggleDesktop())));
        if (Settings.ShowQuickSettings)
            system.Add(View("quick", () => CreateTile("quick", DockItemKind.QuickSettings, "", T("quickSettings"), () => ShellActions.PressWinChord('A'))));
        if (Settings.ShowNotifications)
            system.Add(View("notifications", () => CreateTile("notifications", DockItemKind.Notifications, "", T("notifications"), () => ShellActions.PressWinChord(WindowsBuild.NotificationsKey(Environment.OSVersion.Version.Build)))));
        if (Settings.ShowClock) system.Add(View("clock", CreateClock));
        if (system.Count > 0 && Settings.ShowSeparator && wanted.Count > 0 && wanted[^1].Kind != DockItemKind.Separator)
            wanted.Add(View("sep:system", () => CreateSeparator("sep:system")));
        wanted.AddRange(system);
        if (system.Count == 0) _systemTimer.Stop();
        else
        {
            // Render runs on every window change, and Start restarts a running timer: the clock would stop while titles change.
            if (!_systemTimer.IsEnabled) _systemTimer.Start();
            if (_views.Count != before) RefreshSystemItems();
        }
    }

    private void CloseSystemFlyouts() => _stackFlyout?.Hide();

    private DockItem CreateDownloadsItem(string folder)
    {
        var item = CreateButtonItem("downloads", DockItemKind.Downloads, new FontIcon { Glyph = "", FontSize = Math.Round(Settings.IconSize * 0.55), HorizontalAlignment = HorizontalAlignment.Center }, T("downloads"));
        var button = item.Button!;
        AutomationProperties.SetAutomationId(button, "FloatDock.downloads");
        _ = LoadIconAsync(item.Stack!, [folder]);
        button.Click += (_, _) => ShowFolderStack(button, folder, T("downloads"));
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = T("openFolder") };
        open.Click += (_, _) => ShellActions.OpenFolder(folder);
        menu.Items.Add(open);
        TrackMenu(menu);
        button.ContextFlyout = menu;
        return item;
    }

    private DockItem CreateBinItem()
    {
        var item = CreateButtonItem("bin", DockItemKind.RecycleBin, new FontIcon { Glyph = "", FontSize = Math.Round(Settings.IconSize * 0.55), HorizontalAlignment = HorizontalAlignment.Center }, T("recycleBin"));
        var button = item.Button!;
        AutomationProperties.SetAutomationId(button, "FloatDock.bin");
        _ = LoadIconAsync(item.Stack!, [RecycleBin.FolderName]);
        button.Click += (_, _) => ShellActions.OpenFolder(RecycleBin.FolderName);
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = T("openBin") };
        open.Click += (_, _) => ShellActions.OpenFolder(RecycleBin.FolderName);
        menu.Items.Add(open);
        var empty = new MenuFlyoutItem { Text = T("emptyBin") };
        // The Shell asks for confirmation itself; nothing is deleted unless the user agrees there.
        empty.Click += async (_, _) =>
        {
            try { await Host.ShellWorker.RunAsync(() => RecycleBin.Empty(_hwnd)); }
            catch (InvalidOperationException) { return; }
            RefreshSystemItems();
        };
        menu.Items.Add(empty);
        menu.Opening += (_, _) => empty.IsEnabled = _binFull != false;
        TrackMenu(menu);
        button.ContextFlyout = menu;
        return item;
    }

    private DockItem CreateClock()
    {
        _clockTime = new TextBlock { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        _clockDate = new TextBlock { FontSize = 11, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(_clockTime);
        stack.Children.Add(_clockDate);
        var button = new Button
        {
            Content = stack,
            Width = 84,
            Height = Settings.IconSize + 16,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12)
        };
        AutomationProperties.SetAutomationId(button, "FloatDock.clock");
        // The calendar sits in the notification center, which Windows opens with Win+N.
        button.Click += (_, _) => ShellActions.PressWinChord(WindowsBuild.NotificationsKey(Environment.OSVersion.Version.Build));
        UpdateClock();
        return new DockItem { Key = "clock", Kind = DockItemKind.Clock, Element = button };
    }

    private void UpdateClock()
    {
        if (_clockTime is null || _clockDate is null) return;
        var now = DateTime.Now;
        _clockTime.Text = now.ToString("t");
        _clockDate.Text = now.ToString("ddd, d MMM");
        if (_views.TryGetValue("clock", out var clock)) AutomationProperties.SetName(clock.Element, now.ToString("f"));
    }

    /// <summary>Clock text and the Recycle Bin's state: its icon and tooltip follow whether it holds anything.</summary>
    private async void RefreshSystemItems()
    {
        try
        {
            UpdateClock();
            if (!_views.TryGetValue("bin", out var bin)) return;
            var count = await Host.ShellWorker.RunAsync(RecycleBin.ItemCount);
            if (_closing || !_views.ContainsKey("bin")) return;
            var full = count is > 0;
            ToolTipService.SetToolTip(bin.Button!, count is > 0 ? $"{T("recycleBin")} ({count})" : T("recycleBin"));
            if (_binFull == full) return;
            _binFull = full;
            Host.Icons.Invalidate(RecycleBin.FolderName);
            await LoadIconAsync(bin.Stack!, [RecycleBin.FolderName]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            LocalLog.Write("Warning", "system-items-refresh", ex);
        }
    }

    /// <summary>The newest entries of a folder in a grid above the icon; a click opens the entry.</summary>
    private void ShowFolderStack(FrameworkElement anchor, string folder, string title)
    {
        _stackFlyout?.Hide();
        var entries = FolderListing.Recent(folder, 20);
        var content = new StackPanel { Spacing = 8, Width = 4 * 96 + 8 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var openFolder = new HyperlinkButton { Content = T("openFolder"), Padding = new Thickness(4, 0, 4, 0) };
        Grid.SetColumn(openFolder, 1);
        header.Children.Add(openFolder);
        content.Children.Add(header);
        var flyout = new Flyout { Content = content, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top, ShouldConstrainToRootBounds = false };
        openFolder.Click += (_, _) =>
        {
            flyout.Hide();
            ShellActions.OpenFolder(folder);
        };
        if (entries.Count == 0) content.Children.Add(new TextBlock { Text = T("emptyFolder"), Opacity = 0.7, Margin = new Thickness(0, 8, 0, 8) });
        else
        {
            var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 4, ItemWidth = 96, ItemHeight = 92 };
            foreach (var entry in entries) grid.Children.Add(CreateStackEntry(entry, flyout));
            content.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 3 * 92 + 4, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        TrackMenu(flyout);
        flyout.Closed += (_, _) => { if (ReferenceEquals(_stackFlyout, flyout)) _stackFlyout = null; };
        _stackFlyout = flyout;
        flyout.ShowAt(anchor);
    }

    private Button CreateStackEntry(FolderEntry entry, Flyout flyout)
    {
        var icon = new FontIcon { Glyph = entry.IsFolder ? "" : "", FontSize = 28, Height = 40 };
        var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 84
        });
        var button = new Button
        {
            Content = stack,
            Width = 94,
            Height = 90,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(button, entry.Name);
        AutomationProperties.SetName(button, entry.Name);
        button.Click += (_, _) =>
        {
            flyout.Hide();
            // Through the Shell for every kind, so a downloaded program still meets SmartScreen and its zone warning.
            ShellActions.Open(entry.Path);
        };
        _ = LoadIconAsync(stack, [entry.Path]);
        return button;
    }
}
