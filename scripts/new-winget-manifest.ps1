param(
    [string]$Version = (& (Join-Path $PSScriptRoot 'get-version.ps1')),
    # The installer file of that release (FloatDock-Setup.exe): its SHA-256 goes into the manifest.
    [Parameter(Mandatory)][string]$Installer,
    [string]$Repository = 'Simifar/floatdock',
    [string]$OutputDir
)

# Writes the three winget manifest files for a release, in the layout of the winget-pkgs repository, and validates them
# with `winget validate`. Submit them with a pull request to https://github.com/microsoft/winget-pkgs, or with
# `wingetcreate update Simifar.FloatDock --version <v> --urls <url> --submit`. The release must already be published,
# because winget downloads the installer from InstallerUrl and compares the hash.
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDir) { $OutputDir = Join-Path $repositoryRoot "artifacts/winget/manifests/s/Simifar/FloatDock/$Version" }
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$hash = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToUpperInvariant()
$url = "https://github.com/$Repository/releases/download/v$Version/FloatDock-Setup.exe"
$schema = '1.9.0'
$id = 'Simifar.FloatDock'

# The Velopack installer is an .exe that installs per user without administrator rights; --silent skips its window.
# The uninstall entry it writes is named FloatDockApp (the Velopack package id).
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@ | Set-Content -LiteralPath (Join-Path $OutputDir "$id.yaml") -Encoding utf8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $Version
InstallerType: exe
Scope: user
InstallModes:
- silent
- silentWithProgress
InstallerSwitches:
  Silent: --silent
  SilentWithProgress: --silent
UpgradeBehavior: install
MinimumOSVersion: 10.0.17763.0
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $hash
  ProductCode: FloatDockApp
  AppsAndFeaturesEntries:
  - DisplayName: FloatDock
    DisplayVersion: $Version
    ProductCode: FloatDockApp
ManifestType: installer
ManifestVersion: $schema
"@ | Set-Content -LiteralPath (Join-Path $OutputDir "$id.installer.yaml") -Encoding utf8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: FloatDock contributors
PublisherUrl: https://github.com/$Repository
PublisherSupportUrl: https://github.com/$Repository/issues
PrivacyUrl: https://github.com/$Repository/blob/main/PRIVACY.md
PackageName: FloatDock
PackageUrl: https://github.com/$Repository
License: MIT
LicenseUrl: https://github.com/$Repository/blob/main/LICENSE
Copyright: Copyright (c) 2026 FloatDock contributors
ShortDescription: A floating dock for the Windows desktop.
Description: FloatDock is a floating dock for Windows 10 and 11 with running-app indicators, window previews, an app menu with search, pinned apps and folders, magnifying icons and auto-hide. It can optionally replace the Windows taskbar, always restoring it on exit.
Moniker: floatdock
Tags:
- dock
- launcher
- taskbar
- windows
ReleaseNotesUrl: https://github.com/$Repository/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@ | Set-Content -LiteralPath (Join-Path $OutputDir "$id.locale.en-US.yaml") -Encoding utf8

if (Get-Command winget -ErrorAction SilentlyContinue) {
    winget validate --manifest $OutputDir
    if ($LASTEXITCODE -ne 0) { throw 'winget validate failed.' }
} else { Write-Warning 'winget is not installed here; the manifests were written but not validated.' }
$OutputDir
