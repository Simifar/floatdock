# Store edition check: registers the unpacked package (scripts/build-msix.ps1 output) for the current user, runs the app from it
# and checks what is different in a package: it runs from the package folder, hides the taskbar replacement and the updater in
# Settings, and "Start with Windows" survives a restart through the package's StartupTask. Removes the package again.
# Needs Developer Mode (Settings > System > For developers), pwsh, a visible desktop, and no FloatDock running.
# Backs up settings.json and startup-attempts.json into a folder and verifies the restore.
param([string]$Layout = (Join-Path $PSScriptRoot '../../artifacts/msix/layout'), [string]$PackageName = 'FloatDock.Dev')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
if (Get-AppxPackage -Name $PackageName) { throw "Package $PackageName is already registered: remove it first." }
$A = [Windows.Automation.AutomationElement]
$layoutPath = (Resolve-Path $Layout).Path
$data = Join-Path $env:LOCALAPPDATA 'FloatDock'; $settings = Join-Path $data 'settings.json'; $attempts = Join-Path $data 'startup-attempts.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
foreach ($f in $settings, $attempts) { if (Test-Path $f) { Copy-Item $f $backup } }
$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail = '') { "{0}: {1} {2}" -f $name, $(if ($ok) { 'ok' } else { 'FAILED' }), $detail; if (-not $ok) { $script:failures += $name } }
function Wait-Until([scriptblock]$c, [int]$ms = 20000) { $w = [Diagnostics.Stopwatch]::StartNew(); while ($w.ElapsedMilliseconds -lt $ms) { if (& $c) { return $true }; Start-Sleep -Milliseconds 200 }; $false }
function Dock { Get-Process FloatDock.App -ErrorAction SilentlyContinue | Select-Object -First 1 }
function Start-App($pfn) { Start-Process "shell:AppsFolder\$pfn!FloatDock"; [void](Wait-Until { $null -ne (Dock) }); Start-Sleep -Seconds 6 }
function Quit-App {
    $quit = $null
    [void](Wait-Until { try { $script:quit = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Quit'); $true } catch { $false } } 10000)
    if ($quit) { [void]$quit.Set(); $quit.Dispose() }
    [void](Wait-Until { -not (Dock) } 15000)
}
function Settings-Window($p) { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
    (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'FloatDock Settings'))))) }
function Open-Settings($p) {
    $activate = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Activate'); [void]$activate.Set(); $activate.Dispose()
    $script:win = $null
    [void](Wait-Until { $script:win = Settings-Window $p; $null -ne $script:win } 10000)
    $script:win
}
function Named($win, [string]$name) { $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name))) }
function Toggle-State($e) { $e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState }
try {
    Remove-Item $attempts -ErrorAction SilentlyContinue
    '{"FirstRunCompleted":true,"Mode":0,"Language":1,"ReplaceTaskbar":true}' | Set-Content $settings
    Add-AppxPackage -Register (Join-Path $layoutPath 'AppxManifest.xml')
    $package = Get-AppxPackage -Name $PackageName
    Check 'The package is registered' ($null -ne $package) $package.PackageFullName
    $pfn = $package.PackageFamilyName

    Start-App $pfn
    $dock = Dock
    Check 'The app starts from the package' ($null -ne $dock -and $dock.Path -like "$layoutPath\*") $dock.Path
    $win = Open-Settings $dock
    Check 'Settings opens' ($null -ne $win)
    if ($win) {
        Check 'No taskbar replacement switch in the Store edition' ($null -eq (Named $win 'Replace the Windows taskbar'))
        Check 'No update button in the Store edition' ($null -eq (Named $win 'Check for updates'))
        Check 'Settings says the Store updates the app' ($null -ne (Named $win 'FloatDock was installed from the Microsoft Store, which keeps it up to date.'))
        Check 'The version is shown' ($null -ne (Named $win 'Version 0.3.0'))
        $startup = Named $win 'Start with Windows'
        Check 'Start with Windows is offered and off' ($null -ne $startup -and (Toggle-State $startup) -eq [Windows.Automation.ToggleState]::Off)
        if ($startup) {
            $startup.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
            Start-Sleep -Seconds 2
            Check 'Turning it on sticks in the window' ((Toggle-State (Named (Settings-Window $dock) 'Start with Windows')) -eq [Windows.Automation.ToggleState]::On)
        }
    }
    Check 'The saved ReplaceTaskbar request is ignored (taskbar visible)' ((Add-Type -Name T -Namespace W -PassThru -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern System.IntPtr FindWindowW(string c, string t); [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);' | ForEach-Object { $h = $_::FindWindowW('Shell_TrayWnd', $null); $_::IsWindowVisible($h) }))
    Quit-App

    # The StartupTask state is read back when the app starts again.
    Start-App $pfn
    $win = Open-Settings (Dock)
    $startup = if ($win) { Named $win 'Start with Windows' }
    Check 'Start with Windows is still on after a restart' ($null -ne $startup -and (Toggle-State $startup) -eq [Windows.Automation.ToggleState]::On)
    if ($startup) { $startup.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Seconds 2 }
    Quit-App
    Start-App $pfn
    $win = Open-Settings (Dock)
    $startup = if ($win) { Named $win 'Start with Windows' }
    Check 'and off again after turning it off' ($null -ne $startup -and (Toggle-State $startup) -eq [Windows.Automation.ToggleState]::Off)
    Quit-App
}
finally {
    Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $leftover = Get-AppxPackage -Name $PackageName
    if ($leftover) { $leftover | Remove-AppxPackage }
    Check 'The package is removed again' ($null -eq (Get-AppxPackage -Name $PackageName))
    foreach ($f in $settings, $attempts) {
        $saved = Join-Path $backup (Split-Path $f -Leaf)
        if (Test-Path $saved) { Copy-Item $saved $f -Force } else { Remove-Item $f -ErrorAction SilentlyContinue }
    }
    $restored = $true
    foreach ($saved in Get-ChildItem $backup) { if ((Get-FileHash $saved.FullName).Hash -ne (Get-FileHash (Join-Path $data $saved.Name)).Hash) { $restored = $false } }
    if ($restored) { Remove-Item $backup -Recurse } else { "RESTORE FAILED, the copies are in $backup" }
    "Restored: $restored"
}
if ($failures.Count -gt 0) { throw ('Failed: ' + ($failures -join '; ')) }
