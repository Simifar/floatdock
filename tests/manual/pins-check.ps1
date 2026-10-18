# Manual check carried over from the 2026-10-16 session. Backs up and restores settings.json; close FloatDock first.
param([string]$Exe, [string]$Out)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing, UIAutomationClient
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PC { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); Copy-Item $settings $backup
$A = [Windows.Automation.AutomationElement]
function Find($root, [string]$name) {
  $cond = New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name)
  $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
}
function Invoke($element) { $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Pins { (Get-Content $settings -Raw | ConvertFrom-Json).Pins | ForEach-Object { $_.DisplayName } }
try {
  '{"Theme":2,"FirstRunCompleted":true,"IconSize":44,"Language":2}' | Set-Content $settings
  $p = Start-Process $Exe -ArgumentList '--settings' -PassThru; Start-Sleep -Seconds 6
  $root = $A::RootElement
  $win = $root.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
    (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)),
    (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'Настройки FloatDock')))))
  if (-not $win) { throw 'Settings window not found.' }
  # The pins live on their own page of the settings window.
  (Find $win 'Закреплённые').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Seconds 1
  Invoke (Find $win 'Перенести из панели задач Windows'); Start-Sleep -Seconds 3
  "Imported: " + ((Pins) -join ' | ')
  $win = $root.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'Настройки FloatDock')))
  "Status: " + (Find $win 'Перенесено: 8').Current.Name
  $first = (Pins)[0]; $second = (Pins)[1]
  Invoke (Find $win "Ниже: $first"); Start-Sleep -Seconds 1
  "AfterDown: " + ((Pins)[0..1] -join ' | ')
  $win = $root.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'Настройки FloatDock')))
  Invoke (Find $win "Открепить: $second"); Start-Sleep -Seconds 1
  "AfterUnpin: " + ((Pins) -join ' | ')
  $p.Refresh(); $dock = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
  $buttons = $dock.FindAll([Windows.Automation.TreeScope]::Descendants,
    (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
  "Dock: " + (($buttons | ForEach-Object { $_.Current.Name }) -join ' | ')
  $r = New-Object PC+R; [PC]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($r.Rt - $r.L + 40), ($r.B - $r.T + 40); $g = [Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L - 20, $r.T - 20, 0, 0, $bmp.Size); $bmp.Save($Out)
  [PC]::PostMessageW($p.MainWindowHandle, 0x10, 0, 0) | Out-Null
  if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force }
} finally { Copy-Item $backup $settings -Force; Remove-Item $backup }
