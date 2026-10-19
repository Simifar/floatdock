using FloatDock.Core;
using FloatDock.Windows;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace FloatDock.App;

internal enum DockItemKind { Apps, App, Separator, Hint, Downloads, RecycleBin, ShowDesktop, Clock, QuickSettings, Notifications }

/// <summary>One element of the dock row. Elements live as long as their key does; only their state is updated.</summary>
internal sealed class DockItem
{
    public required string Key { get; init; }
    public required DockItemKind Kind { get; init; }
    public required FrameworkElement Element { get; init; }
    /// <summary>Set for items that grow under the pointer.</summary>
    public CompositeTransform? Transform { get; init; }
    public StackPanel? Stack { get; init; }
    public StackPanel? Indicators { get; init; }
    public Button? Button => Element as Button;
    public AppGroup? Group { get; set; }
    public string IconKey { get; set; } = string.Empty;
    public string IndicatorKey { get; set; } = string.Empty;
    public double TargetScale { get; set; } = 1;
    public double TargetOffset { get; set; }
    /// <summary>True until the item has been through one reconcile; decides whether it may animate in.</summary>
    public bool Fresh { get; set; } = true;
    public ItemPhase Phase { get; set; }
    public double PhaseElapsed { get; set; }
    public double Presence { get; set; } = 1;
    public double StartPresence { get; set; }
    /// <summary>The width the item has at rest; its element is narrower while it enters or leaves.</summary>
    public double FullWidth { get; set; }
    /// <summary>Scales the icon with its presence; exists only while the item enters or leaves.</summary>
    public ScaleTransform? Inner { get; set; }
    /// <summary>Stopwatch ticks of the click that started the app, 0 when it is not starting.</summary>
    public long LaunchingSince { get; set; }
}

/// <summary>Rest, or moving toward being there (Entering) or gone (Leaving); a leaving item stays in the row until it has shrunk away.</summary>
internal enum ItemPhase { Rest, Entering, Leaving }

/// <summary>The dock row: building and updating items, magnification that moves neighbours aside, and sliding.</summary>
public sealed partial class DockWindow
{
    private double ItemSpacing => Settings.Spacing;
    private const double SlideSeconds = 0.2;
    private const double MagnifySeconds = 0.03;
    private const double ItemMotionSeconds = 0.2;
    private const double LaunchPulseSeconds = 1.2;
    private const double LaunchTimeoutSeconds = 10;
    // The pointer may cross a gap that lies outside every item (above the shelf, between two enlarged icons) in a few milliseconds:
    // only leaving for longer than this counts as leaving, or the icons would dip at every gap.
    private const double ExitGraceSeconds = 0.09;
    private long _magnifyExitAt;
    private bool _magnifying;
    private readonly FrameLoop _frames = new();
    private Func<double, bool> _stepMagnify = null!;
    private Func<double, bool> _stepSlide = null!;
    private Func<double, bool> _stepItems = null!;
    private bool _rendered;
    private readonly List<DockItem> _items = [];
    private readonly Dictionary<string, DockItem> _views = new(StringComparer.Ordinal);
    private string _renderSignature = string.Empty;
    private bool _light;
    private readonly DispatcherTimer _systemTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private RectInt32 _dockRect;
    private bool _dockRectSet;
    private RectInt32 _slideFrom;
    private double _slideElapsed;
    private bool _slideActive;

    private bool AnimationsEnabled => Host.AnimationsEnabled;

    private void InitializeItems()
    {
        _stepMagnify = StepMagnification;
        _stepSlide = StepSlide;
        _stepItems = StepItems;
        _systemTimer.Tick += (_, _) => RefreshSystemItems();
    }

    /// <summary>Brings the row in line with the groups: new items are built, existing ones updated, stale ones dropped.</summary>
    private void ReconcileItems(IReadOnlyList<AppGroup> groups, bool light)
    {
        _light = light;
        var signature = string.Join('|', Settings.IconSize, Settings.Language, light, Settings.ShowAppsButton, Settings.ShowSeparator,
            Settings.ShowRecycleBin, Settings.ShowDownloads, Settings.ShowDesktopButton, Settings.ShowClock,
            Settings.ShowQuickSettings, Settings.ShowNotifications);
        var rebuilt = signature != _renderSignature;
        if (rebuilt)
        {
            // Icon size, language, theme and which items exist change how every item looks: start over.
            _renderSignature = signature;
            ItemsPanel.Children.Clear();
            _views.Clear();
            _items.Clear();
            _frames.Stop(_stepItems);
            StopMagnification();
            CloseSystemFlyouts();
        }
        // The first render and a rebuild show the row at once; later arrivals and departures of apps move.
        var animate = AnimationsEnabled && _rendered && !rebuilt;
        _rendered = true;
        var wanted = new List<DockItem>();
        if (Settings.ShowAppsButton) wanted.Add(View("apps", () => CreateTile("apps", DockItemKind.Apps, "", T("apps"), ToggleLauncher)));
        var pinned = groups.Count(group => group.IsPinned);
        for (var i = 0; i < groups.Count; i++)
        {
            if (Settings.ShowSeparator && pinned > 0 && i == pinned) wanted.Add(View("sep:apps", () => CreateSeparator("sep:apps")));
            var item = View("app:" + groups[i].Key, () => CreateAppItem("app:" + groups[i].Key));
            UpdateAppItem(item, groups[i]);
            wanted.Add(item);
        }
        if (groups.Count == 0) wanted.Add(View("hint", CreateHint));
        AppendSystemItems(wanted);

        var keys = wanted.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var previous = _items.ToList();
        // Apps that are gone shrink away and stay in the row until they have; an app that came back is a new item.
        var leaving = previous.Where(item => item.Phase == ItemPhase.Leaving).ToList();
        foreach (var stale in _views.Where(pair => !keys.Contains(pair.Key)).ToList())
        {
            _views.Remove(stale.Key);
            if (!animate || stale.Value.Kind != DockItemKind.App || stale.Value.Phase == ItemPhase.Leaving) continue;
            BeginPhase(stale.Value, ItemPhase.Leaving);
            leaving.Add(stale.Value);
        }
        foreach (var item in wanted)
        {
            if (!item.Fresh) continue;
            item.Fresh = false;
            if (animate && item.Kind == DockItemKind.App) BeginPhase(item, ItemPhase.Entering);
        }

        // A leaving item keeps the place after the item that used to precede it.
        var row = new List<DockItem>(wanted);
        foreach (var gone in leaving.OrderBy(previous.IndexOf))
        {
            var at = previous.IndexOf(gone);
            while (--at >= 0 && !row.Contains(previous[at])) { }
            row.Insert(at + 1, gone);
        }
        for (var i = 0; i < row.Count; i++)
        {
            var element = row[i].Element;
            if (i < ItemsPanel.Children.Count && ReferenceEquals(ItemsPanel.Children[i], element)) continue;
            ItemsPanel.Children.Remove(element);
            ItemsPanel.Children.Insert(Math.Min(i, ItemsPanel.Children.Count), element);
        }
        while (ItemsPanel.Children.Count > row.Count) ItemsPanel.Children.RemoveAt(ItemsPanel.Children.Count - 1);
        _items.Clear();
        _items.AddRange(row);
    }

    // --- Arriving, leaving and starting ----------------------------------------------------------------------------

    /// <summary>
    /// An item enters or leaves by growing or shrinking from nothing: its element's width, margin (which cancels the row's
    /// spacing), opacity and icon scale follow one presence value, so neighbours and the shelf slide along without a jump.
    /// </summary>
    private void BeginPhase(DockItem item, ItemPhase phase)
    {
        if (item.Phase == ItemPhase.Rest)
        {
            item.FullWidth = ItemWidth(item);
            item.Presence = 1;
            if (item.Stack is { } stack)
            {
                item.Inner = new ScaleTransform();
                stack.RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 1);
                stack.RenderTransform = item.Inner;
            }
        }
        item.Phase = phase;
        item.PhaseElapsed = 0;
        item.StartPresence = phase == ItemPhase.Entering && item.Presence >= 1 ? 0 : item.Presence;
        ApplyPresence(item, item.StartPresence);
        _frames.Run(_stepItems);
    }

    private void ApplyPresence(DockItem item, double presence)
    {
        item.Presence = presence;
        item.Element.Width = Math.Max(0, item.FullWidth * presence);
        item.Element.Opacity = presence;
        var margin = -ItemSpacing / 2 * (1 - presence);
        item.Element.Margin = new Thickness(margin, 0, margin, 0);
        if (item.Inner is { } inner) inner.ScaleX = inner.ScaleY = Math.Max(0.01, presence);
    }

    private void FinishPhase(DockItem item)
    {
        if (item.Phase == ItemPhase.Leaving)
        {
            ItemsPanel.Children.Remove(item.Element);
            _items.Remove(item);
            return;
        }
        item.Phase = ItemPhase.Rest;
        item.Presence = 1;
        item.Element.Width = item.FullWidth;
        item.Element.Opacity = 1;
        item.Element.Margin = new Thickness(0);
        if (item.Stack is { } stack) stack.RenderTransform = null;
        item.Inner = null;
    }

    /// <summary>The icon of an app that was just clicked pulses softly until its window appears (or ten seconds pass).</summary>
    private void BeginLaunching(DockItem item)
    {
        if (!AnimationsEnabled || item.Stack is null) return;
        item.LaunchingSince = System.Diagnostics.Stopwatch.GetTimestamp();
        _frames.Run(_stepItems);
    }

    private static void EndLaunching(DockItem item)
    {
        item.LaunchingSince = 0;
        if (item.Stack is { } stack) stack.Opacity = 1;
    }

    private bool StepItems(double seconds)
    {
        var active = false;
        var finished = false;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var item in _items.ToArray())
        {
            if (item.Phase != ItemPhase.Rest)
            {
                item.PhaseElapsed += seconds;
                var progress = Motion.Progress(item.PhaseElapsed, ItemMotionSeconds);
                var entering = item.Phase == ItemPhase.Entering;
                var eased = entering ? Motion.OutCubic(progress) : 1 - Motion.OutCubic(1 - progress);
                if (progress < 1)
                {
                    ApplyPresence(item, Motion.Lerp(item.StartPresence, entering ? 1 : 0, eased));
                    active = true;
                }
                else
                {
                    FinishPhase(item);
                    finished = true;
                }
            }
            if (item.LaunchingSince == 0) continue;
            var launching = (now - item.LaunchingSince) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (launching >= LaunchTimeoutSeconds || item.Group is { IsRunning: true }) EndLaunching(item);
            else
            {
                item.Stack!.Opacity = 0.7 + 0.3 * Math.Cos(2 * Math.PI * launching / LaunchPulseSeconds);
                active = true;
            }
        }
        if (finished)
        {
            // A departed item no longer holds the window wide.
            PositionDock();
            UpdateHitRegion();
        }
        return active;
    }

    private DockItem View(string key, Func<DockItem> create)
    {
        if (_views.TryGetValue(key, out var existing)) return existing;
        var item = create();
        _views[key] = item;
        return item;
    }

    private DockItem CreateButtonItem(string key, DockItemKind kind, UIElement icon, string name)
    {
        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(icon);
        // Segments under the icon: one per window up to three; filled in by UpdateIndicators.
        var indicators = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Height = 3, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(indicators);
        var transform = new CompositeTransform();
        var button = new Button
        {
            Content = stack,
            Width = Settings.IconSize + 10,
            Height = Settings.IconSize + 16,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 1),
            RenderTransform = transform
        };
        ToolTipService.SetToolTip(button, name);
        AutomationProperties.SetName(button, name);
        return new DockItem { Key = key, Kind = kind, Element = button, Transform = transform, Stack = stack, Indicators = indicators };
    }

    /// <summary>A rounded tile with a glyph, for the buttons that are not apps.</summary>
    private DockItem CreateTile(string key, DockItemKind kind, string glyph, string name, Action click)
    {
        var size = Settings.IconSize;
        var tile = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.22),
            Background = new SolidColorBrush(_light ? ColorHelper.FromArgb(28, 0, 0, 0) : ColorHelper.FromArgb(40, 255, 255, 255)),
            Child = new FontIcon { Glyph = glyph, FontSize = Math.Round(size * 0.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var item = CreateButtonItem(key, kind, tile, name);
        item.Button!.Click += (_, _) => click();
        AutomationProperties.SetAutomationId(item.Button, "FloatDock." + key);
        return item;
    }

    private DockItem CreateSeparator(string key) => new()
    {
        Key = key,
        Kind = DockItemKind.Separator,
        Element = new Border
        {
            Width = 1,
            Height = Settings.IconSize * 0.7,
            Margin = new Thickness(2, 0, 2, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(_light ? ColorHelper.FromArgb(50, 0, 0, 0) : ColorHelper.FromArgb(60, 255, 255, 255))
        }
    };

    private DockItem CreateHint() => new()
    {
        Key = "hint",
        Kind = DockItemKind.Hint,
        Element = new TextBlock { Text = T("empty"), Width = 280, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) }
    };

    private DockItem CreateAppItem(string key)
    {
        var placeholder = new FontIcon { Glyph = "", FontSize = Math.Round(Settings.IconSize * 0.55), HorizontalAlignment = HorizontalAlignment.Center };
        var item = CreateButtonItem(key, DockItemKind.App, placeholder, string.Empty);
        var button = item.Button!;
        button.AllowDrop = true;
        button.Click += (_, _) => OnItemClick(item);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) =>
        {
            if (args.GetCurrentPoint(button).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonReleased) return;
            args.Handled = true;
            OpenNewInstance(item);
        }), handledEventsToo: true);
        button.DragStarting += (_, args) =>
        {
            if (item.Group is not { } group) return;
            HidePreview();
            Host.BeginDrag(group, this);
            args.Data.SetText(group.Key);
        };
        button.DropCompleted += async (_, args) =>
        {
            var dragged = Host.EndDrag();
            // Letting go of a pinned icon away from the dock removes it, as on macOS.
            if (dragged is { IsPinned: true } && args.DropResult == global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None &&
                Host.IsCursorAwayFromDocks())
                await Host.UnpinAsync(dragged.Key);
        };
        button.DragOver += (_, args) =>
        {
            if (Host.LauncherDragApp is not null) args.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            else if (Host.DragGroup is not null) args.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            else if (args.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                args.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        };
        button.Drop += async (_, args) =>
        {
            // Dropping on an icon puts the new or moved pin in front of it; on a running app, after the pins.
            var group = item.Group;
            var index = group is { IsPinned: true } ? PinList.IndexOf(Settings.Pins, group.Key) : Settings.Pins.Count;
            if (Host.LauncherDragApp is { } app) await Host.PinCatalogAppAsync(app, index);
            else if (Host.DragGroup is { } dragged) await Host.PinAtAsync(dragged, index);
            else await Host.PinDroppedFilesAsync(args, index);
        };
        var menu = new MenuFlyout();
        TrackMenu(menu);
        menu.Opening += (_, _) => BuildItemMenu(menu, item);
        button.ContextFlyout = menu;
        button.PointerEntered += (_, _) => OnItemPointerEntered(item);
        button.PointerExited += (_, _) => OnItemPointerExited();
        return item;
    }

    private void UpdateAppItem(DockItem item, AppGroup group)
    {
        item.Group = group;
        var button = item.Button!;
        var windows = group.Windows.Count;
        ToolTipService.SetToolTip(button, windows > 1 ? $"{group.DisplayName} ({windows})" : group.DisplayName);
        AutomationProperties.SetName(button, group.DisplayName + (group.IsRunning ? ", " + T("running") : string.Empty));
        AutomationProperties.SetHelpText(button, windows > 1 ? string.Format(System.Globalization.CultureInfo.CurrentCulture, T("windowsCount"), windows) : string.Empty);
        button.CanDrag = group.IsPinned || group.LaunchTarget is not null;
        UpdateIndicators(item, group);
        var sources = Host.IconSources(group).Where(source => !string.IsNullOrWhiteSpace(source)).ToList();
        var iconKey = string.Join('|', sources);
        if (iconKey == item.IconKey) return;
        item.IconKey = iconKey;
        _ = LoadIconAsync(item.Stack!, sources);
    }

    /// <summary>One segment per window up to three; wide when the app is active, orange when it asks for attention.</summary>
    private void UpdateIndicators(DockItem item, AppGroup group)
    {
        var count = group.IsRunning ? Math.Clamp(group.Windows.Count, 1, 3) : 0;
        var key = $"{count}|{group.IsActive}|{group.NeedsAttention}|{_light}";
        if (key == item.IndicatorKey) return;
        item.IndicatorKey = key;
        var holder = item.Indicators!;
        holder.Children.Clear();
        var color = group.NeedsAttention ? Colors.Orange : _light ? Colors.DimGray : group.IsActive ? Colors.White : Colors.LightGray;
        for (var i = 0; i < count; i++)
            holder.Children.Add(new Border
            {
                Width = group.IsActive ? (count == 1 ? 16 : 10) : 6,
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(color)
            });
    }

    private void OnItemClick(DockItem item)
    {
        if (item.Group is not { } group) return;
        if (IsKeyDown(VirtualKey.Shift) && group.LaunchTarget is not null)
        {
            OpenNewInstance(item);
            return;
        }
        HidePreview();
        if (group.Windows.Count > 1) ShowPreview(item, sticky: true);
        else if (group.Windows.Count == 1) Host.Commands.ActivateOrMinimize(group.Windows[0].Handle, Host.Tracker.LastExternalForeground);
        else if (group.LaunchTarget is { Kind: LaunchTargetKind.Folder } folder && Directory.Exists(folder.Value)) ShowFolderStack(item.Button!, folder.Value, group.DisplayName);
        else if (group.LaunchTarget is not null)
        {
            BeginLaunching(item);
            Host.StartTarget(group.LaunchTarget);
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OpenNewInstance(DockItem item)
    {
        if (item.Group?.LaunchTarget is { } target) Host.StartTarget(target);
    }

    private void BuildItemMenu(MenuFlyout menu, DockItem item)
    {
        menu.Items.Clear();
        if (item.Group is not { } group) return;
        foreach (var window in group.Windows)
        {
            var entry = new MenuFlyoutItem { Text = window.Title };
            entry.Click += (_, _) => Host.Commands.ActivateOrMinimize(window.Handle, Host.Tracker.LastExternalForeground);
            menu.Items.Add(entry);
        }
        if (group.Windows.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        if (group.LaunchTarget is { } target)
        {
            var fresh = new MenuFlyoutItem { Text = T("newWindow") };
            fresh.Click += (_, _) => Host.StartTarget(target);
            menu.Items.Add(fresh);
        }
        var pin = new MenuFlyoutItem { Text = group.IsPinned ? T("unpin") : T("pin") };
        pin.Click += async (_, _) => await Host.TogglePinAsync(group);
        menu.Items.Add(pin);
        if (RevealPath(group) is { } path)
        {
            var isFolder = group.LaunchTarget is { Kind: LaunchTargetKind.Folder };
            var reveal = new MenuFlyoutItem { Text = isFolder ? T("openFolder") : T("reveal") };
            reveal.Click += (_, _) => { if (isFolder) ShellActions.OpenFolder(path); else ShellActions.RevealInExplorer(path); };
            menu.Items.Add(reveal);
        }
        if (group.Windows.Count > 0)
        {
            var close = new MenuFlyoutItem { Text = group.Windows.Count > 1 ? T("closeAll") : T("close") };
            close.Click += (_, _) => { foreach (var window in group.Windows) Host.Commands.Close(window.Handle); };
            menu.Items.Add(close);
        }
    }

    /// <summary>The file or folder to show in Explorer for an item: its running executable, else the pin's.</summary>
    private string? RevealPath(AppGroup group)
    {
        if (group.ExecutablePath is { } running) return running;
        var pin = Settings.Pins.FirstOrDefault(p => p.Key.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
        if (pin?.ExecutablePath is { } pinned) return pinned;
        return group.LaunchTarget is { Kind: LaunchTargetKind.Executable or LaunchTargetKind.Folder or LaunchTargetKind.File } target ? target.Value : null;
    }

    // --- Magnification -------------------------------------------------------------------------------------------

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!AnimationsEnabled)
        {
            ResetMagnification();
            return;
        }
        SetMagnificationTargets(args.GetCurrentPoint(ItemsPanel).Position.X);
    }

    private void SetMagnificationTargets(double x)
    {
        _magnifyExitAt = 0;
        var widths = _items.Select(ItemWidth).ToList();
        var lefts = DockLayout.Lefts(widths, ItemSpacing);
        var scales = new double[_items.Count];
        for (var i = 0; i < scales.Length; i++)
            scales[i] = _items[i].Transform is null ? 1 : DockLayout.Scale(x, lefts[i] + widths[i] / 2, Settings.IconSize, Settings.Magnification);
        var offsets = DockLayout.Offsets(widths, scales);
        for (var i = 0; i < scales.Length; i++)
        {
            _items[i].TargetScale = scales[i];
            _items[i].TargetOffset = offsets[i];
        }
        if (_items.Count > 0) StartMagnification();
    }

    /// <summary>The pointer left the shelf: the icons return to rest unless it comes back within the grace time.</summary>
    private void ScheduleMagnificationReset()
    {
        if (!AnimationsEnabled || _items.Count == 0)
        {
            ResetMagnification();
            return;
        }
        _magnifyExitAt = System.Diagnostics.Stopwatch.GetTimestamp();
        StartMagnification();
    }

    private void ResetMagnification()
    {
        _magnifyExitAt = 0;
        foreach (var item in _items)
        {
            item.TargetScale = 1;
            item.TargetOffset = 0;
            if (AnimationsEnabled || item.Transform is null) continue;
            item.Transform.ScaleX = item.Transform.ScaleY = 1;
            item.Transform.TranslateX = 0;
        }
        if (!AnimationsEnabled)
        {
            StopMagnification();
            UpdateHitRegion();
        }
        else if (_items.Count > 0) StartMagnification();
    }

    /// <summary>
    /// Follows the pointer once per rendered frame (see <see cref="FrameLoop"/>; a DispatcherTimer was restarted by every pointer
    /// event and so froze the icons while the pointer moved). The step is by elapsed time, so it is the same at any frame rate.
    /// </summary>
    private void StartMagnification()
    {
        if (_magnifying) return;
        _magnifying = true;
        _frames.Run(_stepMagnify);
    }

    private void StopMagnification()
    {
        if (!_magnifying) return;
        _frames.Stop(_stepMagnify);
        EndMagnification();
    }

    private void EndMagnification()
    {
        _magnifying = false;
        _magnifyExitAt = 0;
    }

    private bool StepMagnification(double seconds)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_magnifyExitAt != 0 && (now - _magnifyExitAt) / (double)System.Diagnostics.Stopwatch.Frequency >= ExitGraceSeconds)
        {
            foreach (var item in _items)
            {
                item.TargetScale = 1;
                item.TargetOffset = 0;
            }
            _magnifyExitAt = 0;
        }
        // Share of the remaining distance covered this frame; the time constant is MagnifySeconds.
        var step = Motion.Follow(seconds, MagnifySeconds);
        var moving = _magnifyExitAt != 0;
        foreach (var item in _items)
        {
            if (item.Transform is not { } transform) continue;
            var scale = Motion.Approach(transform.ScaleX, item.TargetScale, step, 0.005);
            var offset = Motion.Approach(transform.TranslateX, item.TargetOffset, step, 0.25);
            if (scale != item.TargetScale || offset != item.TargetOffset) moving = true;
            transform.ScaleX = transform.ScaleY = scale;
            transform.TranslateX = offset;
        }
        if (moving) return true;
        EndMagnification();
        UpdateHitRegion();
        return false;
    }

    private void OnItemsKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var buttons = _items.Where(item => item.Button is not null && item.Phase != ItemPhase.Leaving).Select(item => item.Button!).ToList();
        if (buttons.Count == 0) return;
        var direction = args.Key switch
        {
            VirtualKey.Left => -1,
            VirtualKey.Right => 1,
            _ => 0
        };
        if (direction == 0) return;
        var current = buttons.FindIndex(button => button.FocusState != FocusState.Unfocused);
        var next = current < 0 ? 0 : Math.Clamp(current + direction, 0, buttons.Count - 1);
        buttons[next].Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    // --- Size and place --------------------------------------------------------------------------------------------

    private static double ItemWidth(DockItem item) => item.Phase != ItemPhase.Rest ? Math.Max(0, item.Element.Width) :
        double.IsFinite(item.Element.Width) && item.Element.Width > 0 ? item.Element.Width : 64;

    /// <summary>The width an item ends up with, or keeps until it has left.</summary>
    private static double RestWidth(DockItem item) => item.Phase != ItemPhase.Rest ? item.FullWidth : ItemWidth(item);

    /// <summary>The window is wider than the shelf by what magnified icons spread beyond its ends.</summary>
    private double DockWidth()
    {
        var content = _items.Sum(RestWidth) + Math.Max(0, _items.Count - 1) * ItemSpacing + 24;
        var overflow = Settings.Magnification > 1 ? Math.Ceiling(1.2 * (Settings.IconSize + 10) * (Settings.Magnification - 1)) : 0;
        return Math.Max(content, 80) + 2 * overflow;
    }

    /// <summary>Places the dock; in Auto-hide mode, moving only up or down slides it.</summary>
    private void MoveDock(RectInt32 target)
    {
        if (_dockRectSet && !_slideActive && target.Equals(_dockRect)) return;
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var slide = _dockRectSet && Settings.Mode == DockMode.AutoHide && AnimationsEnabled && !_closing &&
            size.Width == target.Width && size.Height == target.Height && position.X == target.X && position.Y != target.Y;
        _dockRect = target;
        _dockRectSet = true;
        if (!slide)
        {
            _frames.Stop(_stepSlide);
            _slideActive = false;
            AppWindow.MoveAndResize(target);
            return;
        }
        _slideFrom = new RectInt32(position.X, position.Y, size.Width, size.Height);
        _slideElapsed = 0;
        _slideActive = true;
        _frames.Run(_stepSlide);
    }

    private bool StepSlide(double seconds)
    {
        _slideElapsed += seconds;
        var progress = Motion.Progress(_slideElapsed, SlideSeconds);
        if (progress >= 1)
        {
            _slideActive = false;
            AppWindow.MoveAndResize(_dockRect);
            UpdateHitRegion();
            return false;
        }
        var y = Motion.Lerp(_slideFrom.Y, _dockRect.Y, Motion.OutCubic(progress));
        AppWindow.Move(new PointInt32(_dockRect.X, (int)Math.Round(y)));
        return true;
    }
}
