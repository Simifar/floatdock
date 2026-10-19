# FloatDock architecture

FloatDock is a per-user WinUI 3 process. It does not replace Explorer, inject into other processes, or require elevation. The Windows taskbar stays visible by default. The dock process owns its windows and the only optional shell changes; the guardian is a separate short-lived recovery process.

## Boundaries

- `FloatDock.Core` owns settings, launch targets, grouping, ordering, and geometry decisions. It has no WinUI or P/Invoke dependency.
- `FloatDock.Windows` owns HWND discovery, out-of-context WinEvent hooks, window commands, fullscreen checks, and shell interop. Hooks only enqueue small event values. A bounded channel serializes processing; an initial enumeration and infrequent reconciliation repair lost events.
- `FloatDock.App` owns WinUI windows and dispatches model changes to the UI thread. Native calls are made through services, not XAML event handlers. `DockHost` holds everything the docks share (settings and pins, window tracker, catalog and icons, taskbar and Guardian, app menu, settings window, display changes); a `DockWindow` is the view of one dock on one display (shelf, items, magnification, input region, AppBar band, previews).
- `Program` is the entry point (the XAML-generated one is switched off): Velopack answers its install, update and uninstall command lines first (`InstallHooks`: the Start menu entry that restores the taskbar, and the taskbar restore before uninstall), then the WinUI app starts. `UpdateService` wraps the updater.

- `FloatDock.Guardian` waits on the main process handle and restores the taskbar (and removes a leftover AppBar band) only after an unexpected exit. It exits with the app and is not a service. It is compiled ahead of time (no .NET runtime of its own), so everything it reaches avoids reflection: its JSON files use source generation (`StateJson` in Core).

## Event and identity flow

`EnumWindows` seeds the snapshot. `SetWinEventHook` reports changes to a bounded queue. The tracker re-queries HWND properties, filters tool/owned/cloaked/invisible windows, and publishes snapshots. Application grouping currently uses the executable path, with a PID fallback when the path is inaccessible. Packaged application identity is not resolved yet. Multiple windows from one executable form one group. The UI receives changes through its dispatcher.

Pinned items store a typed launch target (`Executable` or `Shortcut`) and stable order. The model reserves `AppUserModelId` for future packaged-app support; that launch kind is currently unavailable. Paths are never interpreted as shell command lines. Settings use versioned JSON under `%LocalAppData%\FloatDock`, a temporary file with flush, and atomic replacement. Corrupt input is backed up and defaults are loaded. Only the user's choice is saved: safe mode and the Store package limit what the dock does (`DockSettings.Constrained`), not what is written. Nothing is saved, and Settings does not open, until the file has been read.

## Displays and DPI

The dock on the primary display (`DisplayArea.Primary`, the anchor dock: it carries the shell hook, the app menu shortcut and the session messages, and closing it ends the app) always exists. With the setting `Displays = All` every other display gets a dock too; `DockDisplayPlan` (Core, tested) decides which docks to create or close, and `DockHost.ReconcileDisplays` does it when the setting changes or the displays do. Each dock has its own AppBar band, auto-hide, full-screen state, previews and system items; pins, settings, drag state and the one app menu window (opened over the display of the dock that asked, or of the pointer for the shortcut) are shared. `DockPlacement` (Core, tested) computes a dock's rectangle from its display's work area, the AppBar band, the width of the items and `XamlRoot.RasterizationScale`. The application manifest requests PerMonitorV2 DPI awareness; `WM_DPICHANGED` and `XamlRoot.Changed` refetch icons at the new pixel size. A burst of display changes or a resume from sleep is handled once after 400 ms (menus and previews close, the dock is placed again, the AppBar is registered again, windows are rescanned). Full screen has three witnesses: the AppBar notification, a foreground window that covers the display, and `SHQueryUserNotificationState`; the dock is hidden without being activated again when it ends. The Shell's full-screen verdict is not per display, so with several docks it counts only for the display the foreground window is on. A machine with one monitor can exercise the extra docks with the test-only argument `--fake-display=x,y,width,height`, which adds a display that does not exist.

## Shell safety

Floating mode never changes work area; edge auto-hide moves the dock until only an activation strip remains. Reserved mode registers an AppBar band on the bottom edge of each dock's display (journaled in `appbar.json`, one entry per dock window; every entry of a dead process is removed by the next start, Guardian or `--restore-taskbar` after a crash). Taskbar replacement is offered on first run, and the guardian must be running before the taskbar can be hidden.

`TaskbarHidingSession` (Core, behind `ITaskbarShell`) owns the change. Before touching anything it journals the original `ABM_GETSTATE` value to `%LocalAppData%\FloatDock\shell-state.json`; if a journal from a crashed session exists, its value is kept as the original. It then sets `ABS_AUTOHIDE` so the work area is freed and hides `Shell_TrayWnd` and every `Shell_SecondaryTrayWnd`. An out-of-context `EVENT_OBJECT_SHOW` hook on the Explorer process and the `TaskbarCreated` message re-hide immediately; a 500 ms check is the fallback and also re-applies `ABS_AUTOHIDE` if it was undone. In that mode the dock uses the full display bounds.

Undo always restores the journaled state, shows every taskbar, then deletes the journal. It runs on normal exit, on `WM_QUERYENDSESSION` (re-applied if the session end is cancelled), from the unhandled-exception handlers, from Guardian after an unexpected exit, at the next startup if a journal is left, and from `--restore-taskbar`. If Guardian exits unexpectedly, FloatDock restarts it once with the taskbar still hidden; a second exit restores the taskbar. Failures are shown in Settings and never change the saved setting. Safe mode bypasses taskbar changes. Because Explorer owns its taskbar and exposes no supported replacement contract, this feature is isolated and may be unavailable on some Windows builds.

## Security and recovery

Window commands use public user32 APIs and honor foreground restrictions. No process injection, remote memory access, command-line execution from pin data, elevated rights, or network access is needed. The only network use is the optional update check: with an https update source entered in Settings the app downloads release files from that address (Velopack verifies their SHA-256 from the feed index; the feed itself is trusted as much as its host, so sign the binaries and use your own host); with the field empty nothing is contacted. Native handles and hooks have explicit lifetimes. Invalid or vanished HWNDs are ordinary races and are dropped. Startup crash counts force safe mode before optional shell integration is started.

## Not built

- The UI works directly from `AppGroup` lists; there is no separate view-model layer.
- The shell layer (`FloatDock.Windows`) and the WinUI layer have no tests with fakes; `FloatDock.Core` is covered by unit tests, the rest by the scripts in `tests/manual`.
- `WindowTracker` enumerates every window again on each relevant event.
- No tray icons, badges or progress of other apps: Windows has no public API for them (see [NATIVE-APIS.md](NATIVE-APIS.md)).
