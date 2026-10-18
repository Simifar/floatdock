# Native API inventory

| API | Owner and purpose | Failure and fallback |
| --- | --- | --- |
| `EnumWindows`, `IsWindowVisible`, `GetWindow`, `GetWindowLongPtrW`, `GetWindowRect`, `GetWindowTextW`, `GetClassNameW` | Window tracker, initial snapshot and eligibility | Discard invalid or transient HWND; next event or reconciliation retries. |
| `SetWinEventHook`, `UnhookWinEvent` | Tracker, out-of-context event stream | Keep initial snapshot and sparse reconciliation; release hooks on disposal. |
| `GetWindowThreadProcessId`, `OpenProcess`, `QueryFullProcessImageNameW`, `CloseHandle` | Identity resolver | Retain HWND with reduced identity when process access is denied; close every successful process handle. |
| `DwmGetWindowAttribute` | Window tracker, cloaked filtering | Treat failed query as unknown; other eligibility rules still apply. |
| `ShowWindowAsync`, `SetForegroundWindow`, `IsIconic`, `PostMessageW` | Window controller, restore/minimize/activate/close | One attempt, no focus stealing loop; Windows may refuse foreground. |
| `GetCursorPos` | Auto-hide activation strip | Polled only while Auto-hide is selected; XAML pointer events remain available. |
| `FindWindowW`, `FindWindowExW`, `ShowWindow`, `SHAppBarMessage(ABM_GETSTATE/ABM_SETSTATE)` | Taskbar replacement and guardian: hide all Explorer taskbars, free the work area with `ABS_AUTOHIDE` | Original state journaled first; undone on exit, session end, crash handlers, Guardian, next startup or `--restore-taskbar`. A failed hide is undone and reported. |
| `SetWinEventHook(EVENT_OBJECT_SHOW)` on the Explorer process | Re-hide a taskbar Explorer shows again | Re-hooked when Explorer's process changes; 500 ms check is the fallback. |
| `SetWindowLongPtrW`, `SetWindowPos(SWP_FRAMECHANGED)`, `DwmSetWindowAttribute(DWMWA_BORDER_COLOR, DWMWA_WINDOW_CORNER_PREFERENCE)`, `DwmExtendFrameIntoClientArea(-1)` | Dock window: no frame, outline, rounded window corners or window shadow; per-pixel transparent client area | Failures are logged; the dock still works with a visible frame. |
| `SHAppBarMessage(ABM_NEW/QUERYPOS/SETPOS/REMOVE)` | Reserved mode: bottom-edge band so maximized windows stop above the dock; `ABN_POSCHANGED` re-queries, `ABN_FULLSCREENAPP` hides the dock | Registrations journaled in `appbar.json` (one per dock window, so one per display); a band whose owner died is removed at startup, by Guardian, the crash handler or `--restore-taskbar`. |
| `WS_EX_TOOLWINDOW` | Dock window: no Windows taskbar button or Alt+Tab entry | None needed. |
| `CreateRectRgn`, `SetWindowRgn` | Dock window: input only over the shelf, the whole window while the dock is in use | On failure the region is released and the whole window keeps taking input. |
| `CreateDispatcherQueueController` (CoreMessaging) | Transparent system backdrop needs a system dispatcher queue for `Windows.UI.Composition` | Throws on failure; created once per UI thread. |
| `SetWindowSubclass`, `DefSubclassProc`, `RegisterWindowMessageW("TaskbarCreated")` | Dock window: Explorer restart, `WM_QUERYENDSESSION`, `WM_ENDSESSION` | If subclassing fails, the 500 ms check still re-hides; Guardian still covers session end. |
| `SHGetPropertyStoreForWindow` + `PKEY_AppUserModel_ID`, `GetApplicationUserModelId`, `EnumChildWindows` | Window identity for grouping, including UWP windows behind `ApplicationFrameHost` | Null identity falls back to executable grouping; cached per window, re-read every 45 s. |
| `SHCreateItemFromParsingName("shell:AppsFolder")`, `IShellItem::BindToHandler(BHID_EnumItems)`, `IShellItem2::GetString` | App catalog on a dedicated STA thread | Disk cache shown first; failures logged, last list kept. |
| `PackageCatalog.OpenForCurrentUser`, `FileSystemWatcher` on Start menu folders | Catalog reload on installs | Without package events, the list refreshes when stale. |
| `IShellItemImageFactory::GetImage`, `GetDIBits` | Icons with alpha at the needed size | Next source tried; generic glyph if none. LRU of 256. |
| `RegisterShellHookWindow` | Shell notifications: created, destroyed, retitled, activated, flashing windows | WinEvent hooks and reconciliation still refresh the list. |
| `RegisterHotKey` / `UnregisterHotKey` (+ `WM_HOTKEY` through the window subclass) | Global shortcut for the app menu, `MOD_NOREPEAT` | A shortcut another program holds fails to register; this is logged and Settings says so. Nothing else is affected. |
| `MiniDumpWriteDump` (dbghelp) | Local minidump (threads, stacks, modules, no heap) when an unhandled exception is about to end the process | Never throws; the last five dumps are kept in `%LocalAppData%FloatDockcrashes`; nothing leaves the machine. Native crashes that bypass the .NET handlers leave no dump. |
| `WM_DPICHANGED` (window subclass), `XamlRoot.Changed`, `DisplayArea.Primary` | Re-place the dock and refetch icons after a scale change or display change | A display event burst is debounced (400 ms). Without the message, `XamlRoot.Changed` still reports the scale. |
| `SHQueryUserNotificationState` | Hide the dock for exclusive full-screen apps and presentations (states busy, Direct3D full screen, presentation mode) | One of three witnesses with the AppBar notification and window geometry; not per display. Rechecked 700 ms after a foreground change. |
| `AttachThreadInput`, `BringWindowToTop`, `SetForegroundWindow` | Give the app menu the keyboard focus when another app is in front | Short attach to the foreground thread, always detached; if Windows still refuses, the menu is open but the user must click it. |
| `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` | Keep the dock above the app menu so an app can be dragged onto it | None needed. |
| `explorer.exe /select,"path"`, `ms-settings:appsfeatures` | App menu: show an app's file, open Windows' list of installed apps | Path must be fully qualified and exist; failures are logged. |
| `DwmRegisterThumbnail`, `DwmUpdateThumbnailProperties`, `DwmQueryThumbnailSourceSize`, `DwmUnregisterThumbnail` | Live window previews drawn by DWM over a placeholder in the preview window | Skipped for a window DWM refuses (gone, or elevated); the card still works as a button. Always unregistered on hide and close. |
| `SHQueryRecycleBinW`, `SHEmptyRecycleBinW` | Recycle Bin state and "Empty": the Shell shows its own confirmation | Query on the shell STA thread every 10 s only while the item is shown. The dock never deletes anything itself. |
| `SHGetKnownFolderPath(FOLDERID_Downloads)` | Downloads may be moved by the user | No item when it cannot be resolved. |
| `Shell.Application.ToggleDesktop` (COM automation) | Show desktop button | Logged on failure. |
| `keybd_event` (Win+A, Win+N) | Quick settings and notification buttons; Windows offers no other way to open those panels | Plain key presses, as if typed. |
| `IShellLinkW::GetPath`, `IPersistFile::Load` | Shortcut resolver, obtain `.lnk` executable for grouping and icons | Keep the shortcut as a separate pin if target resolution fails. |
| `System.Drawing.Icon.ExtractIcon` | Icon resolver, decode executable resources at 64 px | Use the generic WinUI glyph if extraction fails; cache at most 128 images. |
| `AppWindow`, `DisplayArea`, `DesktopAcrylicBackdrop` | WinUI dock layout and desktop material | Use primary display; no undocumented compositor calls. |
| `SystemEvents.DisplaySettingsChanged`, `SystemEvents.PowerModeChanged` | Reposition and reconcile after display changes and resume | Unsubscribe on close; retain periodic window reconciliation if events are unavailable. |
| `UISettings.AnimationsEnabled` | Respect Windows reduced-motion preference | Keep the explicit FloatDock setting available if this query fails. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Opt-in startup setting for unpackaged release | No elevation; removes only FloatDock's own value. |

Interop is isolated in narrowly scoped `DllImport` declarations and the documented Shell Link COM interface. The Explorer taskbar class name is not a supported shell replacement contract; it is only used in the opt-in mode. The dock does not use undocumented virtual desktop COM interfaces, DLL injection, or Explorer binary modification.

## Taskbar progress and badges: not available, not implemented

Apps report progress (`ITaskbarList3::SetProgressValue`) and overlay icons (`SetOverlayIcon`) to the taskbar object that Explorer itself implements, and packaged apps set badges through `BadgeUpdater`; Explorer stores and draws them. This is from the public documentation; it was not tried on this machine. A dock that leaves Explorer as the shell has no supported way to read either: there is no public API to query another app's progress or badge, and answering the apps' own calls would mean replacing Explorer's taskbar object, which is outside the scope of this project (no injection, no undocumented interfaces). What the dock does show: the "needs attention" state from `HSHELL_FLASH`, and window titles in the previews. Do not promise progress or badges until a documented source is found.
