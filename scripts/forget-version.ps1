# Removes one version from a Velopack release folder that was downloaded from GitHub: its packages, and its lines in the feed files
# (releases.win.json and RELEASES). Used by the release workflow, because `vpk pack` refuses a version that is already in the feed,
# which happens when a draft from a dry run exists for the version that is about to be released.
param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Directory)) { return }

Get-ChildItem -LiteralPath $Directory -Filter "*-$Version-*.nupkg" | Remove-Item

$feed = Join-Path $Directory 'releases.win.json'
if (Test-Path -LiteralPath $feed) {
    $json = Get-Content -LiteralPath $feed -Raw | ConvertFrom-Json
    $json.Assets = @($json.Assets | Where-Object { $_.Version -ne $Version })
    if ($json.Assets.Count -eq 0) { Remove-Item -LiteralPath $feed }
    else { $json | ConvertTo-Json -Depth 5 -Compress | Set-Content -LiteralPath $feed -NoNewline }
}

$legacy = Join-Path $Directory 'RELEASES'
if (Test-Path -LiteralPath $legacy) {
    $kept = @(Get-Content -LiteralPath $legacy | Where-Object { $_ -and $_ -notmatch [regex]::Escape("-$Version-") })
    if ($kept.Count -eq 0) { Remove-Item -LiteralPath $legacy } else { $kept | Set-Content -LiteralPath $legacy }
}
