param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutFile,
    # A folder with the release files: their SHA-256 is appended to the notes.
    [string]$AssetDir
)

# Writes the release notes: the CHANGELOG section for this version, or the "Unreleased" one when the version has none yet,
# followed by the download and install hints.
$ErrorActionPreference = 'Stop'
$changelog = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../CHANGELOG.md') -Raw
$section = $null
foreach ($heading in "## $Version", '## Unreleased') {
    $start = $changelog.IndexOf($heading)
    if ($start -lt 0) { continue }
    $bodyStart = $changelog.IndexOf("`n", $start) + 1
    $next = $changelog.IndexOf("`n## ", $bodyStart)
    $section = if ($next -lt 0) { $changelog.Substring($bodyStart) } else { $changelog.Substring($bodyStart, $next - $bodyStart) }
    break
}
if (-not $section) { $section = "FloatDock $Version" }

$notes = @"
## Install

- **Installer (recommended):** download ``FloatDock-Setup.exe`` below and run it. It installs for the current user, needs no administrator rights, and FloatDock can update itself from these releases (Settings, Updates).
- **winget:** ``winget install Simifar.FloatDock`` once the package is listed.
- **No installation:** ``FloatDockApp-win-Portable.zip``.

The files are not code-signed yet, so Windows SmartScreen may show "Windows protected your PC": choose More info, then Run anyway. FloatDock needs Windows 10 version 1809 or later, or Windows 11, 64-bit. The Windows taskbar stays as it is unless you agree to replace it on first run.

## Changes

$($section.Trim())
"@
if ($AssetDir) {
    $hashes = Get-ChildItem -LiteralPath $AssetDir -File | Where-Object { $_.Name -match '\.(exe|zip|nupkg)$' } | Sort-Object Name |
        ForEach-Object { '| `{0}` | `{1}` |' -f $_.Name, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    $notes += "`n`n## SHA-256`n`n| File | SHA-256 |`n| --- | --- |`n" + ($hashes -join "`n") + "`n"
}
Set-Content -LiteralPath $OutFile -Value $notes -Encoding utf8
