# A damaged update must be reported in Settings, not crash the dock. Installs an older Velopack build, points it at a copy of a
# folder feed whose newer package is damaged (its SHA-256 no longer matches the feed), presses Check for updates and Install, and
# checks that the dock is still running and says the update could not be downloaded. Then uninstalls what it installed.
# Prepare the feed as for update-check.ps1:
#   pwsh scripts/build-velopack.ps1 -Version 0.2.0 -OutputDir <feed>         (keep a copy of FloatDockApp-win-Setup.exe)
#   pwsh scripts/build-velopack.ps1 -Version 0.2.1 -SkipBuild -OutputDir <feed>
# then:  pwsh tests/manual/update-damaged-check.ps1 -Setup <copy of the 0.2.0 setup> -Feed <feed> -From 0.2.0 -To 0.2.1
# Refuses to run when FloatDock is installed (%LocalAppData%\FloatDockApp) or running. Backs up settings.json and
# startup-attempts.json into a folder, restores them first in the finally block and verifies the restore by hash.
param([Parameter(Mandatory)][string]$Setup, [Parameter(Mandatory)][string]$Feed, [string]$From = '0.2.0', [string]$To = '0.2.1')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
$root = Join-Path $env:LOCALAPPDATA 'FloatDockApp'
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
# The uninstaller can leave the empty folder behind; an installed copy has Update.exe or a current folder in it.
if ((Test-Path (Join-Path $root 'Update.exe')) -or (Test-Path (Join-Path $root 'current'))) { throw "FloatDock is installed in ${root}: this check installs its own copy, uninstall the installed one first." }
$A = [Windows.Automation.AutomationElement]
$data = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $data 'settings.json'; $attempts = Join-Path $data 'startup-attempts.json'
$log = Join-Path $data 'logs\floatdock.log'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
foreach ($f in $settings, $attempts) { if (Test-Path $f) { Copy-Item $f $backup } }
$damaged = Join-Path $env:TEMP ('fd-feed-' + [guid]::NewGuid()); Copy-Item (Resolve-Path $Feed).Path $damaged -Recurse
$installed = $false
$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail = '') { "{0}: {1} {2}" -f $name, $(if ($ok) { 'ok' } else { 'FAILED' }), $detail; if (-not $ok) { $script:failures += $name } }
function Wait-Until([scriptblock]$c, [int]$ms = 20000) { $w = [Diagnostics.Stopwatch]::StartNew(); while ($w.ElapsedMilliseconds -lt $ms) { if (& $c) { return $true }; Start-Sleep -Milliseconds 200 }; $false }
function Installed-Version { $f = Join-Path $root 'current\sq.version'; if (Test-Path $f) { ([xml](Get-Content $f -Raw)).package.metadata.version } else { '' } }
function Dock { Get-Process FloatDock.App -ErrorAction SilentlyContinue | Select-Object -First 1 }
function Named($win, [string]$name) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name))) }
function Button($win, [string]$name) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.AndCondition(
  (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name))))) }
function Settings-Window($p) { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
  (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'FloatDock Settings'))))) }
try {
  # The feed index keeps the hashes of the good files; changing the packages of the new version makes them fail the check.
  foreach ($package in Get-ChildItem $damaged -Filter "*-$To-*.nupkg") { [IO.File]::AppendAllText($package.FullName, 'damaged') }
  Remove-Item $attempts -ErrorAction SilentlyContinue
  (@{ FirstRunCompleted = $true; Mode = 0; Language = 1; UpdateFeed = $damaged } | ConvertTo-Json) | Set-Content $settings
  $logLines = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

  & $Setup --silent | Out-Null
  $installed = $true
  Check "Installed $From" (Wait-Until { (Installed-Version) -eq $From }) "version=$(Installed-Version)"
  Start-Process (Join-Path $root 'current\FloatDock.App.exe') | Out-Null
  $dock = $null
  Check 'The installed app starts' (Wait-Until { $script:dock = Dock; $null -ne $script:dock })
  Start-Sleep -Seconds 6
  $dock = Dock
  $activate = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Activate'); [void]$activate.Set(); $activate.Dispose()
  $win = $null
  Check 'Settings opens' (Wait-Until { $script:win = Settings-Window $dock; $null -ne $script:win } 10000)
  # The Updates page is the last one in the page list.
  (Named $win 'Updates').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 800
  (Button (Settings-Window $dock) 'Check for updates').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  $install = $null
  Check "Settings offers $To" (Wait-Until { $script:install = Button (Settings-Window $dock) "Install $To and restart"; $null -ne $script:install })
  if ($install) {
    $install.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Check 'Settings says the update could not be downloaded' (Wait-Until { $w = Settings-Window $dock; $w -and (Named $w 'The update could not be checked or downloaded. Details are in the log.') } 30000)
    Start-Sleep -Seconds 2
    Check 'The dock is still running' (-not $dock.HasExited -and $null -ne (Get-Process -Id $dock.Id -ErrorAction SilentlyContinue))
    Check "$From is still installed" ((Installed-Version) -eq $From) "version=$(Installed-Version)"
  }
  $entries = if (Test-Path $log) { Get-Content $log | Select-Object -Skip $logLines | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } | Where-Object { $_ } } else { @() }
  "Log: " + (($entries | Where-Object { $_.eventName -match 'update|unhandled' } | ForEach-Object { "$($_.level) $($_.eventName) $($_.errorType)" }) -join '; ')
  Check 'No unhandled exception was logged' (@($entries | Where-Object { $_.level -eq 'Fatal' }).Count -eq 0)
}
finally {
  # Restore first: nothing below may keep the user's own files from coming back.
  foreach ($f in $settings, $attempts) {
    $saved = Join-Path $backup (Split-Path $f -Leaf)
    if (Test-Path $saved) { Copy-Item $saved $f -Force } else { Remove-Item $f -ErrorAction SilentlyContinue }
  }
  foreach ($running in @(Get-Process FloatDock.App -ErrorAction SilentlyContinue)) { Stop-Process -Id $running.Id -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Milliseconds 500
  # Only the copy this check installed is removed.
  if ($installed -and (Test-Path (Join-Path $root 'Update.exe'))) { & (Join-Path $root 'Update.exe') --uninstall --silent | Out-Null; Start-Sleep -Seconds 3 }
  Remove-Item $damaged -Recurse -ErrorAction SilentlyContinue
  foreach ($saved in Get-ChildItem $backup) { Copy-Item $saved.FullName (Join-Path $data $saved.Name) -Force }
  $restored = $true
  foreach ($saved in Get-ChildItem $backup) { if ((Get-FileHash $saved.FullName).Hash -ne (Get-FileHash (Join-Path $data $saved.Name)).Hash) { $restored = $false } }
  if ($restored) { Remove-Item $backup -Recurse } else { "RESTORE FAILED, the copies are in $backup" }
  "Restored: $restored; install folder gone: $(-not (Test-Path (Join-Path $root 'current')))"
}
if ($failures.Count -gt 0) { throw ('Failed: ' + ($failures -join '; ')) }
