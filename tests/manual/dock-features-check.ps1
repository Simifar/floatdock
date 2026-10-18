# Stage 6 check: dock items, indicators, multi-window preview, Downloads stack. Needs pwsh and a visible desktop; no mouse is used.
# Backs up and restores settings.json; close FloatDock first. Starts and stops two Character Map windows of its own.
param([string]$Exe, [string]$OutDir = $env:TEMP, [int]$Theme = 2)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null; Copy-Item $settings $backup
$A = [Windows.Automation.AutomationElement]
function Shot([string]$name) {
  $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object Drawing.Bitmap $b.Width, $b.Height; $g = [Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size); $path = Join-Path $OutDir $name; $bmp.Save($path); $path
}
function Dock($p) { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
  (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'FloatDock'))))) }
function Buttons($dock) { $dock.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button))) }
function Names($dock) { (Buttons $dock | ForEach-Object { $_.Current.Name }) -join ' | ' }
function Invoke($e) { $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
$maps = @()
try {
  ('{"Theme":' + $Theme + ',"FirstRunCompleted":true,"IconSize":52,"Language":2,"ShowDesktopButton":true,"ShowClock":true,"ShowQuickSettings":true,"ShowNotifications":true}') | Set-Content $settings
  $p = Start-Process $Exe -PassThru; Start-Sleep -Seconds 7
  $dock = Dock $p
  "Items without apps: " + (Names $dock)
  $maps += Start-Process charmap -PassThru; Start-Sleep -Milliseconds 800; $maps += Start-Process charmap -PassThru; Start-Sleep -Seconds 3
  "Items with two Character Map windows: " + (Names $dock)
  "Screenshot: " + (Shot "dock-items-$Theme.png")
  $map = Buttons $dock | Where-Object { $_.Current.Name -like 'Таблица символов*' } | Select-Object -First 1
  "Help text: '" + $map.Current.HelpText + "'"
  Invoke $map; Start-Sleep -Seconds 2
  "Screenshot: " + (Shot "dock-preview-$Theme.png")
  $preview = $A::RootElement.FindAll([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))
  "Windows of the dock process: " + (($preview | ForEach-Object { '[' + $_.Current.Name + ' ' + $_.Current.BoundingRectangle + ']' }) -join ' ')
  [Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 400
  $downloads = Buttons $dock | Where-Object { $_.Current.AutomationId -eq 'FloatDock.downloads' } | Select-Object -First 1
  if ($downloads) { Invoke $downloads; Start-Sleep -Seconds 2; "Screenshot: " + (Shot "dock-downloads-$Theme.png"); [Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 400 }
  "Recycle Bin: " + ((Buttons $dock | Where-Object { $_.Current.AutomationId -eq 'FloatDock.bin' }) | ForEach-Object { $_.Current.Name })
  $p.Refresh(); [void][Windows.Forms.Application];
  Stop-Process -Id $p.Id -Force
} finally {
  foreach ($m in $maps) { if ($m -and -not $m.HasExited) { Stop-Process -Id $m.Id -Force } }
  Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
  Copy-Item (Join-Path $backup 'settings.json') $settings -Force; Remove-Item $backup -Recurse
}
