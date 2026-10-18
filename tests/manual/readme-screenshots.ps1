# Takes the pictures in docs/images (used by README.md and README.ru.md) from the real app: the dock in the dark and light theme,
# the app menu and two pages of Settings. A plain gradient window is put behind them so that nothing from your desktop is in view.
# Needs pwsh and a visible desktop (1920x1080 at 100 % gives the sizes the README uses); close FloatDock first. The mouse is not used.
# Backs up and restores settings.json, launch-counts.json and apps-first-seen.json. Starts and stops Character Map and Calculator of its own.
# The dock shows every window that is open, so close apps you do not want in the pictures first.
# -Language 1 (English, the default) writes docs/images/*.png; -Language 2 (Russian) writes docs/images/ru/*.png for README.ru.md.
param([string]$Exe, [string]$OutDir = (Join-Path $PSScriptRoot '../../docs/images'), [int]$Language = 1)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class RS { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
if ($Language -eq 2) { $OutDir = Join-Path $OutDir 'ru' }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'
$files = 'settings.json', 'launch-counts.json', 'apps-first-seen.json' | ForEach-Object { Join-Path $dir $_ }
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
foreach ($f in $files) { if (Test-Path $f) { Copy-Item $f $backup } }
$A = [Windows.Automation.AutomationElement]
$started = @(); $backdrop = $null; $p = $null; $calcBefore = @(Get-Process CalculatorApp -ErrorAction SilentlyContinue).Id

function Chord([byte[]]$keys) {
  foreach ($k in $keys) { [RS]::keybd_event($k, 0, 0, [UIntPtr]::Zero) }
  Start-Sleep -Milliseconds 30
  foreach ($k in ($keys | Sort-Object -Descending)) { [RS]::keybd_event($k, 0, 2, [UIntPtr]::Zero) }
}
function Crop([int]$x, [int]$y, [int]$w, [int]$h, [string]$name) {
  $bmp = New-Object Drawing.Bitmap $w, $h; $g = [Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $bmp.Save((Join-Path $OutDir $name), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  "Saved $name ($w x $h)"
}
function ByName($root, [string]$name, $scope = [Windows.Automation.TreeScope]::Descendants) {
  $root.FindFirst($scope, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name)))
}
function Start-Dock([string]$json) {
  Remove-Item (Join-Path $dir 'startup-attempts.json') -ErrorAction SilentlyContinue
  $json | Set-Content (Join-Path $dir 'settings.json')
  $script:p = Start-Process $Exe -PassThru
  Start-Sleep -Seconds 8
}
function Stop-Dock { if ($script:p -and -not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force; Start-Sleep -Seconds 1 } }
function DockRect {
  $w = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
    (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'FloatDock')))))
  $w.Current.BoundingRectangle
}

# A plain gradient behind everything, so no desktop content ends up in a picture.
$backdropScript = @'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$b = [Windows.Forms.Screen]::PrimaryScreen.Bounds
$f = New-Object Windows.Forms.Form
$f.FormBorderStyle = 'None'; $f.StartPosition = 'Manual'; $f.Bounds = $b; $f.ShowInTaskbar = $false
$bmp = New-Object Drawing.Bitmap $b.Width, $b.Height; $g = [Drawing.Graphics]::FromImage($bmp)
$brush = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0, 0), [Drawing.Point]::new($b.Width, $b.Height), [Drawing.Color]::FromArgb(255, 24, 38, 82), [Drawing.Color]::FromArgb(255, 108, 62, 128))
$g.FillRectangle($brush, 0, 0, $b.Width, $b.Height)
$f.BackgroundImage = $bmp
# On top of the ordinary windows, but not above the dock, the app menu or the Settings window that are opened later.
$f.Add_Shown({ $f.Activate(); $f.TopMost = $true; $f.TopMost = $false })
[Windows.Forms.Application]::Run($f)
'@
$pins = @(
  @{ Key = 'c:\windows\explorer.exe'; DisplayName = 'File Explorer'; Target = @{ Kind = 0; Value = 'C:\Windows\explorer.exe' } },
  @{ Key = 'AUMID:MICROSOFT.WINDOWSCALCULATOR_8WEKYB3D8BBWE!APP'; DisplayName = 'Calculator'; Target = @{ Kind = 2; Value = 'Microsoft.WindowsCalculator_8wekyb3d8bbwe!App' } },
  @{ Key = 'c:\windows\system32\charmap.exe'; DisplayName = 'Character Map'; Target = @{ Kind = 0; Value = 'C:\Windows\System32\charmap.exe' } },
  @{ Key = 'c:\windows\system32\taskmgr.exe'; DisplayName = 'Task Manager'; Target = @{ Kind = 0; Value = 'C:\Windows\System32\taskmgr.exe' } },
  @{ Key = 'c:\windows\system32\cmd.exe'; DisplayName = 'Command Prompt'; Target = @{ Kind = 0; Value = 'C:\Windows\System32\cmd.exe' } }
)
function Settings([hashtable]$extra) {
  # HideInFullscreen is off because the backdrop window covers the whole display and would hide the dock.
  $base = @{ FirstRunCompleted = $true; Language = $Language; Pins = $pins; ShowClock = $true; ShowDesktopButton = $true; HideInFullscreen = $false }
  foreach ($k in $extra.Keys) { $base[$k] = $extra[$k] }
  $base | ConvertTo-Json -Depth 6
}

try {
  Remove-Item (Join-Path $dir 'launch-counts.json'), (Join-Path $dir 'apps-first-seen.json') -ErrorAction SilentlyContinue
  # Windows to give the dock something to show: two apps that are pinned (so they get the "running" mark) and nothing else of ours.
  $started += Start-Process charmap -PassThru
  $started += Start-Process calc -PassThru
  Start-Sleep -Seconds 3
  $backdrop = Start-Process pwsh -ArgumentList '-NoProfile', '-EncodedCommand', ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($backdropScript))) -PassThru
  Start-Sleep -Seconds 3

  foreach ($theme in @(@('dark', 2), @('light', 1))) {
    Start-Dock (Settings @{ Theme = $theme[1] })
    $r = DockRect
    Crop ([int]$r.X - 30) ([int]$r.Y + 10) ([int]$r.Width + 60) ([int]$r.Height - 10) ("dock-$($theme[0]).png")
    if ($theme[0] -eq 'dark') {
      # The app menu opens over the same backdrop; its blur is the real one.
      Chord @(0x11, 0x12, 0x41); Start-Sleep -Seconds 2
      Crop 320 24 1280 350 'app-menu.png'
      [Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Seconds 1
    }
    Stop-Dock
  }

  # Settings, dark: one picture per page that is shown in the README.
  # Settings shows the default for hiding in full-screen apps, so it is on here (the dock is hidden by the backdrop, which Settings does not need).
  Start-Dock (Settings @{ Theme = 2; HideInFullscreen = $true })
  [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Activate').Set() | Out-Null; Start-Sleep -Seconds 3
  $win = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
    (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $(if ($Language -eq 2) { 'Настройки FloatDock' } else { 'FloatDock Settings' }))))))
  if (-not $win) { throw 'Settings window not found.' }
  $win.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
  Start-Sleep -Milliseconds 800
  $wr = $win.Current.BoundingRectangle
  $pages = if ($Language -eq 2) { @(@('Внешний вид', 'settings-appearance.png'), @('Док', 'settings-dock.png')) } else { @(@('Appearance', 'settings-appearance.png'), @('Dock', 'settings-dock.png')) }
  foreach ($page in $pages) {
    $item = ByName $win $page[0]
    $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 900
    Crop ([int]$wr.X) ([int]$wr.Y) ([int]$wr.Width) ([int]$wr.Height) $page[1]
  }
  Stop-Dock
} finally {
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
  foreach ($s in $started) { if ($s -and -not $s.HasExited) { Stop-Process -Id $s.Id -Force } }
  # Calculator starts through a stub, so stop only the CalculatorApp processes that did not exist before.
  Get-Process CalculatorApp -ErrorAction SilentlyContinue | Where-Object { $calcBefore -notcontains $_.Id } | Stop-Process -Force
  if ($backdrop -and -not $backdrop.HasExited) { Stop-Process -Id $backdrop.Id -Force }
  foreach ($f in $files) {
    $saved = Join-Path $backup (Split-Path $f -Leaf)
    if (Test-Path $saved) { Copy-Item $saved $f -Force } elseif (Test-Path $f) { Remove-Item $f }
  }
  Remove-Item $backup -Recurse
}
