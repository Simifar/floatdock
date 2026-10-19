# Changelog

## Unreleased

- Settings opened while the dock is still starting (`--settings`, or a second launch at that moment) no longer show the defaults and save them over your settings at the first change, which lost every pin. The window now opens once the settings are loaded.
- An error while the dock starts no longer replaces the loaded settings with the defaults, which the next pin or change then saved over the file. If the file cannot be read at all, the dock runs with the defaults and does not save them.
- Safe mode (`--safe-mode`, or after three quick failed starts) no longer saves its limits (no magnification, floating, primary display only) as your settings when you change something; the next normal start has your own choice again. Settings shows that choice in safe mode too.
- The dock's clock no longer stops while another window keeps changing its title (a browser, a player, a terminal): every change restarted its 10-second timer. The Recycle Bin's full or empty icon had the same problem.
- A damaged update download (its SHA-256 does not match the feed) no longer ends the dock when you press Install in Settings; Settings says the update could not be downloaded. The same holds for the updater's other errors, such as two downloads at once.

## 0.4.0 - 2026-10-18

- The dock has no outline or visible window rectangle: only the shelf is drawn, and clicks outside it reach the windows underneath.
- Reserved mode registers the dock as an AppBar on the bottom edge, so maximized windows stop above it; it is the default when replacing the taskbar. A band left by a crash is removed at the next start, by Guardian or by `--restore-taskbar`.
- The dock no longer appears as a Windows taskbar button or in Alt+Tab, and clicking an active app's icon minimizes it.
- Windows group by AppUserModelID like on the Windows taskbar, so packaged apps (Calculator, Terminal, Settings) get one button with their real name and logo and can be pinned.
- Start menu app catalog (shell:AppsFolder) with search, kept current on installs; icons come from the shell at the magnified size.
- Window tracking reacts only to events that can change the window list, and knows which windows ask for attention.
- Pin folders and files; drag a running app into place to pin it; drop a pinned icon away from the dock to unpin; reorder, unpin and import Windows taskbar pins in Settings.
- Settings no longer overwrite pins changed in the dock while Settings is open.
- Smaller release: the Windows App SDK metapackage is replaced by the parts the dock uses (the AI, ML, Widgets and Search components, about 100 MB, are gone), and Guardian is compiled ahead of time (2 MB instead of a second runtime). The release folder went from 338 MB to about 190 MB, the zip from 117 MB to 74 MB.
- Automatic updates through Velopack: Settings has an "Updates" section (source: an https address or a folder, empty by default so nothing is sent; a button to check; optional download at startup that installs when the dock exits). `scripts/build-velopack.ps1 -Version x.y.z` builds the Velopack installer and the update packages (per-user install to `%LocalAppData%\FloatDockApp`, Start menu entry "Restore the Windows taskbar", the taskbar is restored before uninstall; settings and logs in `%LocalAppData%\FloatDock` stay).
- `build-release.ps1` and `build-velopack.ps1` accept `-CertificateThumbprint` to Authenticode-sign the binaries and the installer with a certificate from the current user's store (`scripts/sign.ps1`); the default build stays unsigned.
- After an unhandled exception a local minidump is written to `%LocalAppData%FloatDockcrashes`; log lines carry the process id.
- Dock rework: items are updated instead of rebuilt on every window change; magnified icons push their neighbours aside; the indicator shows one segment per window (up to three), is wide for the active app and orange when an app asks for attention.
- Hover an app (or click one with several windows) for live window previews with a close button; the context menu gains New window, Show in Explorer and Close all windows; Shift-click or middle-click starts a new instance.
- Optional dock items, each a switch in Settings: separators, Downloads and pinned folders as a grid of recent items, Recycle Bin (open, empty with the Shell's own confirmation), Show desktop, clock and date, quick settings and notification buttons. Replacing the taskbar turns the clock and the two buttons on.
- Auto-hide slides in and out instead of jumping.
- Magnification follows the pointer every rendered frame instead of a timer that the pointer events kept restarting: icons no longer freeze while the pointer moves and then jump. Measured with a synthetic pointer: full size in 150 ms instead of 435 ms, back to normal in 125 ms instead of 372 ms.
- Magnification no longer shakes when the pointer crosses the gap between two enlarged icons: the whole shelf, not just the icons, counts as the dock, and a short exit (under 90 ms) does not reset the icons.
- A dock on every display is an option in Settings ("Show the dock on"); by default there is one dock on the primary display as before. Each dock has its own AppBar band, so `appbar.json` now lists one registration per dock window (an older single-object file still reads), and a crash, Guardian, the next start or `--restore-taskbar` remove all of them.
- The dock sits on the primary display (or on every display, see above), is re-placed once after a burst of display changes (plug, unplug, resolution, wake from sleep) and follows a scale change (`WM_DPICHANGED`) by refetching icons at the new pixel size. Placement maths is tested for displays left of and above the primary one and for 100, 150 and 200 % scales.
- Full-screen detection also asks the Shell (`SHQueryUserNotificationState`), so exclusive full-screen games and presentations hide the dock in every mode, and it reacts as soon as another app takes the foreground, even a window the dock does not list.
- App menu: a full-screen blurred overlay with every Start menu app, search first (Russian, English, wrong keyboard layout), a "Frequent" row, keyboard navigation, and a context menu to pin, run as administrator, show the file or uninstall. Open it from the first dock button or with Ctrl+Alt+A (changeable, or off, in Settings); drag an app onto the dock to pin it there. A newly installed app appears within about ten seconds without a restart.

- Taskbar replacement hides all taskbars and frees their work area, re-hides instantly after Explorer shows or recreates them, and journals the original state so exit, logoff, crashes, Guardian, the next start and `--restore-taskbar` undo it exactly.
- First run asks whether to replace the taskbar. Failures are shown in Settings; the setting is no longer silently turned off.
- Guardian is restarted once if it exits unexpectedly, starts without blocking the UI thread, and is built and copied with the app in every configuration.
- Unhandled exceptions are logged and restore the taskbar.

- Windows 10 version 1809 and later is supported as well as Windows 11 (the Windows App SDK supports it). The Windows 11-only window border colour is skipped on Windows 10, and the Notifications button uses Win+A there, where Win+N does not exist. Only Windows 11 was tested; a Windows Server 2022 CI job checks that the app starts.
- Application icon (a dark shelf with three tiles, the middle one magnified), drawn by `scripts/make-icons.ps1`: in the exe, the installers, the Settings window and the Store logo sizes under `assets/store`.
- The version is set once in `Directory.Build.props` and shown in Settings and in the file properties.
- The default update source is this project's GitHub releases (`https://github.com/Simifar/floatdock`); nothing is contacted until you press "Check for updates" or turn on the check at startup. A GitHub repository address as the source means that repository's releases.
- Release workflow (`.github/workflows/release.yml`): a `vX.Y.Z` tag builds, tests and publishes the Velopack installer (also as `FloatDock-Setup.exe`, a link that never changes), the portable zip, the updater files and release notes with SHA-256 sums.
- Added `PRIVACY.md`, `THIRD-PARTY-NOTICES.md`, issue and pull request templates; `SECURITY.md` points to GitHub's private vulnerability reporting.

- Microsoft Store package (`scripts/build-msix.ps1`, manifest in `packaging/msix`): a packaged copy never replaces the Windows taskbar and does not update itself (the Store does), and starts with Windows through the package's StartupTask. winget manifests are generated for each release (`scripts/new-winget-manifest.ps1`). `docs/DISTRIBUTION.md` lists the channels and what the owner still has to do; `packaging/store/listing.md` is the Store text.
- Smoother motion: magnification and the auto-hide slide share one frame loop (`FrameLoop`, with the maths in Core's `Motion`) that steps by elapsed time and at most about 100 times a second instead of the ~200 the compositor reports. The auto-hide slide no longer runs on a 16 ms timer that really ticked every ~30 ms.
- An app's icon grows into the dock when it starts and shrinks away when its last window closes (200 ms), and the neighbours and the shelf follow; the icon of a pinned app pulses softly after you click it until its window appears (at most 10 seconds). Both are off with "Reduce motion" or when Windows animations are off.
- Settings are reorganised like Windows 11 settings: a page list (General, Appearance, Dock, Pinned, Updates) and rows in cards instead of one long scrolling list, in the light and dark theme. New switch "Hide in full-screen apps" (the setting existed but had no control).
- Settings > Appearance: space between icons (the old `Spacing` setting now works), shelf opacity and shelf corner radius; each slider shows its value and the dock follows at once. Defaults keep the old look (the shelf's alpha is 224 instead of 225).
- App menu: choose the order of the list (alphabetical, most used first, recently added first), hide apps from it with the context menu and bring them back in Settings > App menu, and see a "New" mark on apps FloatDock first saw in the last 7 days (apps that were already installed at the first look are not marked). Settings has a new App menu page that also holds the shortcut. FloatDock keeps `apps-first-seen.json` in `%LocalAppData%\FloatDock`.

## 0.1.0 - 2026-09-30

- Initial functional WinUI dock, event-driven window tracking, basic pinning and window commands.
- Local settings, safe mode, single instance, and opt-in taskbar guardian.
- Self-contained x64 publish workflow and core tests.
- Edge activation polling for reliable auto-hide, background window reconciliation, and separate hosted-window grouping.
- Taskbar restoration if Guardian exits, plus detection of a restored or recreated Explorer taskbar while replacement is enabled.
