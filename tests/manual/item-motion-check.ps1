# Checks that an app's icon grows into the dock when it starts and shrinks away when it closes, and that the neighbours follow.
# Samples the icon's bounds with UI Automation; no mouse is used. Needs pwsh and a visible desktop; close FloatDock first.
# Backs up and restores settings.json. Starts and stops one Character Map window of its own.
param([string]$Exe, [string]$OutDir = $env:TEMP)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $dir 'settings.json'
$attempts = Join-Path $dir 'startup-attempts.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
$had = Test-Path $settings
if ($had) { Copy-Item $settings $backup }
$A = [Windows.Automation.AutomationElement]
function Dock($p) { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
  (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'FloatDock'))))) }
function Buttons($dock) { @($dock.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))) }
function Sample($dock) {
  $r = @{ Width = $null; Right = $null; Count = 0 }
  $buttons = Buttons $dock; $r.Count = $buttons.Count
  $i = 0
  foreach ($b in $buttons) {
    if ($b.Current.Name -like 'Таблица символов*') { $rect = $b.Current.BoundingRectangle; $r.Width = [math]::Round($rect.Width); $r.Right = [math]::Round($rect.Right); break }
    $i++
  }
  $r
}
function Track($dock, [string]$label, [scriptblock]$until) {
  $clock = [Diagnostics.Stopwatch]::StartNew(); $rows = @()
  while ($clock.ElapsedMilliseconds -lt 1500) {
    $s = Sample $dock; $rows += [pscustomobject]@{ Ms = $clock.ElapsedMilliseconds; Width = $s.Width; Count = $s.Count }
    if (& $until $s $clock.ElapsedMilliseconds) { break }
  }
  Write-Host ($label + ': ' + (($rows | ForEach-Object { "$($_.Ms)ms=$($_.Width)" }) -join ' '))
  $rows
}
$map = $null
try {
  Remove-Item $attempts -ErrorAction SilentlyContinue
  '{"Theme":2,"FirstRunCompleted":true,"IconSize":52,"Language":2}' | Set-Content $settings
  $p = Start-Process $Exe -PassThru; Start-Sleep -Seconds 7
  $dock = Dock $p
  $before = (Sample $dock).Count
  $map = Start-Process charmap -PassThru
  $enter = Track $dock 'Enter' { param($s, $ms) $ms -ge 700 }
  $widths = $enter | Where-Object { $_.Width } | ForEach-Object { $_.Width }
  Start-Sleep -Seconds 1
  $rest = (Sample $dock).Width
  "Resting width: $rest, buttons: $before -> $((Sample $dock).Count)"
  if (-not $rest -or ($widths | Measure-Object -Maximum).Maximum -gt $rest) { throw 'The icon did not end at its resting width.' }
  if (-not ($widths | Where-Object { $_ -lt $rest })) { throw 'The icon appeared at once (no intermediate width was sampled).' }
  Stop-Process -Id $map.Id -Force
  $leave = Track $dock 'Leave' { param($s, $ms) $null -eq $s.Width -or $ms -ge 800 }
  $mid = $leave | Where-Object { $_.Width -and $_.Width -lt $rest }
  Start-Sleep -Milliseconds 500
  "Buttons after closing: $((Sample $dock).Count) (expected $before)"
  if ((Sample $dock).Count -ne $before) { throw 'The icon was not removed.' }
  if (-not $mid) { throw 'The icon vanished at once (no intermediate width was sampled).' }
  'OK'
  Stop-Process -Id $p.Id -Force
} finally {
  if ($map -and -not $map.HasExited) { Stop-Process -Id $map.Id -Force }
  Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
  if ($had) { Copy-Item (Join-Path $backup 'settings.json') $settings -Force } else { Remove-Item $settings -ErrorAction SilentlyContinue }
  Remove-Item $backup -Recurse
}
