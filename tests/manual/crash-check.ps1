# Deliberate crash (--crash-test throws on the UI thread 3 s after start) while the taskbar is replaced: the process must die,
# leave a minidump in %LocalAppData%\FloatDock\crashes, log the fatal error, and the taskbar must be visible again.
# Backs up and restores settings.json; close FloatDock first. Needs the taskbar to be visible when it starts.
param([string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class CK {
 [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'; $log = Join-Path $dir 'logs\floatdock.log'
$crashes = Join-Path $dir 'crashes'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null; Copy-Item $settings $backup
function TaskbarVisible { $h = [CK]::FindWindowW('Shell_TrayWnd', [NullString]::Value); $h -ne [IntPtr]::Zero -and [CK]::IsWindowVisible($h) }
try {
  $before = if (Test-Path $crashes) { @(Get-ChildItem $crashes -Filter *.dmp).Count } else { 0 }
  $logLines = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
  '{"ReplaceTaskbar":true,"FirstRunCompleted":true}' | Set-Content $settings
  "Taskbar visible before: $(TaskbarVisible)"
  $p = Start-Process $Exe -ArgumentList '--crash-test' -PassThru; Start-Sleep -Seconds 3
  "Taskbar visible while replaced: $(TaskbarVisible)"
  $exited = $p.WaitForExit(30000); Start-Sleep -Seconds 3
  "Process exited: $exited (exit code $($p.ExitCode))"
  "Taskbar visible after the crash: $(TaskbarVisible)"
  $dumps = @(Get-ChildItem $crashes -Filter *.dmp -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
  "Dumps: before=$before after=$($dumps.Count); newest=$($dumps[-1].Name) size=$([math]::Round($dumps[-1].Length / 1MB, 1)) MB"
  "Log: " + ((Get-Content $log | Select-Object -Skip $logLines | ForEach-Object { ($_ | ConvertFrom-Json) } | Where-Object { $_.level -in 'Fatal', 'Info' -and $_.eventName -match 'unhandled|crash-dump|taskbar' } | ForEach-Object { $_.eventName }) -join ', ')
} finally {
  Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
  Copy-Item (Join-Path $backup 'settings.json') $settings -Force; Remove-Item $backup -Recurse
  if (-not (TaskbarVisible)) { & $Exe --restore-taskbar }
}
