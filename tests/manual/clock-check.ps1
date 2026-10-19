# The dock's clock keeps time while another window changes its title every second (each change makes the dock draw again).
# Needs pwsh and a visible desktop; no mouse. Close FloatDock first. Runs for about a minute and a half: it waits into the next
# minute. Opens a small window of its own that renames itself. Backs up settings.json and startup-attempts.json into a folder,
# restores them first in the finally block and verifies the restore by hash.
param([string]$Exe)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$A = [Windows.Automation.AutomationElement]
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'
$files = 'settings.json', 'startup-attempts.json' | ForEach-Object { Join-Path $dir $_ }
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
foreach ($f in $files) { if (Test-Path $f) { Copy-Item $f $backup } }
function Clock($p) {
  $e = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.AndCondition(
    (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'FloatDock.clock')))))
  if ($e) { $e.Current.Name } else { '' }
}
$ticker = $null; $p = $null; $ok = $false
try {
  Remove-Item (Join-Path $dir 'startup-attempts.json') -ErrorAction SilentlyContinue
  '{"SchemaVersion":1,"FirstRunCompleted":true,"ShowClock":true,"Mode":0}' | Set-Content (Join-Path $dir 'settings.json')
  $p = Start-Process $Exe -PassThru; Start-Sleep -Seconds 6
  $code = "Add-Type -AssemblyName System.Windows.Forms; `$f = New-Object Windows.Forms.Form; `$f.Text = 'Clock check'; `$f.Width = 300; `$f.Height = 120; `$t = New-Object Windows.Forms.Timer; `$t.Interval = 1000; `$n = 0; `$t.Add_Tick({ `$script:n++; `$f.Text = 'Clock check ' + `$script:n }); `$t.Start(); [Windows.Forms.Application]::Run(`$f)"
  $ticker = Start-Process pwsh -ArgumentList '-NoProfile', '-EncodedCommand', ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))) -PassThru
  # Into the next minute and 25 s past it: the clock refreshes every 10 s, so it must show the new minute by then.
  $now = Get-Date; $target = $now.Date.AddHours($now.Hour).AddMinutes($now.Minute + 1).AddSeconds(25)
  Start-Sleep -Seconds ([Math]::Ceiling(($target - (Get-Date)).TotalSeconds))
  $shown = Clock $p
  $expected = (Get-Date).ToString('t')
  $ok = $shown.EndsWith($expected)
  "Clock while a title changes every second: '$shown', expected the time ${expected}: $(if ($ok) { 'ok' } else { 'FAILED' })"
}
finally {
  # Restore first: nothing below may keep the user's own files from coming back.
  foreach ($f in $files) {
    $saved = Join-Path $backup (Split-Path $f -Leaf)
    if (Test-Path $saved) { Copy-Item $saved $f -Force } else { Remove-Item $f -ErrorAction SilentlyContinue }
  }
  if ($ticker) { Stop-Process -Id $ticker.Id -Force -ErrorAction SilentlyContinue }
  foreach ($running in @(Get-Process FloatDock.App -ErrorAction SilentlyContinue)) {
    try { $quit = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Quit'); [void]$quit.Set(); $quit.Dispose() } catch { }
    if (-not $running.WaitForExit(8000)) { Stop-Process -Id $running.Id -Force -ErrorAction SilentlyContinue }
  }
  foreach ($saved in Get-ChildItem $backup) { Copy-Item $saved.FullName (Join-Path $dir $saved.Name) -Force }
  $restored = $true
  foreach ($saved in Get-ChildItem $backup) { if ((Get-FileHash $saved.FullName).Hash -ne (Get-FileHash (Join-Path $dir $saved.Name)).Hash) { $restored = $false } }
  if ($restored) { Remove-Item $backup -Recurse } else { "RESTORE FAILED, the copies are in $backup" }
  "Restored: $restored"
}
if (-not $ok) { throw 'The clock did not keep time.' }
