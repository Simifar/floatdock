# Update check: installs an older Velopack build, updates it from a folder feed through Settings, then uninstalls.
# Prepare the feed first (two versions into one folder, the second without -Build so it reuses the release files):
#   pwsh scripts/build-velopack.ps1 -Version 0.2.0 -OutputDir <feed>
#   pwsh scripts/build-velopack.ps1 -Version 0.2.1 -SkipBuild -OutputDir <feed>
# then:  pwsh tests/manual/update-check.ps1 -Feed <feed> -From 0.2.0 -To 0.2.1
# The installer must be the older one: pass -Setup <path to FloatDockApp-win-Setup.exe built for -From> (Velopack names it the same
# for every version, so keep a copy of the first one). Installs to %LocalAppData%\FloatDockApp and removes it again.
# Backs up settings.json and startup-attempts.json into a folder and verifies the restore. Close FloatDock first.
param([Parameter(Mandatory)][string]$Setup, [Parameter(Mandatory)][string]$Feed, [string]$From = '0.2.0', [string]$To = '0.2.1')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class UP {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h); }
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$A = [Windows.Automation.AutomationElement]
$data = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $data 'settings.json'; $attempts = Join-Path $data 'startup-attempts.json'
$root = Join-Path $env:LOCALAPPDATA 'FloatDockApp'
$shortcutDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'FloatDock'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
foreach ($f in $settings, $attempts) { if (Test-Path $f) { Copy-Item $f $backup } }
$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail = '') { "{0}: {1} {2}" -f $name, $(if ($ok) { 'ok' } else { 'FAILED' }), $detail; if (-not $ok) { $script:failures += $name } }
function Wait-Until([scriptblock]$c, [int]$ms = 20000) { $w = [Diagnostics.Stopwatch]::StartNew(); while ($w.ElapsedMilliseconds -lt $ms) { if (& $c) { return $true }; Start-Sleep -Milliseconds 200 }; $false }
function Installed-Version { $f = Join-Path $root 'current\sq.version'; if (Test-Path $f) { ([xml](Get-Content $f -Raw)).package.metadata.version } else { '' } }
function Dock { Get-Process FloatDock.App -ErrorAction SilentlyContinue | Select-Object -First 1 }
function Settings-Window($p) { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
  (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'Настройки FloatDock')))))}
function Button($win, [string]$name) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.AndCondition(
  (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name))))) }
function Invoke($e) { $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
try {
  if (Test-Path $root) { throw "$root exists: uninstall the Velopack copy first." }
  Remove-Item $attempts -ErrorAction SilentlyContinue
  (@{ FirstRunCompleted = $true; Mode = 0; Language = 2; UpdateFeed = (Resolve-Path $Feed).Path } | ConvertTo-Json) | Set-Content $settings

  & $Setup --silent | Out-Null
  Check "Installed $From" (Wait-Until { (Installed-Version) -eq $From }) "version=$(Installed-Version)"
  Check 'Start menu has the restore-taskbar shortcut' (Test-Path $shortcutDir) $shortcutDir
  $exe = Join-Path $root 'current\FloatDock.App.exe'
  Start-Process $exe | Out-Null
  $dock = $null
  Check 'The installed app starts' (Wait-Until { $script:dock = Dock; $null -ne $script:dock })
  Start-Sleep -Seconds 6

  $activate = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Activate'); [void]$activate.Set(); $activate.Dispose()
  $win = $null
  Check 'Settings opens' (Wait-Until { $script:win = Settings-Window $dock; $null -ne $script:win } 10000)
  # The update button is on the Updates page of the page list.
  $page = $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'Обновления')))
  if ($page) { $page.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 800; $win = Settings-Window $dock }
  $check = Button $win 'Проверить обновления'
  Check 'Settings has the update button' ($null -ne $check)
  Invoke $check
  $install = $null
  Check "Settings offers $To" (Wait-Until { $w = Settings-Window $dock; $script:install = Button $w "Установить $To и перезапустить"; $null -ne $script:install } 20000)

  $oldId = $dock.Id
  Invoke $install
  Check 'The old process exits' (Wait-Until { -not (Get-Process -Id $oldId -ErrorAction SilentlyContinue) } 20000)
  Check "Version $To is installed" (Wait-Until { (Installed-Version) -eq $To } 40000) "version=$(Installed-Version)"
  Check 'The app comes back by itself' (Wait-Until { $script:dock = Dock; $null -ne $script:dock -and $script:dock.Id -ne $oldId } 30000)
  Check 'The restarted app runs from the new version' ((Dock).Path -like "$root\current\*")

  # Quit the way the dock's menu does, then uninstall: the folder goes, the user's data stays.
  # The restarted app creates its signal a moment after the process appears.
  $quit = $null
  Wait-Until { try { $script:quit = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Quit'); $true } catch { $false } } 15000 | Out-Null
  [void]$quit.Set(); $quit.Dispose()
  Check 'The app exits on request' (Wait-Until { -not (Dock) } 10000)
  & (Join-Path $root 'Update.exe') --uninstall --silent | Out-Null
  Check 'Uninstall removes the install folder' (Wait-Until { -not (Test-Path (Join-Path $root 'current')) } 30000)
  Check 'Uninstall removes the Start menu entry' (Wait-Until { -not (Test-Path $shortcutDir) } 10000)
  Check 'The user data folder stays' (Test-Path $settings)
  $taskbar = [UP]::FindWindowW('Shell_TrayWnd', $null)
  Check 'The Windows taskbar is visible' ([UP]::IsWindowVisible($taskbar))

  # Part B: "download at startup" stages the update, and the next exit applies it without a restart.
  Remove-Item $attempts -ErrorAction SilentlyContinue
  (@{ FirstRunCompleted = $true; Mode = 0; Language = 2; UpdateFeed = (Resolve-Path $Feed).Path; CheckForUpdatesAtStartup = $true } | ConvertTo-Json) | Set-Content $settings
  & $Setup --silent | Out-Null
  Check "B: installed $From again" (Wait-Until { (Installed-Version) -eq $From })
  Start-Process (Join-Path $root 'current\FloatDock.App.exe') | Out-Null
  Check 'B: the update is downloaded at startup' (Wait-Until { Test-Path (Join-Path $root "packages\FloatDockApp-$To-full.nupkg") } 40000)
  Start-Sleep -Seconds 3
  Check 'B: still running the old version meanwhile' ((Installed-Version) -eq $From)
  $quit = $null
  Wait-Until { try { $script:quit = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Quit'); $true } catch { $false } } 15000 | Out-Null
  [void]$quit.Set(); $quit.Dispose()
  Check "B: $To is applied after the exit" (Wait-Until { (Installed-Version) -eq $To } 40000) "version=$(Installed-Version)"
  Start-Sleep -Seconds 3
  Check 'B: and the app is not started again' ($null -eq (Dock))
  & (Join-Path $root 'Update.exe') --uninstall --silent | Out-Null
  Check 'B: uninstall removes the install folder' (Wait-Until { -not (Test-Path (Join-Path $root 'current')) } 30000)
}
finally {
  Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
  if (Test-Path (Join-Path $root 'Update.exe')) { & (Join-Path $root 'Update.exe') --uninstall --silent | Out-Null; Start-Sleep -Seconds 3 }
  foreach ($f in $settings, $attempts) {
    $saved = Join-Path $backup (Split-Path $f -Leaf)
    if (Test-Path $saved) { Copy-Item $saved $f -Force } else { Remove-Item $f -ErrorAction SilentlyContinue }
  }
  $restored = $true
  foreach ($saved in Get-ChildItem $backup) { if ((Get-FileHash $saved.FullName).Hash -ne (Get-FileHash (Join-Path $data $saved.Name)).Hash) { $restored = $false } }
  if ($restored) { Remove-Item $backup -Recurse } else { "RESTORE FAILED, the copies are in $backup" }
  "Restored: $restored; install folder gone: $(-not (Test-Path $root))"
}
if ($failures.Count -gt 0) { throw ('Failed: ' + ($failures -join '; ')) }
