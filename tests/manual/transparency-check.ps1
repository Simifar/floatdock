# Manual check carried over from the 2026-10-16 session. Backs up and restores settings.json; close FloatDock first.
param([string]$Exe, [string]$Out, [int]$Theme = 2)
# Puts a magenta window behind the dock, then samples pixels above the shelf and outside the dock window.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class DT { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(System.Drawing.Point p);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); Copy-Item $settings $backup
$form = New-Object Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.BackColor = [Drawing.Color]::Magenta; $form.StartPosition = 'Manual'
$form.Bounds = New-Object Drawing.Rectangle 600, 800, 720, 280; $form.ShowInTaskbar = $false
function Pump([int]$ms) { $w = [Diagnostics.Stopwatch]::StartNew(); while ($w.ElapsedMilliseconds -lt $ms) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 } }
try {
  ('{"Theme":' + $Theme + ',"FirstRunCompleted":true,"IconSize":52}') | Set-Content $settings
  $form.Show(); Pump 500
  $p = Start-Process $Exe -PassThru; Pump 5000
  $p.Refresh(); $h = $p.MainWindowHandle
  $r = New-Object DT+R; [DT]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap 720, 280; $g = [Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen(600, 800, 0, 0, $bmp.Size); $bmp.Save($Out)
  $cx = [int](($r.L + $r.Rt) / 2)
  $inside = $bmp.GetPixel($cx - 600, $r.T + 6 - 800); $outside = $bmp.GetPixel($r.L - 10 - 600, $r.T + 6 - 800)
  "DockRect=$($r.L),$($r.T),$($r.Rt),$($r.B) AboveShelf=$($inside.Name) OutsideDock=$($outside.Name)"
  $hit = [DT]::GetAncestor([DT]::WindowFromPoint((New-Object Drawing.Point $cx, ($r.T + 6))), 2)
  "WindowAtAboveShelf=" + $(if ($hit -eq $h) { 'dock' } elseif ($hit -eq $form.Handle) { 'magenta form' } else { "other $hit" })
  [DT]::PostMessageW($h, 0x10, 0, 0) | Out-Null
  if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force }
} finally { $form.Close(); Copy-Item $backup $settings -Force; Remove-Item $backup }
