using FloatDock.Core;
using FloatDock.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FloatDock.App;

/// <summary>
/// What every dock shares: settings and pins, the window tracker, the app catalog and icons, the Windows taskbar and its
/// Guardian, the app menu, the settings window and the display changes. A <see cref="DockWindow"/> is only the view of one
/// dock on one display. The dock on the primary display (the anchor) always exists and carries the shell hooks and shortcuts;
/// docks for the other displays (see <see cref="DockSettings.Displays"/>) come and go with the displays.
/// </summary>
internal sealed partial class DockHost
{
    private readonly SettingsStore _store = new();
    private readonly StartupManager _startupManager = new();
    private readonly DisplayEvents _displayEvents = new();
    private readonly TaskbarPinImporter _pinImporter;
    private readonly AppCatalogService _catalog;
    private readonly LaunchService _launcher;
    private readonly UpdateService _updates = new();
    private readonly bool _safeMode;
    private readonly bool _disableTaskbarReplacement;
    private readonly List<DockWindow> _views = [];
    private DockWindow? _anchor;
    private Dictionary<string, AppEntry> _appsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _descriptions = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _catalogIds = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<WindowInfo> _windows = [];
    private IReadOnlyList<AppGroup> _groups = [];
    private DockSettings _settings = new();
    private GuardianSession? _guardian;
    private SettingsWindow? _settingsWindow;
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    // Fallback only: Explorer show events and TaskbarCreated re-hide immediately.
    private readonly DispatcherTimer _taskbarTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly SemaphoreSlim _taskbarGate = new(1, 1);
    private string? _taskbarStatus;
    private bool _guardianRestarted;
    private bool _sessionEnding;
    private bool _firstRunPromptShown;
    private bool _closing;
    private bool _systemAnimationsEnabled = true;

    public DockHost(bool safeMode, bool disableTaskbarReplacement)
    {
        _safeMode = safeMode;
        _disableTaskbarReplacement = disableTaskbarReplacement || PackageIdentity.IsPackaged;
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Icons = new IconService(ShellWorker);
        _launcher = new LaunchService(IsLaunchable);
        _pinImporter = new TaskbarPinImporter(ShellWorker);
        _catalog = new AppCatalogService(ShellWorker);
        _catalog.Changed += apps => DispatcherQueue.TryEnqueue(() =>
        {
            var byKey = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in apps) byKey.TryAdd(app.Key, app);
            // Apps registered under an AppUserModelID whose windows do not carry it are found by executable, but only
            // when one entry uses that executable: shortcuts that start explorer.exe or a launcher with arguments do not name it.
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var single in apps.Where(app => app.ExecutablePath is not null).GroupBy(app => DockModel.PathKey(app.ExecutablePath!))
                         .Where(group => group.Count() == 1).Select(group => group.First()))
                if (!single.ExecutablePath!.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                    byKey.TryAdd(DockModel.PathKey(single.ExecutablePath), single);
            _appsByKey = byKey;
            _catalogIds = apps.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Render();
            _firstSeen.Observe(apps.Select(app => app.Id), DateTimeOffset.UtcNow);
            _launcherWindow?.UpdateApps(MenuApps(apps));
        });
        Taskbar.TaskbarShown += () => { if (Taskbar.EnsureHidden()) PositionDocks(reserveAgain: true); };
        Tracker.SnapshotChanged += windows => DispatcherQueue.TryEnqueue(() =>
        {
            _windows = windows;
            Render();
        });
        _taskbarTimer.Tick += (_, _) =>
        {
            if (Taskbar.EnsureHidden()) PositionDocks(reserveAgain: true);
        };
        // Display changes arrive in bursts (a monitor plugged in changes several things): act once, a moment after the last.
        _displayTimer.Tick += (_, _) =>
        {
            _displayTimer.Stop();
            OnDisplaysChanged();
        };
        _displayEvents.Changed += () => DispatcherQueue.TryEnqueue(() =>
        {
            _displayTimer.Stop();
            _displayTimer.Start();
        });
        // A full-screen app often is not a window the dock lists, so its arrival is not a snapshot change.
        // The Shell's verdict can lag the foreground change a little, hence the second look.
        _fullscreenTimer.Tick += (_, _) =>
        {
            _fullscreenTimer.Stop();
            UpdateFullscreen();
        };
        Tracker.ForegroundChanged += () => DispatcherQueue.TryEnqueue(() =>
        {
            if (_closing) return;
            UpdateFullscreen();
            _fullscreenTimer.Stop();
            _fullscreenTimer.Start();
        });
    }

    public event Action? Closed;

    public DispatcherQueue DispatcherQueue { get; }
    public DockSettings Settings => _settings;
    public WindowTracker Tracker { get; } = new();
    public WindowCommands Commands { get; } = new();
    public StaWorker ShellWorker { get; } = new("FloatDock shell");
    public IconService Icons { get; }
    public TaskbarController Taskbar { get; } = new();
    public CursorMonitor Cursor { get; } = new();
    public FullscreenDetector Fullscreen { get; } = new();

    /// <summary>Whether motion is on: the Reduce motion setting and Windows' own animation setting.</summary>
    public bool AnimationsEnabled => !_settings.ReduceMotion && _systemAnimationsEnabled;

    public bool IsSettingsOpen => _settingsWindow is not null;

    /// <summary>More than one dock exists, so things that are not per display (the Shell's full-screen verdict) must be told apart.</summary>
    public bool HasSeveralDocks => _views.Count > 1;

    /// <summary>Creates the dock on the primary display and starts loading.</summary>
    public void Start()
    {
        _anchor = AddView(null);
        var messages = _anchor.Messages;
        messages.ShellHook += Tracker.OnShellEvent;
        messages.TaskbarCreated += OnTaskbarCreated;
        messages.SessionEnding += () =>
        {
            // ABS_AUTOHIDE persists across logon, so undo it before the session can end.
            _sessionEnding = true;
            Taskbar.Restore();
        };
        messages.SessionEnded += ended =>
        {
            if (ended) return;
            _sessionEnding = false;
            _ = ApplyTaskbarReplacementAsync();
        };
        InitializeLauncher(messages);
        _anchor.Activate();
        _ = InitializeAsync();
    }

    private DockWindow AddView(long? displayId)
    {
        var view = new DockWindow(this, displayId);
        _views.Add(view);
        return view;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _settings = await _store.LoadAsync();
            _launchCounts.Load();
            _firstSeen.Load();
            if (Environment.ProcessPath is string executable)
                _settings = _settings with { StartWithWindows = await _startupManager.IsEnabledAsync(executable) };
            _settings = Constrain(_settings);
            Render();
            Tracker.Start();
            _anchor!.Messages.RegisterShellHook();
            ApplyLauncherHotKey();
            _catalog.Start();
            if (_settings.CheckForUpdatesAtStartup) _ = DownloadUpdateAtStartupAsync();
            await ApplyTaskbarReplacementAsync();
            PositionDocks();
            ScheduleHides();
            ReconcileDisplays();
            if (!_settings.FirstRunCompleted && !_disableTaskbarReplacement)
            {
                _firstRunPromptShown = true;
                OpenSettings();
            }
        }
        catch (Exception ex)
        {
            LocalLog.Write("Error", "dock-initialization", ex);
            _settings = new DockSettings();
            Render();
            Tracker.Start();
        }
    }

    /// <summary>
    /// Safe mode keeps to one plain dock on the primary display. The Microsoft Store package never replaces the Windows taskbar
    /// (the Store's policy 10.2.8 allows only supported methods for changing the user's Windows experience) and is updated by the
    /// Store, so it neither hides the taskbar nor checks for updates itself.
    /// </summary>
    private DockSettings Constrain(DockSettings settings)
    {
        if (_safeMode) settings = settings with { Mode = DockMode.Floating, Magnification = 1, Displays = DockDisplays.Primary };
        if (PackageIdentity.IsPackaged) settings = settings with { ReplaceTaskbar = false, CheckForUpdatesAtStartup = false };
        return settings;
    }

    /// <summary>Builds the groups once and lets every dock draw them.</summary>
    public void Render()
    {
        if (_closing) return;
        try { _systemAnimationsEnabled = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TypeLoadException) { _systemAnimationsEnabled = true; }
        _groups = DockModel.Build(_windows, _settings.Pins, AppFor, DescriptionFor);
        foreach (var view in _views.ToList()) view.Render(_groups);
    }

    /// <summary>
    /// The app's own shell item first (packaged logos, Start menu icon), then its files. A window-supplied ID is only
    /// turned into a shell name when it is a plain AppUserModelID; Start menu file items only when the catalog lists them.
    /// </summary>
    public IEnumerable<string?> IconSources(AppGroup group)
    {
        if (group.AppId is { } appId && LaunchTarget.IsAppUserModelId(appId)) yield return IconService.AppsFolderItem(appId);
        if (group.LaunchTarget is { Kind: LaunchTargetKind.AppUserModelId } target && target.Value != group.AppId && IsLaunchable(target.Value))
            yield return IconService.AppsFolderItem(target.Value);
        if (group.LaunchTarget is { Kind: LaunchTargetKind.Shortcut } shortcut) yield return shortcut.Value;
        yield return group.ExecutablePath;
        if (group.LaunchTarget is { Kind: LaunchTargetKind.Executable } executable) yield return executable.Value;
        if (group.LaunchTarget is { Kind: LaunchTargetKind.Folder or LaunchTargetKind.File } path) yield return path.Value;
    }

    /// <summary>shell:AppsFolder names: any plain AppUserModelID, a known-folder file item only if the catalog lists it.</summary>
    private bool IsLaunchable(string appsFolderName) =>
        LaunchTarget.IsAppUserModelId(appsFolderName) ||
        (LaunchTarget.IsKnownFolderItem(appsFolderName) && _catalogIds.Contains(appsFolderName));

    /// <summary>Start menu entry by AppUserModelID or executable.</summary>
    private AppEntry? AppFor(string? appId, string? executablePath)
    {
        if (appId is not null && _appsByKey.TryGetValue(DockModel.AppIdKey(appId), out var app)) return app;
        return executablePath is not null && _appsByKey.TryGetValue(DockModel.PathKey(executablePath), out app) ? app : null;
    }

    /// <summary>The executable's own (localized) description, for apps without a Start menu entry.</summary>
    private string? DescriptionFor(string executablePath)
    {
        if (_descriptions.TryGetValue(executablePath, out var description)) return description;
        try { description = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath).FileDescription?.Trim(); }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException) { description = null; }
        description = string.IsNullOrWhiteSpace(description) ? null : description;
        _descriptions[executablePath] = description;
        return description;
    }

    // --- Starting things ---------------------------------------------------------------------------------------------

    /// <summary>Starts the app's launch target, which opens another instance or window when the app allows several.</summary>
    public void StartTarget(LaunchTarget target)
    {
        _launcher.Launch(target);
        // A catalog app is counted under its list name, which is also its launch value.
        if (target.Kind is LaunchTargetKind.AppUserModelId or LaunchTargetKind.Executable) _launchCounts.Increment(target.Value);
    }

    // --- Pins ----------------------------------------------------------------------------------------------------------

    public async Task TogglePinAsync(AppGroup group)
    {
        if (group.IsPinned) await PersistAndRenderAsync(_settings with { Pins = PinList.Remove(_settings.Pins, group.Key) });
        else await PinAtAsync(group, _settings.Pins.Count);
    }

    /// <summary>Pins a running app, or moves an already pinned one, to <paramref name="index"/>.</summary>
    public async Task PinAtAsync(AppGroup group, int index)
    {
        var existing = _settings.Pins.FirstOrDefault(p => p.Key.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
        var pin = existing ?? (group.LaunchTarget is { } target && target.IsValid() ? new PinnedApp(group.Key, group.DisplayName, target) : null);
        if (pin is null) return;
        await PersistAndRenderAsync(_settings with { Pins = PinList.Insert(_settings.Pins, pin, index) });
    }

    /// <summary>Pins dropped apps, shortcuts, folders and files at <paramref name="index"/>, in drop order.</summary>
    public async Task PinDroppedFilesAsync(DragEventArgs args, int index)
    {
        if (!args.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
        var files = await args.DataView.GetStorageItemsAsync();
        var pins = _settings.Pins.ToList();
        foreach (var file in files)
        {
            var target = LaunchTarget.FromFile(file.Path);
            if (target is null) continue;
            PinnedApp? pin;
            if (target.Kind == LaunchTargetKind.Shortcut) pin = await _pinImporter.PinForShortcutAsync(target.Value);
            else
            {
                var executable = target.Kind == LaunchTargetKind.Executable ? target.Value : null;
                var name = (executable is null ? null : AppFor(null, executable)?.Name)
                    ?? (target.Kind == LaunchTargetKind.Folder ? Path.GetFileName(target.Value) : Path.GetFileNameWithoutExtension(target.Value));
                pin = new PinnedApp(PinList.KeyFor(target, executable), string.IsNullOrWhiteSpace(name) ? target.Value : name, target);
            }
            if (pin is null) continue;
            var before = pins.Count;
            pins = PinList.Insert(pins, pin, index);
            if (pins.Count > before) index++;
        }
        await PersistAndRenderAsync(_settings with { Pins = pins });
    }

    public async Task PersistAndRenderAsync(DockSettings next)
    {
        try
        {
            await _store.SaveAsync(next);
            _settings = next;
            Render();
            _settingsWindow?.ShowSettings(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LocalLog.Write("Error", "settings-save", ex);
        }
    }

    // --- Dragging --------------------------------------------------------------------------------------------------------

    /// <summary>The app being dragged from a dock, for every dock to accept as a drop.</summary>
    private DockWindow? _dragSource;
    public string? DragKey { get; private set; }
    public AppGroup? DragGroup { get; private set; }

    public void BeginDrag(AppGroup group, DockWindow source)
    {
        _dragSource = source;
        DragKey = group.Key;
        DragGroup = group;
        foreach (var view in _views) view.UpdateHitRegion();
    }

    public AppGroup? EndDrag()
    {
        var dragged = DragGroup;
        _dragSource = null;
        DragKey = null;
        DragGroup = null;
        ScheduleHides();
        foreach (var view in _views) view.UpdateHitRegion();
        return dragged;
    }

    /// <summary>Whether the pointer is away from every dock, for dropping a pinned icon into the void.</summary>
    public bool IsCursorAwayFromDocks() => _views.ToList().All(view => view.IsCursorAwayFromDock());

    // --- Docks across displays -------------------------------------------------------------------------------------------

    /// <summary>Makes the set of docks match the Displays setting and the displays that exist.</summary>
    private void ReconcileDisplays()
    {
        if (_closing) return;
        var extras = _views.Where(view => !view.IsAnchor).Select(view => view.DisplayId!.Value).ToList();
        // A failed listing says nothing about the displays: keep the docks as they are.
        if (!ScreenDisplays.TryAll(out var displays)) return;
        var change = DockDisplayPlan.Reconcile(extras, displays, _settings.Displays);
        foreach (var id in change.Remove) _views.FirstOrDefault(view => view.DisplayId == id)?.Close();
        foreach (var id in change.Add)
        {
            DockWindow? view = null;
            try
            {
                view = AddView(id);
                // Placed and filled before it is shown, and shown without taking the focus from the app in use.
                view.Render(_groups);
                view.AppWindow.Show(false);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or InvalidCastException or ArgumentException)
            {
                // An extra dock is optional: the primary dock must keep working.
                LocalLog.Write("Error", "extra-dock-create", ex);
                if (view is not null) { _views.Remove(view); try { view.Close(); } catch (Exception closeError) when (closeError is System.Runtime.InteropServices.COMException or InvalidOperationException) { } }
            }
        }
        if (change.Add.Count > 0 || change.Remove.Count > 0)
            LocalLog.Write("Info", $"docks-reconciled added={change.Add.Count} removed={change.Remove.Count} total={_views.Count}");
    }

    /// <summary>A dock's display scale changed: the menu is closed and every dock draws again at the new size.</summary>
    internal void OnDockScaleChanged()
    {
        _launcherWindow?.Dismiss();
        Render();
    }

    /// <summary>A display was added, removed, rearranged or changed resolution, or the PC woke up.</summary>
    private void OnDisplaysChanged()
    {
        if (_closing) return;
        // Menus and previews sit where the old layout was.
        _launcherWindow?.Dismiss();
        Taskbar.EnsureHidden();
        ReconcileDisplays();
        foreach (var view in _views.ToList()) view.OnDisplaysChanged();
        Tracker.Refresh();
    }

    public void PositionDocks(bool reserveAgain = false)
    {
        foreach (var view in _views.ToList()) view.PositionDock(reserveAgain);
    }

    private void ScheduleHides()
    {
        foreach (var view in _views.ToList()) view.ScheduleHide();
    }

    private void UpdateFullscreen()
    {
        foreach (var view in _views.ToList()) view.UpdateFullscreen();
    }

    /// <summary>The dock a user at the pointer is most likely looking at: the one on that display, else the primary dock.</summary>
    private DockWindow DockAtPointer()
    {
        if (Cursor.Position is { } cursor && DockDisplayPlan.At(ScreenDisplays.All(), cursor.X, cursor.Y) is { } display &&
            _views.FirstOrDefault(view => view.DisplayId == display.Id) is { } here)
            return here;
        return _anchor!;
    }

    internal void ViewClosed(DockWindow view)
    {
        _views.Remove(view);
        // The dock a drag started on is gone, so no drop will report back.
        if (ReferenceEquals(_dragSource, view)) EndDrag();
        if (!_closing) Render();
    }

    // --- Leaving -----------------------------------------------------------------------------------------------------------

    /// <summary>Closes every dock; the app ends with the primary one.</summary>
    public void Quit()
    {
        foreach (var view in _views.Where(view => !view.IsAnchor).ToList()) view.Close();
        _anchor?.Close();
    }

    /// <summary>The primary dock is closing: stop everything the docks share, before that dock lets go of its window.</summary>
    internal void BeginShutdown()
    {
        _closing = true;
        _settingsWindow?.Close();
        _settingsWindow = null;
        DisposeLauncher();
        _displayTimer.Stop();
        _fullscreenTimer.Stop();
        _taskbarTimer.Stop();
        foreach (var view in _views.Where(view => !view.IsAnchor).ToList()) view.Close();
        Taskbar.Restore();
        _guardian?.Dispose();
    }

    internal void CompleteShutdown()
    {
        // An update downloaded earlier is applied by the updater once this process is gone.
        if (_updates.HasDownloaded) _updates.ApplyOnExit(restart: false);
        Tracker.Dispose();
        _displayEvents.Dispose();
        _catalog.Dispose();
        ShellWorker.Dispose();
        Closed?.Invoke();
    }

    // --- Settings window -------------------------------------------------------------------------------------------------

    public void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, _taskbarStatus, _firstRunPromptShown && !_settings.FirstRunCompleted, async value =>
            {
                if (_closing) return _settings;
                var previous = _settings;
                _settings = Constrain(value.Validate());
                var hotKeyChanged = previous.LauncherHotKey != _settings.LauncherHotKey;
                if (Environment.ProcessPath is string executable &&
                    !await _startupManager.SetEnabledAsync(executable, _settings.StartWithWindows))
                    _settings = _settings with { StartWithWindows = await _startupManager.IsEnabledAsync(executable) };
                try { await _store.SaveAsync(_settings); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LocalLog.Write("Error", "settings-save", ex);
                    _settings = previous;
                }
                await ApplyTaskbarReplacementAsync();
                if (hotKeyChanged) ApplyLauncherHotKey();
                if (previous.LauncherSort != _settings.LauncherSort) _launcherWindow?.UpdateApps(MenuApps(_catalog.Apps));
                ReconcileDisplays();
                Render();
                return _settings;
            }, () => _pinImporter.ReadAsync(), CheckForUpdatesAsync, InstallUpdateAsync, AppName, id => HideAppAsync(id, hide: false));
            _settingsWindow.SetHotKeyStatus(_hotKeyFailed);
            _settingsWindow.Closed += async (_, _) =>
            {
                _settingsWindow = null;
                ScheduleHides();
                // Closing the first-run prompt counts as "not now"; it is not asked again.
                if (_firstRunPromptShown && !_settings.FirstRunCompleted && !_closing)
                    await PersistAndRenderAsync(_settings with { FirstRunCompleted = true });
            };
        }
        _settingsWindow.Activate();
    }

    // --- Updates -----------------------------------------------------------------------------------------------------------

    public Task<UpdateCheck> CheckForUpdatesAsync() => _updates.CheckAsync(_settings.UpdateFeed);

    /// <summary>Downloads the update that was found, hands it to the updater and exits; the new version starts by itself.</summary>
    public async Task<bool> InstallUpdateAsync()
    {
        if (!await _updates.DownloadAsync() || !_updates.ApplyOnExit(restart: true)) return false;
        Quit();
        return true;
    }

    private async Task DownloadUpdateAtStartupAsync()
    {
        if ((await _updates.CheckAsync(_settings.UpdateFeed)).Outcome == UpdateOutcome.Available) await _updates.DownloadAsync();
    }

    // --- Windows taskbar -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Brings the taskbar in line with the setting. A failure is reported, never turned into a changed setting.
    /// </summary>
    private async Task ApplyTaskbarReplacementAsync()
    {
        await _taskbarGate.WaitAsync();
        try
        {
            if (_closing || _sessionEnding) return;
            if (!_settings.ReplaceTaskbar || _disableTaskbarReplacement)
            {
                StopTaskbarReplacement();
                SetTaskbarStatus(_settings.ReplaceTaskbar ? "replaceSafeMode" : null);
                return;
            }
            if (_guardian is null)
            {
                var guardian = await GuardianSession.TryStartAsync();
                if (_closing || !_settings.ReplaceTaskbar)
                {
                    guardian?.Dispose();
                    return;
                }
                if (guardian is null)
                {
                    StopTaskbarReplacement();
                    SetTaskbarStatus("guardianUnavailable");
                    return;
                }
                _guardian = guardian;
                _guardian.UnexpectedExit += OnGuardianExited;
            }
            if (Taskbar.IsHidden)
            {
                SetTaskbarStatus(null);
                return;
            }
            var result = Taskbar.TryHide();
            if (result == TaskbarHideResult.Hidden)
            {
                _taskbarTimer.Start();
                SetTaskbarStatus(null);
                return;
            }
            StopTaskbarReplacement();
            SetTaskbarStatus(result switch
            {
                TaskbarHideResult.TaskbarNotFound => "taskbarNotFound",
                TaskbarHideResult.JournalUnavailable => "journalUnavailable",
                _ => "hideFailed"
            });
        }
        finally
        {
            _taskbarGate.Release();
            if (!_closing) PositionDocks(reserveAgain: true);
        }
    }

    private void StopTaskbarReplacement()
    {
        _taskbarTimer.Stop();
        Taskbar.Restore();
        _guardian?.Dispose();
        _guardian = null;
    }

    private void SetTaskbarStatus(string? key)
    {
        if (_taskbarStatus == key) return;
        _taskbarStatus = key;
        if (key is not null) LocalLog.Write(key == "replaceSafeMode" ? "Info" : "Error", "taskbar-" + key);
        _settingsWindow?.SetTaskbarStatus(key);
        if (key is not null && key != "replaceSafeMode") OpenSettings();
    }

    private void OnTaskbarCreated()
    {
        // Explorer restarted: re-hide its new taskbar, or retry a hide that found no taskbar.
        if (Taskbar.IsHidden) Taskbar.EnsureHidden();
        else if (_settings.ReplaceTaskbar) _ = ApplyTaskbarReplacementAsync();
        PositionDocks(reserveAgain: true);
        Tracker.Refresh();
    }

    private void OnGuardianExited(GuardianSession session)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (!ReferenceEquals(_guardian, session) || _closing) return;
            LocalLog.Write("Error", "guardian-exited");
            _guardian.Dispose();
            _guardian = null;
            if (_guardianRestarted)
            {
                StopTaskbarReplacement();
                SetTaskbarStatus("guardianFailed");
                PositionDocks(reserveAgain: true);
                return;
            }
            // One restart per session; the taskbar stays hidden while the new Guardian starts.
            _guardianRestarted = true;
            await ApplyTaskbarReplacementAsync();
        });
    }
}
