param(
    [string]$Version = (& (Join-Path $PSScriptRoot 'get-version.ps1')),
    [string]$OutputDir,
    [string]$DotnetPath = 'dotnet',
    # Optional Authenticode signing, as for build-release.ps1.
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$SkipBuild
)

# Builds the installer and the update packages with Velopack (the vpk tool pinned in dotnet-tools.json):
#   <OutputDir>\FloatDockApp-win-Setup.exe             the installer (per user, installs to %LocalAppData%\FloatDockApp)
#   <OutputDir>\FloatDockApp-<Version>-full.nupkg      the package the updater downloads (plus a delta when an older release is in the folder)
#   <OutputDir>\releases.win.json                      the feed index; host the whole folder at an https address for updates
# The package id differs from the data folder (%LocalAppData%\FloatDock) on purpose: Velopack owns and deletes its own folder.
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDir) { $OutputDir = Join-Path $repository 'artifacts/velopack' }
$release = Join-Path $repository 'artifacts/FloatDock-win-x64'

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-release.ps1') -DotnetPath $DotnetPath -CertificateThumbprint $CertificateThumbprint -TimestampUrl $TimestampUrl | Out-Null
}
if (-not (Test-Path -LiteralPath (Join-Path $release 'FloatDock.App.exe'))) { throw 'The release folder is missing; run without -SkipBuild.' }

Push-Location $repository
try {
    & $DotnetPath tool restore | Out-Null
    $arguments = @('vpk', 'pack', '--packId', 'FloatDockApp', '--packVersion', $Version, '--packDir', $release,
        '--mainExe', 'FloatDock.App.exe', '--packTitle', 'FloatDock', '--outputDir', $OutputDir,
        '--icon', (Join-Path $repository 'src/FloatDock.App/Assets/FloatDock.ico'))
    if ($CertificateThumbprint) {
        $sign = "/sha1 $CertificateThumbprint /fd SHA256"
        if ($TimestampUrl) { $sign += " /tr $TimestampUrl /td SHA256" }
        $arguments += @('--signParams', $sign)
    }
    & $DotnetPath @arguments
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }
}
finally { Pop-Location }
Join-Path $OutputDir 'FloatDockApp-win-Setup.exe'
