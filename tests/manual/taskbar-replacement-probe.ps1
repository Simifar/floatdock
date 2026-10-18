# Probe, not a pass/fail check: with the Windows taskbar replaced by the dock, which Windows shell features still answer?
# Sends Win-key shortcuts and reports which window gets the focus. No mouse. Needs pwsh and a visible desktop; close FloatDock first.
# Backs up and restores settings.json; the taskbar is restored on exit (and with --restore-taskbar if the probe fails midway).
param([string]$Exe)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class TP {
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
 [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
Copy-Item $settings $backup
function Foreground {
  $h = [TP]::GetForegroundWindow(); $procId = [uint32]0; [void][TP]::GetWindowThreadProcessId($h, [ref]$procId)
  $name = try { (Get-Process -Id $procId).ProcessName } catch { '?' }
  $sb = New-Object Text.StringBuilder 120; [void][TP]::GetWindowTextW($h, $sb, 120)
  "$name '$($sb.ToString())'"
}
function Chord([byte[]]$keys) {
  foreach ($k in $keys) { [TP]::keybd_event($k, 0, 0, [UIntPtr]::Zero) }
  Start-Sleep -Milliseconds 60
  foreach ($k in ($keys | Sort-Object -Descending)) { [TP]::keybd_event($k, 0, 2, [UIntPtr]::Zero) }
}
function Probe([string]$label, [byte[]]$keys) {
  Chord $keys; Start-Sleep -Milliseconds 1500
  "{0,-10} -> {1}" -f $label, (Foreground)
  [Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 500; [Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 700
}
Add-Type -AssemblyName System.Windows.Forms
try {
  Remove-Item (Join-Path $dir 'startup-attempts.json') -ErrorAction SilentlyContinue
  '{"FirstRunCompleted":true,"Language":2,"ReplaceTaskbar":true,"Mode":1}' | Set-Content $settings
  $p = Start-Process $Exe -PassThru; Start-Sleep -Seconds 9
  "Taskbar window visible: " + [TP]::IsWindowVisible([TP]::FindWindowW('Shell_TrayWnd', $null))
  "Before: " + (Foreground)
  Probe 'Win'     @(0x5B)
  Probe 'Win+S'   @(0x5B, 0x53)
  Probe 'Win+Tab' @(0x5B, 0x09)
  Probe 'Win+A'   @(0x5B, 0x41)
  Probe 'Win+R'   @(0x5B, 0x52)
  Probe 'Win+N'   @(0x5B, 0x4E)
  Probe 'Win+B'   @(0x5B, 0x42)
  $p.Refresh(); $null = $p.CloseMainWindow(); if (-not $p.WaitForExit(6000)) { Stop-Process -Id $p.Id -Force }
  Start-Sleep -Seconds 2
  "Taskbar window visible after exit: " + [TP]::IsWindowVisible([TP]::FindWindowW('Shell_TrayWnd', $null))
} finally {
  # Settings first: nothing below may keep the user's own settings from coming back.
  if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { Get-Process FloatDock.App | Stop-Process -Force; Start-Sleep 3 }
  Copy-Item (Join-Path $backup 'settings.json') $settings -Force
  Remove-Item $backup -Recurse -ErrorAction SilentlyContinue
  if (-not [TP]::IsWindowVisible([TP]::FindWindowW('Shell_TrayWnd', $null))) { & $Exe --restore-taskbar; Start-Sleep 2 }
}