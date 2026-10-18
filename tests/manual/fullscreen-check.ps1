# Stage 7 check: the dock hides while a borderless full-screen window is in front and returns afterwards.
# Backs up and restores settings.json; close FloatDock first. Opens a black full-screen window of its own for a few seconds.
param([string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class FS {
 [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int s); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null; Copy-Item $settings $backup
function Pump([int]$ms) { $w = [Diagnostics.Stopwatch]::StartNew(); while ($w.ElapsedMilliseconds -lt $ms) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 } }
function DockVisible { $h = [FS]::FindWindowW([NullString]::Value, 'FloatDock'); $h -ne [IntPtr]::Zero -and [FS]::IsWindowVisible($h) }
function State { $s = 0; [void][FS]::SHQueryUserNotificationState([ref]$s); $s }
$form = $null
try {
  '{"FirstRunCompleted":true,"Mode":0}' | Set-Content $settings
  $p = Start-Process $Exe -PassThru; Pump 6000
  "Before: dockVisible=$(DockVisible) notificationState=$(State)"
  $form = New-Object Windows.Forms.Form
  $form.FormBorderStyle = 'None'; $form.BackColor = [Drawing.Color]::Black; $form.StartPosition = 'Manual'; $form.TopMost = $true
  $form.Bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds; $form.Show(); $form.Activate(); Pump 3500
  "Full-screen window: dockVisible=$(DockVisible) notificationState=$(State)"
  $form.Close(); $form = $null; Pump 3500
  "After: dockVisible=$(DockVisible) notificationState=$(State)"
  Stop-Process -Id $p.Id -Force
} finally {
  if ($form) { $form.Close() }
  Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
  Copy-Item (Join-Path $backup 'settings.json') $settings -Force; Remove-Item $backup -Recurse
}
