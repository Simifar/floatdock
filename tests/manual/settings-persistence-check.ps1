# Settings must survive two paths that used to overwrite them: a Settings window opened before the file is read (a cold start with
# --settings) and a change made in safe mode (whose limits are for use, not for saving). Needs pwsh and a visible desktop; no mouse.
# Close FloatDock first. Backs up settings.json, startup-attempts.json, launch-counts.json and apps-first-seen.json into a folder,
# restores them first in the finally block and verifies the restore by hash.
param([string]$Exe)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$A = [Windows.Automation.AutomationElement]
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'
$files = 'settings.json', 'startup-attempts.json', 'launch-counts.json', 'apps-first-seen.json' | ForEach-Object { Join-Path $dir $_ }
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
foreach ($f in $files) { if (Test-Path $f) { Copy-Item $f $backup } }
$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail = '') { "{0}: {1} {2}" -f $name, $(if ($ok) { 'ok' } else { 'FAILED' }), $detail; if (-not $ok) { $script:failures += $name } }
function Wait-Until([scriptblock]$c, [int]$ms = 15000) { $w = [Diagnostics.Stopwatch]::StartNew(); while ($w.ElapsedMilliseconds -lt $ms) { if (& $c) { return $true }; Start-Sleep -Milliseconds 200 }; $false }
function Find-Window([string]$name, $pid_) { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
  (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $pid_)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name))))) }
# The row's title and its switch share the name; the switch is the element with the toggle pattern.
function Toggle-ReduceMotion($win) {
  $switch = $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.AndCondition(
    (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'Reduce motion')),
    (New-Object Windows.Automation.PropertyCondition($A::IsTogglePatternAvailableProperty, $true)))))
  $switch.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
}
function Saved { Get-Content (Join-Path $dir 'settings.json') -Raw | ConvertFrom-Json }
function Start-Dock([string]$json, [string[]]$arguments) {
  Remove-Item (Join-Path $dir 'startup-attempts.json') -ErrorAction SilentlyContinue
  $json | Set-Content (Join-Path $dir 'settings.json')
  if ($arguments) { Start-Process $Exe -ArgumentList $arguments -PassThru } else { Start-Process $Exe -PassThru }
}
function Quit-Dock($p) {
  $e = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Quit'); [void]$e.Set(); $e.Dispose()
  [void]$p.WaitForExit(8000)
}
$pin = '{"Key":"C:\\WINDOWS\\SYSTEM32\\CHARMAP.EXE","DisplayName":"Pinned Character Map","Target":{"Kind":0,"Value":"C:\\Windows\\System32\\charmap.exe"}}'
try {
  # --- A cold start with --settings: the window waits for the file and a change keeps everything else in it.
  $p = Start-Dock ('{"SchemaVersion":1,"FirstRunCompleted":true,"Language":1,"IconSize":68,"ShowRecycleBin":false,"Pins":[' + $pin + ']}') @('--settings')
  $win = $null
  Check 'Settings opens with the loaded language' (Wait-Until { $script:win = Find-Window 'FloatDock Settings' $p.Id; $null -ne $script:win })
  if ($win) {
    Toggle-ReduceMotion $win
    Check 'The change is saved' (Wait-Until { (Saved).ReduceMotion -eq $true } 5000)
    $s = Saved
    Check 'Pins, icon size and the other switches are kept' (@($s.Pins).Count -eq 1 -and $s.IconSize -eq 68 -and $s.Language -eq 1 -and $s.ShowRecycleBin -eq $false) "pins=$(@($s.Pins).Count) iconSize=$($s.IconSize) language=$($s.Language) showRecycleBin=$($s.ShowRecycleBin)"
  }
  Quit-Dock $p

  # --- Safe mode: the dock runs plain, but a change does not save the safe-mode limits over the user's choice.
  $p = Start-Dock ('{"SchemaVersion":1,"FirstRunCompleted":true,"Language":1,"Magnification":1.8,"Mode":2,"Displays":1,"Pins":[' + $pin + ']}') @('--safe-mode')
  [void](Wait-Until { try { $e = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Activate'); [void]$e.Set(); $e.Dispose(); $true } catch { $false } })
  $win = $null
  Check 'Settings opens in safe mode' (Wait-Until { $script:win = Find-Window 'FloatDock Settings' $p.Id; $null -ne $script:win })
  if ($win) {
    Toggle-ReduceMotion $win
    Check 'The change is saved in safe mode' (Wait-Until { (Saved).ReduceMotion -eq $true } 5000)
    $s = Saved
    Check 'Magnification, mode and displays are the user''s, not the safe-mode limits' ($s.Magnification -eq 1.8 -and $s.Mode -eq 2 -and $s.Displays -eq 1 -and @($s.Pins).Count -eq 1) "magnification=$($s.Magnification) mode=$($s.Mode) displays=$($s.Displays)"
  }
  Quit-Dock $p
}
finally {
  # Restore first: nothing below may keep the user's own files from coming back.
  foreach ($f in $files) {
    $saved = Join-Path $backup (Split-Path $f -Leaf)
    if (Test-Path $saved) { Copy-Item $saved $f -Force } else { Remove-Item $f -ErrorAction SilentlyContinue }
  }
  foreach ($running in @(Get-Process FloatDock.App -ErrorAction SilentlyContinue)) { Stop-Process -Id $running.Id -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Milliseconds 500
  # A dock stopped above may have written again: copy once more, then compare.
  foreach ($saved in Get-ChildItem $backup) { Copy-Item $saved.FullName (Join-Path $dir $saved.Name) -Force }
  $restored = $true
  foreach ($saved in Get-ChildItem $backup) { if ((Get-FileHash $saved.FullName).Hash -ne (Get-FileHash (Join-Path $dir $saved.Name)).Hash) { $restored = $false } }
  if ($restored) { Remove-Item $backup -Recurse } else { "RESTORE FAILED, the copies are in $backup" }
  "Restored: $restored"
}
if ($failures.Count -gt 0) { throw ('Failed: ' + ($failures -join '; ')) }
