# Distribution

How FloatDock reaches users, what is automated, and what only the owner of the project can do. Status words are exact: "done" means built and tested here, "ready" means prepared and checked as far as it can be without the outside service, "owner" means an action only the maintainer can take.

| Channel | Status | Needs from the owner |
| --- | --- | --- |
| GitHub Releases (installer, portable zip, self-update) | ready: workflow written, local pipeline tested | make the repository public; push a tag |
| winget | ready: manifests generated and validated | one pull request per release to `microsoft/winget-pkgs` |
| Microsoft Store (MSIX) | ready: package builds and runs; Store review is open | Partner Center account, product name, screenshots, submission |
| Code signing | not done | a certificate or a signing service |

## 1. GitHub Releases (the main channel)

`.github/workflows/release.yml` runs when a tag `vX.Y.Z` is pushed (the tag must equal `<Version>` in `Directory.Build.props`). It builds, tests, packs with Velopack and publishes a release with:

- `FloatDock-Setup.exe` (also named `FloatDockApp-win-Setup.exe`): the per-user installer, no administrator rights. The permanent link `https://github.com/Simifar/floatdock/releases/latest/download/FloatDock-Setup.exe` always points to the newest one and is the link in the README.
- `FloatDockApp-win-Portable.zip`, the full and delta `.nupkg` packages and `releases.win.json`: what the in-app updater reads (Settings, Updates; the default source is the repository itself).
- release notes from `CHANGELOG.md` with the SHA-256 of every file, and `winget-manifests-<version>.zip`.

To release: update `CHANGELOG.md` (rename "Unreleased" to the version), commit, `git tag vX.Y.Z`, `git push --tags`. Run the workflow by hand (workflow_dispatch) to get a draft instead.

Not yet run on GitHub (the repository is private): the workflow itself, `vpk download/upload github`, the in-app update from the repository. The first release is the test of all three; the pipeline's local parts (`scripts/build-velopack.ps1`, `tests/manual/update-check.ps1`) pass.

## 2. winget

`scripts/new-winget-manifest.ps1 -Installer <FloatDock-Setup.exe>` writes the three manifest files for `Simifar.FloatDock` (installer type `exe`, per user, silent switch `--silent`, product code `FloatDockApp`) and runs `winget validate` (passes). The release workflow attaches them as `winget-manifests-<version>.zip`.

To publish: fork `microsoft/winget-pkgs`, copy the files to `manifests/s/Simifar/FloatDock/<version>/`, open a pull request (its bot runs the installer in a sandbox and checks the hash). Later versions: `wingetcreate update Simifar.FloatDock --version <v> --urls <url> --submit`. After that `winget install Simifar.FloatDock` works. Because the installer is unsigned, SmartScreen can still react to the first run on some PCs; winget itself verifies the hash.

## 3. Microsoft Store

`scripts/build-msix.ps1` produces `artifacts/msix/FloatDock-<version>.msix` (unsigned: the Store signs what it publishes) and the unpacked `layout` folder. `tests/manual/msix-check.ps1` registers the layout for a local test (Developer Mode), runs the app from the package and checks the Store edition. This passes on Windows 11.

**What the Store edition does differently** (the app notices its package identity, `PackageIdentity.IsPackaged`):

- It never replaces the Windows taskbar; the switch is not shown. Reason: Store policy 10.2.8 requires supported methods and user consent for anything that changes the user's Windows experience, and hiding Explorer's taskbar windows is not a supported method. Shipping that feature would most likely fail certification. Guardian is not in the package.
- It does not update itself (policy 10.2.2 and the Store's own update); Settings says so.
- "Start with Windows" uses the package's StartupTask instead of the Run key.
- Everything else is the same code: window tracking, app menu, pins, magnification, auto-hide, Reserved mode.

**Owner steps**

1. Register in Partner Center (an individual account has a one-time fee) and reserve the product name. "FloatDock" must be free.
2. Open "Product management > Product identity" and copy `Package/Identity/Name`, `Package/Identity/Publisher` and the publisher display name. Build with them:
   `pwsh scripts/build-msix.ps1 -IdentityName <Name> -Publisher <CN=...> -PublisherDisplayName <name>`
3. Create the submission, upload the `.msix`, and fill the listing from `packaging/store/listing.md`. Privacy policy URL: `https://github.com/Simifar/floatdock/blob/main/PRIVACY.md` (needs the repository to be public). Screenshots are not in the repository yet: at least one, 1366x768 or larger, taken on your own PC without personal data in view.
4. In the certification notes explain the `runFullTrust` capability (a desktop dock must enumerate windows, register an AppBar and launch programs) and that taskbar replacement is not in this edition (text in the listing file).

**Risks, stated plainly.** The package passed `makeappx` packing and the local run only; the Windows App Certification Kit was not run (it needs an elevated session). Certification of a shell-like full-trust app can still ask for changes. The package was tested on Windows 11 only. App data is written to `%LocalAppData%\FloatDock`, which the Store package may redirect to its private folder; the code does not depend on which.

Direct submission of the installer by URL (policy 10.2.9) is not used: it needs every executable signed with a certificate from a certificate authority in the Microsoft Trusted Root Program.

## 4. Code signing

Nothing is signed yet, so Windows SmartScreen shows a warning for a new `Setup.exe` until it has reputation. `scripts/sign.ps1` and the `-CertificateThumbprint` option of `build-release.ps1` and `build-velopack.ps1` sign with a certificate in your user store (tested with a throwaway self-signed one). Options to look at, none set up or verified here: a certificate from a certificate authority (paid), the SignPath Foundation's free certificates for open-source projects, or Microsoft's Trusted Signing service. The Store signs the MSIX itself, so signing matters for the GitHub installer and winget only.

## 5. Repository settings for a public repository

- The history starts from one commit authored with the GitHub no-reply address. In GitHub, "Keep my email addresses private" and "Block command line pushes that expose my email" keep it that way for later commits.
- Enable private vulnerability reporting (Settings, Code security) so `SECURITY.md` works, and protect `main` (require the `windows` and `windows10-era` checks).
- `DESIGN.md` is written in Russian; everything else is English.
- The default update source in `DockSettings.DefaultUpdateFeed` names the repository `Simifar/floatdock`; change it if the repository is renamed or moved.
