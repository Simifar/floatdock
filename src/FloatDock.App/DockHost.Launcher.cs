using System.Diagnostics;
using FloatDock.Core;
using FloatDock.Windows;

namespace FloatDock.App;

/// <summary>The app menu: one window shared by the docks, opened over the display of the dock that asked for it.</summary>
internal sealed partial class DockHost : ILauncherHost
{
    private const int LauncherHotKeyId = 1;
    private readonly LaunchCounts _launchCounts = new();
    private readonly AppFirstSeen _firstSeen = new();
    private LauncherWindow? _launcherWindow;
    private DockWindow? _launcherOwner;
    private WindowMessageHook? _hotKeys;
    private long _launcherLostFocusAt;
    private bool _hotKeyFailed;

    /// <summary>An app dragged out of the menu, for every dock to accept as a drop.</summary>
    public AppEntry? LauncherDragApp { get; private set; }

    public bool IsLauncherOpen => _launcherWindow is { IsOpen: true };

    /// <summary>Whether the menu is open over this dock's display.</summary>
    public bool IsLauncherOpenOn(DockWindow view) => IsLauncherOpen && ReferenceEquals(_launcherOwner, view);

    private void InitializeLauncher(WindowMessageHook messages)
    {
        _hotKeys = messages;
        messages.HotKeyPressed += id =>
        {
            // Not inside the window procedure: creating a window there would re-enter it.
            var timer = Stopwatch.StartNew();
            if (id == LauncherHotKeyId) DispatcherQueue.TryEnqueue(() => ToggleLauncher(null, timer));
        };
    }

    private void DisposeLauncher()
    {
        _launcherWindow?.Close();
        _launcherWindow = null;
    }

    /// <summary>Opens the app menu over <paramref name="from"/>'s display, or over the pointer's when it is a shortcut; closes it when it is open.</summary>
    public void ToggleLauncher(DockWindow? from) => ToggleLauncher(from, Stopwatch.StartNew());

    private void ToggleLauncher(DockWindow? from, Stopwatch timer)
    {
        if (_closing) return;
        if (IsLauncherOpen)
        {
            _launcherWindow!.Dismiss();
            return;
        }
        // Clicking the dock's own button first takes the focus from the open menu and so closes it; that click must not reopen it.
        if (Environment.TickCount64 - _launcherLostFocusAt < 400) return;
        if (_launcherWindow is null)
        {
            _launcherWindow = new LauncherWindow(this, Icons, _launchCounts);
            _launcherWindow.Dismissed += byFocus =>
            {
                if (byFocus) _launcherLostFocusAt = Environment.TickCount64;
                foreach (var view in _views.ToList()) view.UpdateHitRegion();
                ScheduleHides();
            };
        }
        var dock = from ?? DockAtPointer();
        _launcherOwner = dock;
        dock.ShowDock();
        _launcherWindow.Open(dock.DisplayRect, MenuApps(_catalog.Apps), timer);
        // The dock stays above the dimmed menu, so an app can be dragged onto it.
        WindowChrome.BringToFront(dock.Hwnd);
        dock.UpdateHitRegion();
        _ = _catalog.RefreshIfStaleAsync(TimeSpan.FromMinutes(1));
    }

    private void ApplyLauncherHotKey()
    {
        _hotKeys!.UnregisterHotKey(LauncherHotKeyId);
        _hotKeyFailed = false;
        if (HotKeyGesture.TryParse(_settings.LauncherHotKey, out var gesture) && !_hotKeys.RegisterHotKey(LauncherHotKeyId, gesture))
        {
            _hotKeyFailed = true;
            LocalLog.Write("Warning", "launcher-hotkey-unavailable");
        }
        _settingsWindow?.SetHotKeyStatus(_hotKeyFailed);
    }

    public Task PinCatalogAppAsync(AppEntry app, int index) =>
        PersistAndRenderAsync(_settings with { Pins = PinList.Insert(_settings.Pins, app.ToPin(), index) });

    DockLanguage ILauncherHost.Language => _settings.Language;

    bool ILauncherHost.Animations => AnimationsEnabled;

    LauncherSort ILauncherHost.Sort => _settings.LauncherSort;

    IReadOnlyDictionary<string, long> ILauncherHost.FirstSeen => _firstSeen.Snapshot();

    Task ILauncherHost.HideAsync(AppEntry app) => HideAppAsync(app.Id);

    /// <summary>The catalog without the apps the user hid from the menu.</summary>
    private IReadOnlyList<AppEntry> MenuApps(IReadOnlyList<AppEntry> apps) =>
        _settings.HiddenApps.Count == 0 ? apps : apps.Where(app => !_settings.HiddenApps.Contains(app.Id, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>Hides an app from the menu, or (when it is hidden) shows it again.</summary>
    public async Task HideAppAsync(string id, bool hide = true)
    {
        var hidden = _settings.HiddenApps.Where(existing => !existing.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hide) hidden.Add(id);
        await PersistAndRenderAsync(_settings with { HiddenApps = hidden });
        _launcherWindow?.UpdateApps(MenuApps(_catalog.Apps));
    }

    /// <summary>The name of a catalog app for lists in Settings; the id itself when the app is gone.</summary>
    public string AppName(string id) => _catalog.Apps.FirstOrDefault(app => app.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;

    // The dock window's height (icons at full magnification plus the shelf) and its bottom margin, plus a gap.
    double ILauncherHost.BottomInset => _effective.IconSize * _effective.Magnification + 35 + 12 + 24;

    bool ILauncherHost.IsPinned(AppEntry app) => PinList.IndexOf(_settings.Pins, app.Key) >= 0;

    Task ILauncherHost.PinAsync(AppEntry app) => PinCatalogAppAsync(app, _settings.Pins.Count);

    Task ILauncherHost.UnpinAsync(AppEntry app) => UnpinAsync(app.Key);

    void ILauncherHost.Launch(AppEntry app, bool asAdministrator)
    {
        _launchCounts.Increment(app.Id);
        // Elevation needs the executable itself; the Start menu item cannot be started with another verb.
        if (asAdministrator && app.ExecutablePath is { } path) _launcher.Launch(new LaunchTarget(LaunchTargetKind.Executable, path), asAdministrator: true);
        else _launcher.Launch(app.Target);
    }

    void ILauncherHost.DragStarted(AppEntry app)
    {
        LauncherDragApp = app;
        foreach (var view in _views.ToList()) view.UpdateHitRegion();
    }

    void ILauncherHost.DragEnded()
    {
        LauncherDragApp = null;
        foreach (var view in _views.ToList()) view.UpdateHitRegion();
        ScheduleHides();
    }
}
