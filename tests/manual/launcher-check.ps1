# Stage 5 check: opens the app menu with its shortcut, measures the time, searches, closes it. Needs pwsh and a visible desktop.
# Backs up and restores settings.json, launch-counts.json and apps-first-seen.json; close FloatDock first. The mouse is not used.
param([string]$Exe, [string]$OutDir = $env:TEMP, [int]$Theme = 2, [int]$Opens = 5)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class LC {
 [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'
$settings = Join-Path $dir 'settings.json'; $counts = Join-Path $dir 'launch-counts.json'; $log = Join-Path $dir 'logs\floatdock.log'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
$seen = Join-Path $dir 'apps-first-seen.json'
Copy-Item $settings $backup; if (Test-Path $counts) { Copy-Item $counts $backup }; if (Test-Path $seen) { Copy-Item $seen $backup }
$A = [Windows.Automation.AutomationElement]
$title = 'FloatDock Приложения'
function Chord([byte[]]$keys) {
  foreach ($k in $keys) { [LC]::keybd_event($k, 0, 0, [UIntPtr]::Zero) }
  Start-Sleep -Milliseconds 30
  foreach ($k in ($keys | Sort-Object -Descending)) { [LC]::keybd_event($k, 0, 2, [UIntPtr]::Zero) }
}
function Toggle { Chord @(0x11, 0x12, 0x41) }   # Ctrl+Alt+A, the default
function MenuVisible { $h = [LC]::FindWindowW([NullString]::Value, $title); $h -ne [IntPtr]::Zero -and [LC]::IsWindowVisible($h) }
function WaitMenu([bool]$visible, [int]$timeoutMs = 3000) {
  $w = [Diagnostics.Stopwatch]::StartNew()
  while ($w.ElapsedMilliseconds -lt $timeoutMs) { if ((MenuVisible) -eq $visible) { return $w.ElapsedMilliseconds }; Start-Sleep -Milliseconds 5 }
  return -1
}
function Shot([string]$name) {
  $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object Drawing.Bitmap $b.Width, $b.Height; $g = [Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size); $path = Join-Path $OutDir $name; $bmp.Save($path); $path
}
function Menu { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $title))) }
function Search { $m = Menu; $m.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'LauncherSearch'))) }
function Items { $m = Menu
  $list = $m.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'LauncherApps')))
  $list.FindAll([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))) }
function SetQuery([string]$text) { (Search).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($text); Start-Sleep -Milliseconds 400 }
try {
  ('{"Theme":' + $Theme + ',"FirstRunCompleted":true,"IconSize":52,"Language":2}') | Set-Content $settings
  $logLines = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
  $p = Start-Process $Exe -PassThru; Start-Sleep -Seconds 7
  "Hotkey registered: " + (-not ((Get-Content $log | Select-Object -Skip $logLines) -match "launcher-hotkey-unavailable"))
  for ($i = 1; $i -le $Opens; $i++) {
    $t = [Diagnostics.Stopwatch]::StartNew(); Toggle; $ms = WaitMenu $true
    "Open #$i visible after ${ms} ms (polled, includes key injection)"
    Start-Sleep -Milliseconds 600
    if ($i -eq 1) { "Screenshot: " + (Shot "launcher-open-$Theme.png") }
    "  foreground is menu: " + ([LC]::GetForegroundWindow() -eq [LC]::FindWindowW([NullString]::Value, $title))
    Toggle; "  closed after " + (WaitMenu $false) + " ms"; Start-Sleep -Milliseconds 400
  }
  Toggle; WaitMenu $true | Out-Null; Start-Sleep -Milliseconds 600
  "All apps: " + (Items).Count
  SetQuery 'калькулятор'; "Query 'калькулятор': " + ((Items | ForEach-Object { $_.Current.Name } | Select-Object -First 3) -join ' | ')
  SetQuery 'rfkm';        "Query 'rfkm' (wrong layout): " + ((Items | ForEach-Object { $_.Current.Name } | Select-Object -First 3) -join ' | ')
  SetQuery 'calc';        "Query 'calc': " + ((Items | ForEach-Object { $_.Current.Name } | Select-Object -First 3) -join ' | ')
  SetQuery 'zzzzqq';      "Query 'zzzzqq': " + (Items).Count + " items"
  "Screenshot: " + (Shot "launcher-empty-$Theme.png")
  SetQuery 'calc'
  [Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 400
  "After Esc: query='" + ((Search).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value) + "' menuVisible=" + (MenuVisible)
  [Windows.Forms.SendKeys]::SendWait('{ESC}'); "Second Esc closes: " + (WaitMenu $false)
  # The dock button opens the menu; typing searches from the grid too; Enter starts the best match, which is then counted.
  $dock = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
    (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'FloatDock')))))
  $button = $dock.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'FloatDock.apps')))
  "Apps button: " + $button.Current.Name
  $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); "Opened by the button: " + (WaitMenu $true)
  Start-Sleep -Milliseconds 800
  [Windows.Forms.SendKeys]::SendWait('{DOWN}'); Start-Sleep -Milliseconds 300
  "Focus after Down: " + [Windows.Automation.AutomationElement]::FocusedElement.Current.Name
  [Windows.Forms.SendKeys]::SendWait('кальк'); Start-Sleep -Milliseconds 500
  "Typed from the grid: query='" + ((Search).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value) + "' first=" + ((Items | Select-Object -First 1).Current.Name)
  $before = @(Get-Process CalculatorApp -ErrorAction SilentlyContinue).Count
  [Windows.Forms.SendKeys]::SendWait('{ENTER}'); "Closed by Enter: " + (WaitMenu $false); Start-Sleep -Seconds 3
  "Calculator processes: before=$before after=" + @(Get-Process CalculatorApp -ErrorAction SilentlyContinue).Count
  "Counts file: " + (Get-Content $counts -Raw)
  Toggle; WaitMenu $true | Out-Null; Start-Sleep -Milliseconds 800
  $freq = (Menu).FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'LauncherFrequent')))
  "Frequent: " + (($freq.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join ' | ')
  "Screenshot: " + (Shot "launcher-frequent-$Theme.png")
  # A classic Start menu entry (a known-folder item, not a packaged app) starts too.
  $charmapBefore = @(Get-Process charmap -ErrorAction SilentlyContinue).Count
  SetQuery 'Таблица символов'; "Classic app match: " + ((Items | Select-Object -First 1).Current.Name)
  [Windows.Forms.SendKeys]::SendWait('{ENTER}'); WaitMenu $false | Out-Null; Start-Sleep -Seconds 2
  "Character Map processes: before=$charmapBefore after=" + @(Get-Process charmap -ErrorAction SilentlyContinue).Count
  if ($charmapBefore -eq 0) { Get-Process charmap -ErrorAction SilentlyContinue | Stop-Process -Force }
  # Context menu from the keyboard (Shift+F10): pin to the dock.
  Toggle; WaitMenu $true | Out-Null; Start-Sleep -Milliseconds 800
  [Windows.Forms.SendKeys]::SendWait('{DOWN}'); Start-Sleep -Milliseconds 400
  "Focus before the menu: " + [Windows.Automation.AutomationElement]::FocusedElement.Current.Name
  [Windows.Forms.SendKeys]::SendWait('+{F10}'); Start-Sleep -Milliseconds 600
  $menuItems = (Menu).FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::MenuItem)))
  "Context menu: " + (($menuItems | ForEach-Object { $_.Current.Name }) -join ' | ')
  "Screenshot: " + (Shot "launcher-menu-$Theme.png")
  $pin = $menuItems | Where-Object { $_.Current.Name -eq 'Закрепить в Dock' } | Select-Object -First 1
  if ($pin) { $pin.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800 }
  "Pinned after menu: " + (((Get-Content $settings -Raw | ConvertFrom-Json).Pins | ForEach-Object { $_.DisplayName + ' [' + $_.Key + ']' }) -join ' | ')
  [Windows.Forms.SendKeys]::SendWait('{ESC}'); WaitMenu $false | Out-Null
  # A new Start menu entry shows up without restarting FloatDock.
  $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'FloatDock Check App.lnk'
  $shell = New-Object -ComObject WScript.Shell; $s = $shell.CreateShortcut($lnk); $s.TargetPath = "$env:WINDIR\System32\notepad.exe"; $s.Save()
  Start-Sleep -Seconds 14
  Toggle; WaitMenu $true | Out-Null; Start-Sleep -Milliseconds 600; SetQuery 'FloatDock Check'
  "New app found: " + ((Items | ForEach-Object { $_.Current.Name }) -join ' | ')
  "New mark on it: " + ((Items | Select-Object -First 1).Current.Name -like '*, Новое')
  [Windows.Forms.SendKeys]::SendWait('{ESC}{ESC}'); WaitMenu $false | Out-Null
  Remove-Item $lnk   # the shortcut this check created
  Start-Sleep -Seconds 14
  Toggle; WaitMenu $true | Out-Null; Start-Sleep -Milliseconds 600; SetQuery 'FloatDock Check'
  "After removing it: " + (Items).Count + " items"
  [Windows.Forms.SendKeys]::SendWait('{ESC}{ESC}'); WaitMenu $false | Out-Null
  # Hide an app from the menu with the keyboard context menu; it disappears and is saved (settings.json is restored below).
  Toggle; WaitMenu $true | Out-Null; Start-Sleep -Milliseconds 600; SetQuery 'калькулятор'
  [Windows.Forms.SendKeys]::SendWait('{DOWN}'); Start-Sleep -Milliseconds 400
  [Windows.Forms.SendKeys]::SendWait('+{F10}'); Start-Sleep -Milliseconds 600
  $hide = (Menu).FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::MenuItem))) |
    Where-Object { $_.Current.Name -eq 'Скрыть из меню' } | Select-Object -First 1
  "Hide item offered: " + ($null -ne $hide)
  # Keys, not InvokePattern: invoking the item through UI Automation makes the window lose the focus and the menu closes, real input does not.
  if ($hide) { [Windows.Forms.SendKeys]::SendWait("{DOWN}{ENTER}"); Start-Sleep -Milliseconds 900 }
  "Menu still open after hiding: " + (MenuVisible)
  "Hidden in settings: " + (@((Get-Content $settings -Raw | ConvertFrom-Json).HiddenApps).Count)
  "Calculator still listed after hiding: " + (@(Items | Where-Object { $_.Current.Name -like 'Калькулятор*' }).Count -gt 0)
  [Windows.Forms.SendKeys]::SendWait('{ESC}{ESC}'); WaitMenu $false | Out-Null
  if ($before -eq 0) { Get-Process CalculatorApp -ErrorAction SilentlyContinue | Stop-Process -Force }   # only the one this check started
  "Log: " + ((Select-String -Path $log -Pattern 'launcher-open' | Select-Object -Last $Opens | ForEach-Object { ($_.Line | ConvertFrom-Json).eventName }) -join ' ')
  $p.Refresh(); [LC]::PostMessageW($p.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force }
} finally {
  Copy-Item (Join-Path $backup 'settings.json') $settings -Force
  if (Test-Path (Join-Path $backup 'launch-counts.json')) { Copy-Item (Join-Path $backup 'launch-counts.json') $counts -Force } elseif (Test-Path $counts) { Remove-Item $counts }
  if (Test-Path (Join-Path $backup 'apps-first-seen.json')) { Copy-Item (Join-Path $backup 'apps-first-seen.json') $seen -Force } elseif (Test-Path $seen) { Remove-Item $seen }
  Remove-Item $backup -Recurse
}
