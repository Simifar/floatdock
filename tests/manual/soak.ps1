# Soak test: runs the dock for a long time with some activity and samples memory, handles, GDI and USER objects and CPU into a CSV.
# Every cycle: opens and closes the app menu with its shortcut (Ctrl+Alt+A) and starts and closes a Character Map window.
# Backs up and restores settings.json; close FloatDock first; do not use another FloatDock while it runs.
#   pwsh tests/manual/soak.ps1 -Exe <FloatDock.App.exe> -Minutes 480 -OutCsv soak.csv      (the release gate is 8 hours)
param([string]$Exe, [int]$Minutes = 480, [int]$IntervalSeconds = 30, [string]$OutCsv = (Join-Path $env:TEMP 'floatdock-soak.csv'))
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SK {
 [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null; Copy-Item $settings $backup
function Chord([byte[]]$keys) {
  foreach ($k in $keys) { [SK]::keybd_event($k, 0, 0, [UIntPtr]::Zero) }
  Start-Sleep -Milliseconds 30
  foreach ($k in ($keys | Sort-Object -Descending)) { [SK]::keybd_event($k, 0, 2, [UIntPtr]::Zero) }
}
$map = $null
try {
  '{"FirstRunCompleted":true,"ShowClock":true,"IconSize":52}' | Set-Content $settings
  $p = Start-Process $Exe -PassThru; Start-Sleep -Seconds 8
  $guardian = $null
  'time,minutes,workingSetMB,privateMB,handles,gdi,user,cpuSeconds,threads' | Set-Content $OutCsv
  $started = Get-Date; $lastCpu = 0
  while (((Get-Date) - $started).TotalMinutes -lt $Minutes) {
    $p.Refresh()
    if ($p.HasExited) { "The dock exited after $([int]((Get-Date) - $started).TotalMinutes) min (code $($p.ExitCode))"; break }
    Chord @(0x11, 0x12, 0x41); Start-Sleep -Seconds 2; Chord @(0x11, 0x12, 0x41); Start-Sleep -Seconds 1      # open and close the menu
    if ($map -and -not $map.HasExited) { Stop-Process -Id $map.Id -Force; $map = $null } else { $map = Start-Process charmap -PassThru }
    Start-Sleep -Seconds ([Math]::Max(1, $IntervalSeconds - 3))
    $p.Refresh()
    # Invariant culture: a comma as the decimal separator would break the CSV columns.
    $line = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:o},{1:N1},{2:N1},{3:N1},{4},{5},{6},{7:N1},{8}', (Get-Date), ((Get-Date) - $started).TotalMinutes, ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB),
      $p.HandleCount, [SK]::GetGuiResources($p.Handle, 0), [SK]::GetGuiResources($p.Handle, 1), $p.TotalProcessorTime.TotalSeconds, $p.Threads.Count)
    Add-Content $OutCsv $line
  }
  $rows = Import-Csv $OutCsv
  if ($rows.Count -ge 4) {
    $quarter = [int]($rows.Count / 4)
    function Avg($set, $name) { ($set | ForEach-Object { [double]$_.$name } | Measure-Object -Average).Average }
    $early = $rows[0..($quarter - 1)]; $late = $rows[($rows.Count - $quarter)..($rows.Count - 1)]
    foreach ($metric in 'workingSetMB', 'privateMB', 'handles', 'gdi', 'user', 'threads') {
      [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0,-13} first quarter {1,9:N1}   last quarter {2,9:N1}', $metric, (Avg $early $metric), (Avg $late $metric))
    }
    $cpu = ([double]$rows[-1].cpuSeconds - [double]$rows[0].cpuSeconds) / (([double]$rows[-1].minutes - [double]$rows[0].minutes) * 60)
    [string]::Format([Globalization.CultureInfo]::InvariantCulture, 'CPU while exercised: {0:N2} % of one core', $cpu * 100)
  }
  "Samples: $($rows.Count), CSV: $OutCsv"
} finally {
  if ($map -and -not $map.HasExited) { Stop-Process -Id $map.Id -Force }
  Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
  Copy-Item (Join-Path $backup 'settings.json') $settings -Force; Remove-Item $backup -Recurse
}
