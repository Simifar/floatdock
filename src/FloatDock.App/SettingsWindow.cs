using FloatDock.Core;
using FloatDock.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace FloatDock.App;

public sealed class SettingsWindow : Window
{
    private const string XamlNamespace = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";
    // Page key, text key, Segoe Fluent Icons glyph.
    private static readonly (string Key, string Text, string Glyph)[] Pages =
    [
        ("general", "navGeneral", ""),
        ("appearance", "appearance", ""),
        ("dock", "navDock", ""),
        ("launcher", "navLauncher", ""),
        ("pinned", "pinnedTitle", ""),
        ("updates", "updatesTitle", "")
    ];

    private DockSettings _settings;
    private readonly Func<DockSettings, Task<DockSettings>> _save;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Func<Task<IReadOnlyList<PinnedApp>>> _readTaskbarPins;
    private readonly Func<Task<UpdateCheck>> _checkForUpdates;
    private readonly Func<Task<bool>> _installUpdate;
    private readonly Func<string, string> _appName;
    private readonly Func<string, Task> _showApp;
    private UpdateCheck? _update;
    private int _changeVersion;
    private string? _taskbarStatus;
    private bool _firstRun;
    private bool _importOnFirstRun = true;
    private string? _importResult;
    private InfoBar? _statusBar;
    private TextBlock? _hotKeyStatus;
    private bool _hotKeyFailed;
    private string _page = "general";

    internal SettingsWindow(DockSettings settings, string? taskbarStatus, bool firstRun, Func<DockSettings, Task<DockSettings>> save,
        Func<Task<IReadOnlyList<PinnedApp>>> readTaskbarPins, Func<Task<UpdateCheck>> checkForUpdates, Func<Task<bool>> installUpdate,
        Func<string, string> appName, Func<string, Task> showApp)
    {
        _settings = settings;
        _taskbarStatus = taskbarStatus;
        _firstRun = firstRun;
        _save = save;
        _readTaskbarPins = readTaskbarPins;
        _checkForUpdates = checkForUpdates;
        _installUpdate = installUpdate;
        _appName = appName;
        _showApp = showApp;
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(820, 680));
        if (Path.Combine(AppContext.BaseDirectory, "Assets", "FloatDock.ico") is { } icon && File.Exists(icon)) AppWindow.SetIcon(icon);
        Build();
    }

    /// <summary>The window: a page list on the left, the chosen page on the right. Rebuilt whenever a setting changes the text or the list.</summary>
    private void Build()
    {
        Title = T("settingsTitle");
        var nav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsSettingsVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsPaneToggleButtonVisible = false,
            OpenPaneLength = 208,
            RequestedTheme = _settings.Theme switch
            {
                DockTheme.Light => ElementTheme.Light,
                DockTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            }
        };
        // The window has no Mica or acrylic behind it (they are not on Windows 10), so the surfaces are solid; without this the
        // pane and the page stay dark when the page is forced light.
        var light = _settings.Theme == DockTheme.Light || (_settings.Theme == DockTheme.System && Application.Current.RequestedTheme == ApplicationTheme.Light);
        var pane = new Microsoft.UI.Xaml.Media.SolidColorBrush(light ? Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243) : Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32));
        nav.Resources["NavigationViewDefaultPaneBackground"] = pane;
        nav.Resources["NavigationViewExpandedPaneBackground"] = pane;
        nav.Resources["NavigationViewContentBackground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(light ? Microsoft.UI.ColorHelper.FromArgb(255, 250, 250, 250) : Microsoft.UI.ColorHelper.FromArgb(255, 39, 39, 39));
        var header = new StackPanel { Margin = new Thickness(16, 12, 16, 12), Spacing = 2 };
        header.Children.Add(new TextBlock { Text = "FloatDock", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, T("version"), typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)),
            FontSize = 12,
            Opacity = 0.65
        });
        nav.PaneHeader = header;
        foreach (var (key, text, glyph) in Pages)
            nav.MenuItems.Add(new NavigationViewItem { Content = T(text), Tag = key, Icon = new FontIcon { Glyph = glyph } });
        nav.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItem is NavigationViewItem { Tag: string key }) ShowPage(nav, key);
        };
        Content = nav;
        nav.SelectedItem = nav.MenuItems.OfType<NavigationViewItem>().First(item => (string)item.Tag == _page);
    }

    private void ShowPage(NavigationView nav, string key)
    {
        _page = key;
        _statusBar = null;
        _hotKeyStatus = null;
        var page = new StackPanel { Spacing = 24, Padding = new Thickness(36, 28, 36, 36), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(new TextBlock
        {
            Text = T(Pages.First(entry => entry.Key == key).Text),
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        if (_firstRun) page.Children.Add(CreateFirstRunPrompt());
        switch (key)
        {
            case "general": BuildGeneral(page); break;
            case "appearance": BuildAppearance(page); break;
            case "dock": BuildDock(page); break;
            case "launcher": BuildLauncher(page); break;
            case "pinned": page.Children.Add(CreatePinnedSection()); break;
            default: page.Children.Add(CreateUpdatesSection()); break;
        }
        nav.Content = new ScrollViewer { Content = page };
    }

    // --- Pages -----------------------------------------------------------------------------------------------------

    private void BuildGeneral(StackPanel page)
    {
        var language = Combo("language", [T("system"), "English", "Русский"], (int)_settings.Language);
        language.Combo.SelectionChanged += async (_, _) =>
        {
            await UpdateAsync(_settings with { Language = (DockLanguage)language.Combo.SelectedIndex });
            Build();
        };
        page.Children.Add(Card(
            ToggleRow("startup", _settings.StartWithWindows, value => _settings with { StartWithWindows = value }, settings => settings.StartWithWindows),
            language.Row,
            ToggleRow("reduceMotion", _settings.ReduceMotion, value => _settings with { ReduceMotion = value })));
    }

    private void BuildAppearance(StackPanel page)
    {
        var theme = Combo("theme", [T("system"), T("light"), T("dark")], (int)_settings.Theme);
        theme.Combo.SelectionChanged += async (_, _) =>
        {
            await UpdateAsync(_settings with { Theme = (DockTheme)theme.Combo.SelectedIndex });
            Build();
        };
        page.Children.Add(Section(T("sectionIcons"), Card(
            SliderRow("iconSize", 32, 96, 4, _settings.IconSize, value => _settings with { IconSize = (int)value }, value => $"{value:0}"),
            SliderRow("magnification", 1, 2, 0.05, _settings.Magnification, value => _settings with { Magnification = value }, value => $"{value:0.00}×"),
            SliderRow("spacing", 0, 32, 2, _settings.Spacing, value => _settings with { Spacing = (int)value }, value => $"{value:0}"))));
        page.Children.Add(Section(T("sectionShelf"), Card(
            theme.Row,
            SliderRow("shelfOpacity", 30, 100, 2, _settings.ShelfOpacity, value => _settings with { ShelfOpacity = (int)value }, value => $"{value:0} %"),
            SliderRow("shelfRadius", 0, 32, 2, _settings.ShelfRadius, value => _settings with { ShelfRadius = (int)value }, value => $"{value:0}"))));
    }

    /// <summary>A slider with its current value written next to it.</summary>
    private Grid SliderRow(string key, double min, double max, double step, double value, Func<double, DockSettings> change, Func<double, string> format)
    {
        var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = step, Value = value, Width = 200, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = format(value), Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65 };
        slider.ValueChanged += async (_, args) =>
        {
            label.Text = format(args.NewValue);
            await UpdateAsync(change(args.NewValue));
        };
        var control = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        control.Children.Add(slider);
        control.Children.Add(label);
        var row = Row(T(key), control);
        // The name belongs to the slider, which is what a screen reader focuses.
        AutomationProperties.SetName(slider, T(key));
        return row;
    }

    private void BuildDock(StackPanel page)
    {
        var mode = Combo("mode", [T("floating"), T("reserved"), T("autoHide")], (int)_settings.Mode);
        mode.Combo.SelectionChanged += async (_, _) =>
        {
            await UpdateAsync(_settings with { Mode = (DockMode)Math.Max(0, mode.Combo.SelectedIndex) });
            mode.Combo.SelectedIndex = (int)_settings.Mode;
        };
        var displays = Combo("displays", [T("displaysPrimary"), T("displaysAll")], (int)_settings.Displays);
        displays.Combo.SelectionChanged += async (_, _) =>
        {
            await UpdateAsync(_settings with { Displays = (DockDisplays)Math.Max(0, displays.Combo.SelectedIndex) });
            displays.Combo.SelectedIndex = (int)_settings.Displays;
        };
        page.Children.Add(Section(T("behavior"), Card(
            mode.Row,
            displays.Row,
            ToggleRow("hideInFullscreen", _settings.HideInFullscreen, value => _settings with { HideInFullscreen = value }))));

        page.Children.Add(Section(T("itemsTitle"), Card(
            ToggleRow("showApps", _settings.ShowAppsButton, value => _settings with { ShowAppsButton = value }),
            ToggleRow("showSeparator", _settings.ShowSeparator, value => _settings with { ShowSeparator = value }),
            ToggleRow("showDownloads", _settings.ShowDownloads, value => _settings with { ShowDownloads = value }),
            ToggleRow("showRecycleBin", _settings.ShowRecycleBin, value => _settings with { ShowRecycleBin = value }),
            ToggleRow("showDesktopButton", _settings.ShowDesktopButton, value => _settings with { ShowDesktopButton = value }),
            ToggleRow("showClock", _settings.ShowClock, value => _settings with { ShowClock = value }),
            ToggleRow("showQuickSettings", _settings.ShowQuickSettings, value => _settings with { ShowQuickSettings = value }),
            ToggleRow("showNotifications", _settings.ShowNotifications, value => _settings with { ShowNotifications = value }))));

        if (!PackageIdentity.IsPackaged) page.Children.Add(Section(T("taskbarSection"), CreateTaskbarReplacement()));
    }

    private void BuildLauncher(StackPanel page)
    {
        var sort = Combo("launcherSort", [T("sortName"), T("sortUsed"), T("sortNew")], (int)_settings.LauncherSort);
        sort.Combo.SelectionChanged += async (_, _) =>
        {
            if (sort.Combo.SelectedIndex < 0) return;
            await UpdateAsync(_settings with { LauncherSort = (LauncherSort)sort.Combo.SelectedIndex });
        };
        page.Children.Add(Card(CreateHotKeyChooser(), sort.Row,
            new TextBlock { Text = T("sortNewHelp"), FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 10, 16, 12) }));

        var rows = new List<UIElement>();
        if (_settings.HiddenApps.Count == 0)
            rows.Add(new TextBlock { Text = T("hiddenEmpty"), TextWrapping = TextWrapping.Wrap, Opacity = 0.65, Margin = new Thickness(16, 12, 16, 12) });
        foreach (var id in _settings.HiddenApps)
        {
            var name = _appName(id);
            var show = new Button { Content = T("showAgain") };
            AutomationProperties.SetName(show, $"{T("showAgain")}: {name}");
            show.Click += async (_, _) =>
            {
                await _showApp(id);
                _settings = _settings with { HiddenApps = _settings.HiddenApps.Where(hidden => !hidden.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList() };
                Build();
            };
            var row = Row(name, show);
            // The button, not the row, is what a screen reader reaches; the row's own name comes from the text.
            AutomationProperties.SetName(show, $"{T("showAgain")}: {name}");
            rows.Add(row);
        }
        page.Children.Add(Section(T("hiddenTitle"), Card([.. rows])));
    }

    private UIElement CreateTaskbarReplacement()
    {
        _statusBar = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsClosable = false,
            IsOpen = _taskbarStatus is not null,
            Message = _taskbarStatus is null ? string.Empty : T(_taskbarStatus),
            Margin = new Thickness(16, 0, 16, 12)
        };
        return Card(
            ToggleRow("replaceTaskbar", _settings.ReplaceTaskbar, value => _settings with { ReplaceTaskbar = value }, settings => settings.ReplaceTaskbar,
                description: T("replaceHelp")),
            _statusBar);
    }

    // --- Building blocks -------------------------------------------------------------------------------------------

    private string T(string key) => UiText.Get(key, _settings.Language);

    /// <summary>Elements loaded from XAML so that their brushes follow the theme of the window.</summary>
    private static T Themed<T>(string xaml) where T : UIElement => (T)XamlReader.Load(xaml);

    private static Border CardShell() => Themed<Border>(
        $"<Border {XamlNamespace} Background='{{ThemeResource CardBackgroundFillColorDefaultBrush}}' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}' BorderThickness='1' CornerRadius='8'/>");

    private static Border Divider() => Themed<Border>(
        $"<Border {XamlNamespace} Height='1' Background='{{ThemeResource DividerStrokeColorDefaultBrush}}'/>");

    /// <summary>Rows in one rounded card, divided by thin lines.</summary>
    private static Border Card(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) stack.Children.Add(Divider());
            stack.Children.Add(rows[i]);
        }
        var card = CardShell();
        card.Child = stack;
        return card;
    }

    private static StackPanel Section(string title, UIElement body)
    {
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        section.Children.Add(body);
        return section;
    }

    /// <summary>A line of a card: the name (and a note) on the left, the control on the right.</summary>
    private static Grid Row(string title, FrameworkElement control, string? description = null, TextBlock? note = null)
    {
        var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(16, 12, 16, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (description is not null) text.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
        if (note is not null) text.Children.Add(note);
        row.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        AutomationProperties.SetName(control, title);
        return row;
    }

    private Grid ToggleRow(string key, bool isOn, Func<bool, DockSettings> change, Func<DockSettings, bool>? read = null, string? description = null)
    {
        var toggle = new ToggleSwitch { IsOn = isOn, OnContent = T("on"), OffContent = T("off"), MinWidth = 0 };
        // Some changes may be refused (the taskbar could not be hidden, startup could not be set): show what actually holds.
        toggle.Toggled += async (_, _) =>
        {
            await UpdateAsync(change(toggle.IsOn));
            if (read is not null) toggle.IsOn = read(_settings);
        };
        return Row(T(key), toggle, description);
    }

    private (Grid Row, ComboBox Combo) Combo(string key, string[] items, int selected)
    {
        var box = new ComboBox { Width = 270 };
        foreach (var item in items) box.Items.Add(item);
        box.SelectedIndex = selected;
        return (Row(T(key), box), box);
    }

    // Choices that do not collide with common shortcuts; a different value written to settings.json is kept and listed too.
    private static readonly string[] HotKeyChoices = ["Ctrl+Alt+A", "Ctrl+Alt+Enter", "Ctrl+Alt+Home", "Ctrl+Alt+Space", "Ctrl+Shift+Space"];

    private Grid CreateHotKeyChooser()
    {
        var choices = new List<string> { string.Empty };
        choices.AddRange(HotKeyChoices);
        if (!choices.Contains(_settings.LauncherHotKey, StringComparer.OrdinalIgnoreCase)) choices.Add(_settings.LauncherHotKey);
        var box = new ComboBox { Width = 270 };
        foreach (var choice in choices) box.Items.Add(choice.Length == 0 ? T("off") : choice);
        box.SelectedIndex = choices.FindIndex(choice => choice.Equals(_settings.LauncherHotKey, StringComparison.OrdinalIgnoreCase));
        box.SelectionChanged += async (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            await UpdateAsync(_settings with { LauncherHotKey = choices[box.SelectedIndex] });
        };
        _hotKeyStatus = new TextBlock
        {
            Text = T("hotKeyBusy"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Visibility = _hotKeyFailed ? Visibility.Visible : Visibility.Collapsed
        };
        return Row(T("launcherHotKey"), box, note: _hotKeyStatus);
    }

    /// <summary>Tells whether another program already holds the app menu shortcut.</summary>
    public void SetHotKeyStatus(bool failed)
    {
        _hotKeyFailed = failed;
        if (_hotKeyStatus is not null) _hotKeyStatus.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Settings changed in the dock (pins dragged, setting reset): show them and never save a stale copy.</summary>
    public void ShowSettings(DockSettings settings)
    {
        if (settings == _settings) return;
        _settings = settings;
        Build();
    }

    // --- Updates and pins ------------------------------------------------------------------------------------------

    private UIElement CreateUpdatesSection()
    {
        if (PackageIdentity.IsPackaged)
        {
            // The Store keeps its package up to date; the app contacts nothing itself.
            return Card(Row(T("updatesTitle"), new TextBlock(), T("updStore")));
        }
        var feed = new TextBox { Text = _settings.UpdateFeed, PlaceholderText = DockSettings.DefaultUpdateFeed, Width = 300 };
        // Saved when the box is left, not at every key: half a typed address is not a feed.
        feed.LostFocus += async (_, _) =>
        {
            if (feed.Text.Trim() != _settings.UpdateFeed) await UpdateAsync(_settings with { UpdateFeed = feed.Text.Trim() });
        };
        var atStartup = ToggleRow("updateAtStartup", _settings.CheckForUpdatesAtStartup, value => _settings with { CheckForUpdatesAtStartup = value });

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.65, Text = UpdateStatusText() };
        var install = new Button
        {
            Content = string.Format(System.Globalization.CultureInfo.CurrentCulture, T("updateInstall"), _update?.Version),
            Visibility = _update?.Outcome == UpdateOutcome.Available ? Visibility.Visible : Visibility.Collapsed
        };
        var check = new Button { Content = T("updateCheck") };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = install.IsEnabled = false;
            if (feed.Text.Trim() != _settings.UpdateFeed) await UpdateAsync(_settings with { UpdateFeed = feed.Text.Trim() });
            status.Text = T("updChecking");
            _update = await _checkForUpdates();
            Build();
        };
        install.Click += async (_, _) =>
        {
            check.IsEnabled = install.IsEnabled = false;
            status.Text = T("updDownloading");
            if (await _installUpdate()) return;
            _update = new UpdateCheck(UpdateOutcome.Failed);
            Build();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(check);
        buttons.Children.Add(install);
        return Card(
            Row(T("updateFeed"), feed),
            new TextBlock { Text = T("updateFeedHelp"), FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 10, 16, 12) },
            atStartup,
            Row(T("updateCheck"), buttons, note: status));
    }

    private string UpdateStatusText() => _update?.Outcome switch
    {
        UpdateOutcome.NoSource => T("updNoSource"),
        UpdateOutcome.BadSource => T("updBadSource"),
        UpdateOutcome.NotInstalled => T("updNotInstalled"),
        UpdateOutcome.UpToDate => T("updUpToDate"),
        UpdateOutcome.Available => string.Format(System.Globalization.CultureInfo.CurrentCulture, T("updAvailable"), _update.Version),
        UpdateOutcome.Failed => T("updFailed"),
        _ => string.Empty
    };

    private StackPanel CreatePinnedSection()
    {
        var section = new StackPanel { Spacing = 12 };
        var rows = new List<UIElement>();
        if (_settings.Pins.Count == 0)
            rows.Add(new TextBlock { Text = T("pinnedEmpty"), TextWrapping = TextWrapping.Wrap, Opacity = 0.65, Margin = new Thickness(16, 12, 16, 12) });
        for (var i = 0; i < _settings.Pins.Count; i++) rows.Add(CreatePinRow(_settings.Pins[i], i));
        section.Children.Add(Card([.. rows]));
        var import = new Button { Content = T("importTaskbar") };
        import.Click += async (_, _) =>
        {
            import.IsEnabled = false;
            var count = await ImportTaskbarPinsAsync();
            _importResult = string.Format(System.Globalization.CultureInfo.CurrentCulture, T("importDone"), count);
            Build();
        };
        section.Children.Add(import);
        if (_importResult is not null) section.Children.Add(new TextBlock { Text = _importResult, Opacity = 0.65 });
        return section;
    }

    private Grid CreatePinRow(PinnedApp pin, int index)
    {
        var row = new Grid { ColumnSpacing = 4, Padding = new Thickness(16, 8, 8, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var c = 0; c < 3; c++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = pin.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = T(KindKey(pin.Target.Kind)), FontSize = 12, Opacity = 0.65 });
        row.Children.Add(text);
        AddRowButton(row, 1, "", T("moveUp"), pin.DisplayName, index > 0, () => PinList.Insert(_settings.Pins, pin, index - 1));
        AddRowButton(row, 2, "", T("moveDown"), pin.DisplayName, index < _settings.Pins.Count - 1,
            () => PinList.Insert(_settings.Pins, pin, index + 2));
        AddRowButton(row, 3, "", T("unpin"), pin.DisplayName, true, () => PinList.Remove(_settings.Pins, pin.Key));
        return row;
    }

    private void AddRowButton(Grid row, int column, string glyph, string label, string appName, bool enabled, Func<List<PinnedApp>> change)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, IsEnabled = enabled, Padding = new Thickness(8) };
        AutomationProperties.SetName(button, $"{label}: {appName}");
        ToolTipService.SetToolTip(button, label);
        button.Click += async (_, _) =>
        {
            await UpdateAsync(_settings with { Pins = change() });
            Build();
        };
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private static string KindKey(LaunchTargetKind kind) => kind switch
    {
        LaunchTargetKind.Folder => "kindFolder",
        LaunchTargetKind.File => "kindFile",
        _ => "kindApp"
    };

    /// <summary>Adds Windows taskbar pins that are not pinned yet, after the current pins. Returns how many were added.</summary>
    private async Task<int> ImportTaskbarPinsAsync()
    {
        IReadOnlyList<PinnedApp> found;
        try { found = await _readTaskbarPins(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LocalLog.Write("Warning", "taskbar-pin-import", ex);
            return 0;
        }
        var pins = _settings.Pins.ToList();
        var added = 0;
        foreach (var pin in found.Where(p => PinList.IndexOf(pins, p.Key) < 0))
        {
            var before = pins.Count;
            pins = PinList.Insert(pins, pin, pins.Count);
            if (pins.Count > before) added++;
        }
        if (added > 0) await UpdateAsync(_settings with { Pins = pins });
        return added;
    }

    /// <summary>Shows why the taskbar is not hidden; null clears the message.</summary>
    public void SetTaskbarStatus(string? key)
    {
        _taskbarStatus = key;
        if (_statusBar is null) return;
        _statusBar.Message = key is null ? string.Empty : T(key);
        _statusBar.IsOpen = key is not null;
    }

    private InfoBar CreateFirstRunPrompt()
    {
        var replace = new Button { Content = T("replaceNow"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var later = new Button { Content = T("notNow") };
        replace.Click += async (_, _) => await AnswerFirstRunAsync(replace: true);
        later.Click += async (_, _) => await AnswerFirstRunAsync(replace: false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(replace);
        buttons.Children.Add(later);
        var import = new CheckBox { Content = T("importOnFirstRun"), IsChecked = _importOnFirstRun };
        import.Click += (_, _) => _importOnFirstRun = import.IsChecked == true;
        var content = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        content.Children.Add(import);
        content.Children.Add(buttons);
        return new InfoBar
        {
            Severity = InfoBarSeverity.Informational,
            IsOpen = true,
            IsClosable = false,
            Title = T("replacePromptTitle"),
            Message = T("replacePromptText"),
            Content = content
        };
    }

    private async Task AnswerFirstRunAsync(bool replace)
    {
        // Replacing the taskbar defaults to reserving the bottom edge, so maximized windows stop above the dock.
        await UpdateAsync(_settings with
        {
            FirstRunCompleted = true,
            ReplaceTaskbar = replace || _settings.ReplaceTaskbar,
            // The taskbar's clock and quick panels go away with it, so the dock offers them.
            ShowClock = replace || _settings.ShowClock,
            ShowQuickSettings = replace || _settings.ShowQuickSettings,
            ShowNotifications = replace || _settings.ShowNotifications,
            Mode = replace && _settings.Mode == DockMode.Floating ? DockMode.Reserved : _settings.Mode
        });
        if (_importOnFirstRun) await ImportTaskbarPinsAsync();
        _firstRun = false;
        Build();
    }

    private async Task UpdateAsync(DockSettings value)
    {
        _settings = value.Validate();
        var version = ++_changeVersion;
        await _saveGate.WaitAsync();
        try
        {
            if (version != _changeVersion) return;
            var saved = await _save(_settings);
            if (version == _changeVersion) _settings = saved;
        }
        finally { _saveGate.Release(); }
    }
}
