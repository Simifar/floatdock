using FloatDock.Core;
using FloatDock.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace FloatDock.App;

/// <summary>
/// The dock on one display: shelf, items, magnification, input region and the AppBar band on its own display.
/// Everything shared between docks lives in <see cref="DockHost"/>.
/// </summary>
public sealed partial class DockWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly AppBarHost _appBar;
    private bool _appBarFullscreen;
    private bool _isHidden;
    private bool _fullscreenHidden;
    private bool _pointerInside;
    private bool _closing;
    private readonly nint _hwnd;
    private (int Left, int Top, int Right, int Bottom)? _hitRegion;
    private bool _hitRegionApplied;
    private int _openMenus;
    private DockLanguage? _renderedLanguage;
    private double _lastScale = 1;

    /// <param name="host">What the docks share.</param>
    /// <param name="displayId">The display this dock is on; null for the dock on the primary display, wherever that is.</param>
    internal DockWindow(DockHost host, long? displayId)
    {
        Host = host;
        DisplayId = displayId;
        InitializeComponent();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        SystemBackdrop = new TransparentBackdrop();
        var hwnd = _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!WindowChrome.RemoveBorder(hwnd)) LocalLog.Write("Warning", "dwm-border-unavailable");
        WindowChrome.HideFromSwitchers(hwnd);
        if (!WindowChrome.EnableClientTransparency(hwnd)) LocalLog.Write("Warning", "dwm-transparency-unavailable");
        Messages = new WindowMessageHook(hwnd);
        _appBar = new AppBarHost(hwnd, Messages.AppBarMessage);
        Messages.AppBarNotification += OnAppBarNotification;
        Closed += (_, _) =>
        {
            _closing = true;
            DisposePreview();
            _frames.StopAll();
            _systemTimer.Stop();
            _hideTimer.Stop();
            _cursorTimer.Stop();
            if (IsAnchor) Host.BeginShutdown();
            _appBar.Dispose();
            Messages.Dispose();
            if (IsAnchor) Host.CompleteShutdown();
            else Host.ViewClosed(this);
        };
        ItemsPanel.KeyDown += OnItemsKeyDown;
        // On the whole shelf, not on the items: the gaps and padding between icons are not part of any item, and leaving the items
        // for a gap must not look like leaving the dock.
        DockSurface.PointerMoved += OnPointerMoved;
        InitializeItems();
        InitializePreview();
        DockSurface.PointerEntered += (_, _) => { _pointerInside = true; ShowDock(); UpdateHitRegion(); };
        DockSurface.PointerExited += (_, _) => { _pointerInside = false; ScheduleHide(); UpdateHitRegion(); ScheduleMagnificationReset(); };
        DockSurface.SizeChanged += (_, _) => UpdateHitRegion();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); HideDock(); };
        _cursorTimer.Tick += (_, _) => CheckCursor();
        Messages.DpiChanged += () => DispatcherQueue.TryEnqueue(OnScaleChanged);
        ((FrameworkElement)Content).Loaded += (_, _) =>
        {
            if (Content.XamlRoot is { } root) root.Changed += (_, _) => { if (Content.XamlRoot?.RasterizationScale != _lastScale) OnScaleChanged(); };
            // A dock created later was placed before its display's scale was known: measure again now that it is shown.
            if (!IsAnchor) DispatcherQueue.TryEnqueue(OnScaleChanged);
        };
        DockSurface.ContextFlyout = CreateDockMenu();
        _renderedLanguage = Settings.Language;
        DockSurface.DragOver += (_, args) =>
        {
            if (Host.LauncherDragApp is not null || args.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                args.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        };
        DockSurface.Drop += async (_, args) =>
        {
            if (Host.LauncherDragApp is { } app) await Host.PinCatalogAppAsync(app, Settings.Pins.Count);
            else if (Host.DragGroup is { } dragged) await Host.PinAtAsync(dragged, Settings.Pins.Count);
            else await Host.PinDroppedFilesAsync(args, Settings.Pins.Count);
        };
    }

    internal DockHost Host { get; }

    /// <summary>The display this dock is on; null for the primary one (the anchor dock).</summary>
    internal long? DisplayId { get; }

    internal bool IsAnchor => DisplayId is null;

    internal WindowMessageHook Messages { get; }

    internal nint Hwnd => _hwnd;

    private DockSettings Settings => Host.Settings;

    /// <summary>This dock's display, fresh each time; null when it has been unplugged.</summary>
    private DisplayInfo? Display => DisplayId is { } id ? ScreenDisplays.Find(id) : ScreenDisplays.Primary();

    /// <summary>The whole area of this dock's display in screen pixels; the primary display when this one is gone.</summary>
    internal ScreenRect DisplayBounds => (Display ?? ScreenDisplays.Primary()).Bounds;

    internal RectInt32 DisplayRect
    {
        get
        {
            var bounds = DisplayBounds;
            return new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }

    internal void Render(IReadOnlyList<AppGroup> groups)
    {
        if (_closing) return;
        var theme = Settings.Theme switch
        {
            DockTheme.Light => ElementTheme.Light,
            DockTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        // Theme only the shelf: a themed window root paints an opaque light background over the transparent window.
        DockSurface.RequestedTheme = theme;
        var light = theme == ElementTheme.Light || (theme == ElementTheme.Default && DockSurface.ActualTheme == ElementTheme.Light);
        var alpha = (byte)Math.Round(255 * Settings.ShelfOpacity / 100.0);
        DockSurface.Background = new SolidColorBrush(light
            ? Microsoft.UI.ColorHelper.FromArgb(alpha, 246, 247, 249)
            : Microsoft.UI.ColorHelper.FromArgb(alpha, 32, 35, 42));
        DockSurface.CornerRadius = new CornerRadius(Settings.ShelfRadius);
        ItemsPanel.Spacing = Settings.Spacing;
        if (_renderedLanguage != Settings.Language)
        {
            DockSurface.ContextFlyout = CreateDockMenu();
            _renderedLanguage = Settings.Language;
        }
        ReconcileItems(groups, light);
        if (Settings.Mode == DockMode.AutoHide) _cursorTimer.Start();
        else
        {
            _cursorTimer.Stop();
            _hideTimer.Stop();
            _isHidden = false;
            _pointerInside = false;
        }
        PositionDock();
        UpdateFullscreen();
        RefreshPreview();
    }

    /// <summary>Icons are fetched at the magnified size on this display so they stay sharp when enlarged.</summary>
    private int IconPixelSize()
    {
        var needed = Settings.IconSize * Math.Max(1, Settings.Magnification) * (Content.XamlRoot?.RasterizationScale ?? 1);
        return new[] { 32, 48, 64, 96, 128, 256 }.FirstOrDefault(size => size >= needed, 256);
    }

    private async Task LoadIconAsync(StackPanel stack, IEnumerable<string?> sources, double? size = null)
    {
        try
        {
            var bytes = await Host.Icons.GetAsync(sources, IconPixelSize());
            if (bytes is null) return;
            stack.Children[0] = new Image
            {
                Source = await IconImages.FromPngAsync(bytes),
                Width = size ?? Settings.IconSize,
                Height = size ?? Settings.IconSize,
                Stretch = Stretch.Uniform
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            LocalLog.Write("Warning", "icon-load", ex);
        }
    }

    private MenuFlyout CreateDockMenu()
    {
        var menu = new MenuFlyout();
        var settings = new MenuFlyoutItem { Text = T("settings") };
        settings.Click += (_, _) => Host.OpenSettings();
        menu.Items.Add(settings);
        var exit = new MenuFlyoutItem { Text = T("exit") };
        exit.Click += (_, _) => Host.Quit();
        menu.Items.Add(exit);
        TrackMenu(menu);
        return menu;
    }

    private void TrackMenu(Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase menu)
    {
        menu.Opened += (_, _) => { _openMenus++; _hideTimer.Stop(); UpdateHitRegion(); };
        menu.Closed += (_, _) =>
        {
            _openMenus = Math.Max(0, _openMenus - 1);
            UpdateHitRegion();
            ScheduleHide();
        };
    }

    internal bool IsCursorAwayFromDock()
    {
        if (Host.Cursor.Position is not { } cursor) return false;
        var margin = (int)(24 * (Content.XamlRoot?.RasterizationScale ?? 1));
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        return cursor.X < position.X - margin || cursor.X > position.X + size.Width + margin ||
            cursor.Y < position.Y - margin || cursor.Y > position.Y + size.Height + margin;
    }

    /// <summary>A display was added, removed, rearranged or changed resolution, or the PC woke up.</summary>
    internal void OnDisplaysChanged()
    {
        if (_closing) return;
        // Previews sit where the old layout was.
        HidePreview();
        PositionDock(reserveAgain: true);
        UpdateFullscreen();
    }

    /// <summary>The display scale changed (the dock moved to another display, or the scale setting was changed).</summary>
    private void OnScaleChanged()
    {
        if (_closing) return;
        _lastScale = Content.XamlRoot?.RasterizationScale ?? 1;
        // Icons are fetched at the pixel size they are shown at, so fetch them again.
        foreach (var item in _items) item.IconKey = string.Empty;
        // Windows may have resized the window itself for the new scale: the next move must not be skipped as "unchanged".
        _dockRectSet = false;
        HidePreview();
        Host.OnDockScaleChanged();
        PositionDock(reserveAgain: true);
    }

    internal void PositionDock(bool reserveAgain = false)
    {
        if (_closing) return;
        // A dock whose display is gone waits to be closed; until then it keeps its place.
        if (Display is not { } display) return;
        var scale = Content.XamlRoot?.RasterizationScale ?? 1;
        _lastScale = scale;
        var width = (int)Math.Ceiling(DockWidth() * scale);
        var height = (int)Math.Ceiling((Settings.IconSize * Settings.Magnification + 35) * scale);
        var bounds = Host.Taskbar.IsHidden ? display.Bounds : display.WorkArea;
        var bottom = bounds.Y + bounds.Height;
        if (Settings.Mode == DockMode.Reserved)
        {
            // Reserve the resting shelf plus its margin; the work area then excludes it, so place the dock by the band.
            var monitor = display.Bounds;
            var reserve = (int)Math.Ceiling((Settings.IconSize + 32 + 12) * scale);
            if (_appBar.Reserve((monitor.X, monitor.Y, monitor.Right, monitor.Bottom), reserve, reserveAgain) is { } band)
                bottom = band.Bottom;
        }
        else _appBar.Unregister();
        var place = DockPlacement.Compute(bounds, bottom, width, height, scale, _isHidden);
        MoveDock(new RectInt32(place.X, place.Y, place.Width, place.Height));
        UpdateHitRegion();
    }

    /// <summary>
    /// Outside the shelf the window is transparent, so clicks there go to the windows underneath.
    /// While the dock is in use the whole window takes input, so magnified icons above the shelf are not clipped.
    /// </summary>
    internal void UpdateHitRegion()
    {
        if (_closing) return;
        var magnified = _items.Any(item => item.Transform is { ScaleX: > 1.001 } || item.Phase != ItemPhase.Rest);
        var inUse = _pointerInside || _openMenus > 0 || Host.DragKey is not null || Host.LauncherDragApp is not null ||
            Host.IsLauncherOpenOn(this) || IsPreviewOpen || magnified;
        var size = AppWindow.Size;
        var scale = Content.XamlRoot?.RasterizationScale ?? 1;
        var shelf = (int)Math.Ceiling(DockSurface.ActualHeight * scale);
        var shelfWidth = (int)Math.Ceiling(DockSurface.ActualWidth * scale);
        var left = Math.Max(0, (size.Width - shelfWidth) / 2);
        (int, int, int, int)? area = inUse || shelf <= 0 || shelf >= size.Height || shelfWidth <= 0
            ? null
            : (left, size.Height - shelf, Math.Min(size.Width, left + shelfWidth), size.Height);
        if (_hitRegionApplied && area == _hitRegion) return;
        _hitRegionApplied = true;
        _hitRegion = area;
        WindowChrome.SetHitRegion(_hwnd, area);
    }

    internal void ShowDock()
    {
        _hideTimer.Stop();
        if (_isHidden) { _isHidden = false; PositionDock(); }
    }

    private void CheckCursor()
    {
        if (_fullscreenHidden || Settings.Mode != DockMode.AutoHide) return;
        var cursor = Host.Cursor.Position;
        if (cursor is null) return;
        // The target place, not the current one: the window may still be sliding.
        var bottom = _dockRect.Y + (_isHidden ? Math.Min(18, _dockRect.Height) : _dockRect.Height);
        var inside = cursor.Value.X >= _dockRect.X && cursor.Value.X < _dockRect.X + _dockRect.Width &&
            cursor.Value.Y >= _dockRect.Y && cursor.Value.Y < bottom;
        if (inside)
        {
            _pointerInside = true;
            ShowDock();
        }
        else if (_pointerInside || (!_isHidden && !_hideTimer.IsEnabled))
        {
            _pointerInside = false;
            ScheduleHide();
        }
    }

    internal void ScheduleHide()
    {
        if (!_closing && Settings.Mode == DockMode.AutoHide) _hideTimer.Start();
    }

    private void HideDock()
    {
        if (Settings.Mode != DockMode.AutoHide || _pointerInside || _openMenus > 0 || Host.DragKey is not null || Host.LauncherDragApp is not null ||
            Host.IsLauncherOpenOn(this) || IsPreviewOpen || Host.IsSettingsOpen) return;
        _isHidden = true;
        PositionDock();
    }

    private void OnAppBarNotification(int code, nint lParam)
    {
        if (code == AppBarHost.NotifyFullscreenApp)
        {
            _appBarFullscreen = lParam != 0;
            UpdateFullscreen();
        }
        else if ((code is AppBarHost.NotifyPositionChanged or AppBarHost.NotifyStateChange) && _appBar.IsRegistered)
        {
            // The taskbar or another AppBar moved: ask again where the band may go.
            if (Display is not { } display) return;
            var area = display.Bounds;
            var scale = Content.XamlRoot?.RasterizationScale ?? 1;
            _appBar.Reserve((area.X, area.Y, area.Right, area.Bottom),
                (int)Math.Ceiling((Settings.IconSize + 32 + 12) * scale), force: true);
            PositionDock();
        }
    }

    internal void UpdateFullscreen()
    {
        if (_closing) return;
        var bounds = DisplayBounds;
        // Three witnesses: the AppBar notification (Reserved mode), a foreground window that covers the display, and the Shell's own
        // verdict, which also knows exclusive full-screen games and presentations.
        var fullscreen = Settings.HideInFullscreen && (_appBarFullscreen || Host.Fullscreen.IsForegroundFullscreen(bounds) || ShellSaysFullscreenHere(bounds));
        if (fullscreen && !_fullscreenHidden)
        {
            _fullscreenHidden = true;
            AppWindow.Hide();
        }
        else if (!fullscreen && _fullscreenHidden)
        {
            _fullscreenHidden = false;
            AppWindow.Show(false);
        }
    }

    /// <summary>
    /// The Shell's verdict is not per display: with several docks it counts only for the display the foreground window is on.
    /// </summary>
    private bool ShellSaysFullscreenHere(ScreenRect bounds) =>
        FullscreenDetector.ShellSaysFullscreen() &&
        (!Host.HasSeveralDocks || FullscreenDetector.ForegroundCenter() is not { } center || bounds.Contains(center.X, center.Y));

    private string T(string key) => UiText.Get(key, Settings.Language);
}
