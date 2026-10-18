param(
    [string]$Version = (& (Join-Path $PSScriptRoot 'get-version.ps1')),
    # From Partner Center, "Product management > Product identity". The defaults are for local testing and are refused by the Store.
    [string]$IdentityName = 'FloatDock.Dev',
    [string]$Publisher = 'CN=FloatDock Dev',
    [string]$PublisherDisplayName = 'FloatDock contributors',
    [string]$OutputDir,
    [string]$DotnetPath = 'dotnet',
    [switch]$SkipBuild
)

# Builds the Microsoft Store package: artifacts/msix/layout (the unpacked package, which can be registered for a local test with
#   Add-AppxPackage -Register artifacts\msix\layout\AppxManifest.xml     (needs Developer Mode)
# ) and artifacts/msix/FloatDock-<version>.msix (unsigned: Partner Center signs the package it publishes; to sideload one you
# need to sign it with a certificate whose subject equals -Publisher).
# The Store edition differs from the normal build in behaviour, not in files: the app detects its package identity and then
# never replaces the Windows taskbar, never updates itself, and starts with Windows through the package's StartupTask.
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDir) { $OutputDir = Join-Path $repository 'artifacts/msix' }
$release = Join-Path $repository 'artifacts/FloatDock-win-x64'
$layout = Join-Path $OutputDir 'layout'
$package = Join-Path $OutputDir "FloatDock-$Version.msix"
$fourPart = if (($Version -split '\.').Count -eq 3) { "$Version.0" } else { $Version }

if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build-release.ps1') -DotnetPath $DotnetPath | Out-Null }
if (-not (Test-Path -LiteralPath (Join-Path $release 'FloatDock.App.exe'))) { throw 'The release folder is missing; run without -SkipBuild.' }

$kits = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin'
$makeappx = Get-ChildItem -Path $kits -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '[\\/]x64[\\/]' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $makeappx) { throw 'makeappx.exe was not found (install the Windows SDK).' }

if (Test-Path -LiteralPath $OutputDir) {
    if (-not ([IO.Path]::GetFullPath($OutputDir)).StartsWith((Join-Path $repository 'artifacts'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean a folder outside artifacts.' }
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $layout -Force | Out-Null

# Everything but Guardian: it exists to restore a hidden taskbar, which this edition never hides.
Get-ChildItem -LiteralPath $release | Where-Object { $_.Name -ne 'Guardian' } | Copy-Item -Destination $layout -Recurse -Force
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repository 'assets/store') -Filter '*.png' | Copy-Item -Destination $assets -Force

$manifest = Get-Content -LiteralPath (Join-Path $repository 'packaging/msix/AppxManifest.template.xml') -Raw
foreach ($pair in @{ IdentityName = $IdentityName; Publisher = $Publisher; PublisherDisplayName = $PublisherDisplayName; Version = $fourPart }.GetEnumerator()) {
    $manifest = $manifest.Replace('{{' + $pair.Key + '}}', [Security.SecurityElement]::Escape($pair.Value))
}
if ($manifest -match '\{\{') { throw 'An unfilled placeholder is left in the manifest.' }
Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Value $manifest -Encoding utf8

& $makeappx pack /d $layout /p $package /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed.' }
Write-Output $package
