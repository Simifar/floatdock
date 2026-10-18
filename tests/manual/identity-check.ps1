# Manual check carried over from the 2026-10-16 session. Backs up and restores settings.json; close FloatDock first.
param([string]$Exe, [string]$Out)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, UIAutomationClient
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class IC { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$calcWasRunning = [bool](Get-Process CalculatorApp -ErrorAction SilentlyContinue)
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); Copy-Item $settings $backup
try {
  '{"Theme":2,"FirstRunCompleted":true,"IconSize":52,"Language":2}' | Set-Content $settings
  if (-not $calcWasRunning) { Start-Process 'explorer.exe' 'shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App'; Start-Sleep -Seconds 3 }
  $p = Start-Process $Exe -PassThru; Start-Sleep -Seconds 8
  $p.Refresh(); $h = $p.MainWindowHandle
  $root = [Windows.Automation.AutomationElement]::FromHandle($h)
  $buttons = $root.FindAll([Windows.Automation.TreeScope]::Descendants,
    (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
  "Buttons: " + (($buttons | ForEach-Object { $_.Current.Name }) -join ' | ')
  $cache = Join-Path $dir 'apps-cache.json'
  if (Test-Path $cache) {
    $apps = Get-Content $cache -Raw | ConvertFrom-Json
    "Catalog: $($apps.Count) apps, packaged $(@($apps | Where-Object IsPackaged).Count), with exe $(@($apps | Where-Object ExecutablePath).Count)"
    "Sample: " + ((@($apps | Where-Object { $_.Name -match 'Калькулятор|Terminal|Терминал|Telegram|Firefox|Steam' }) | ForEach-Object { "$($_.Name) [$($_.Id)]" }) -join '; ')
  } else { 'Catalog cache missing' }
  $r = New-Object IC+R; [IC]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.Rt - $r.L; $hh = $r.B - $r.T
  $bmp = New-Object Drawing.Bitmap $w, $hh; $g = [Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $bmp.Save($Out)
  [IC]::PostMessageW($h, 0x10, 0, 0) | Out-Null
  if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force }
} finally {
  if (-not $calcWasRunning) { Get-Process CalculatorApp -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null } ; Start-Sleep 1; $f = [IC]::FindWindowW('ApplicationFrameWindow', 'Калькулятор'); if ($f -ne [IntPtr]::Zero) { [IC]::PostMessageW($f, 0x10, 0, 0) | Out-Null } }
  Copy-Item $backup $settings -Force; Remove-Item $backup
}
