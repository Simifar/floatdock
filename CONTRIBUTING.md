# Contributing

Thanks for looking at FloatDock. This page covers how to build it, what the code expects of a change, and how a release is made. By taking part you agree to the [code of conduct](CODE_OF_CONDUCT.md).

## Build and test

You need the .NET 10 SDK on Windows 10 (1809 or later) or Windows 11.

```powershell
dotnet build FloatDock.slnx
dotnet test tests/FloatDock.Core.Tests/FloatDock.Core.Tests.csproj
```

The build treats warnings as errors, so it must finish with none. The release folder (`scripts/build-release.ps1`) also needs the C++ build tools, because Guardian is compiled ahead of time.

## How the code is laid out

- `src/FloatDock.Core`: models, settings, layout maths. No WinUI and no P/Invoke here, and everything in it is unit tested.
- `src/FloatDock.Windows`: all Win32 and shell calls. Keep them here, not in XAML event handlers.
- `src/FloatDock.App`: the WinUI interface. `DockHost` holds what the docks share, `DockWindow` is one dock on one display.
- `src/FloatDock.Guardian`: the recovery helper, compiled ahead of time. Everything it reaches must avoid reflection, so the files it reads or writes use JSON source generation (`StateJson` in Core).
- `tests`: unit tests for Core, and `tests/manual` for scripts that start the real app.

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the design and [DESIGN.md](DESIGN.md) records the interface rules (colors, spacing, motion). Read `DESIGN.md` before changing anything visible.

## What a change needs

- Small, focused commits whose messages say why, not only what.
- Tests for model, settings and layout changes. A new setting gets an initializer, a clamp in `Validate()`, and a test that an old `settings.json` without it still loads.
- Only documented Windows interfaces. Taskbar hiding must never become the default.
- The matching script from `tests/manual` for behavior the unit tests cannot reach. None of them uses the mouse, because synthetic pointer input does not reach WinUI, so say in the pull request what only a person with a mouse can check.
- An entry in `CHANGELOG.md` under "Unreleased" for anything a user would notice, and an update to `docs/TESTING.md` for what you checked and what you did not.

Open a pull request from a branch. CI builds and tests on `windows-latest` and starts the published app on a Windows Server 2022 runner; both must pass. The pull request template lists the same points.

Some test aids ship in the release build on purpose: `FloatDock.App.exe --crash-test` throws on the UI thread after three seconds to exercise the crash handler (`tests/manual/crash-check.ps1`), and `--fake-display=x,y,width,height` adds a display that does not exist so one monitor can exercise the dock on every display.

## Releasing

The release number lives in `Directory.Build.props` (`<Version>`). To release:

1. Rename "Unreleased" in `CHANGELOG.md` to the version and date, and add an empty "Unreleased" above it.
2. Set `<Version>` and merge both changes.
3. Run the Release workflow by hand (workflow_dispatch) to get a draft release and look at it.
4. Push a tag `vX.Y.Z` that equals `<Version>`. `.github/workflows/release.yml` builds, tests, packs with Velopack and publishes `FloatDock-Setup.exe`, the portable zip, the files the in-app updater reads, release notes with SHA-256 sums and the winget manifests.

`scripts/build-velopack.ps1` does the packing locally, and `tests/manual/update-check.ps1` tries an update against a folder. The icon is drawn by `scripts/make-icons.ps1`; run it after changing the artwork. The pictures in `docs/images` come from `tests/manual/readme-screenshots.ps1`. [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md) covers winget, the Microsoft Store and code signing.

## Reporting problems

Use the issue templates for bugs and ideas. Report security problems privately, see [SECURITY.md](SECURITY.md).
