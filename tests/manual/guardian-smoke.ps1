param(
    [string]$Executable = (Join-Path $PSScriptRoot '../../artifacts/FloatDock-win-x64/FloatDock.App.exe')
)

# Changes the real taskbar of the current session and always puts it back in the finally block.
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FloatDockTaskbarProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct AppBarData { public int Size; public IntPtr Window; public uint Callback; public uint Edge; public Rect Bounds; public IntPtr Parameter; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string className, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint action, uint param, out Rect rect, uint flags);
    [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    public static int GetState() { var d = new AppBarData(); d.Size = Marshal.SizeOf(d); return (int)SHAppBarMessage(4, ref d); }
    public static void SetState(int state) { var d = new AppBarData(); d.Size = Marshal.SizeOf(d); d.Window = FindWindowW("Shell_TrayWnd", null); d.Parameter = (IntPtr)state; SHAppBarMessage(10, ref d); }
    public static int WorkAreaBottom() { Rect r; SystemParametersInfoW(0x30, 0, out r, 0); return r.Bottom; }
}
'@

function Wait-Until([scriptblock]$Condition, [int]$TimeoutMs = 15000) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return $watch.ElapsedMilliseconds }
        Start-Sleep -Milliseconds 50
    }
    return -1
}

function Get-Guardian([int]$ParentId) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$ParentId AND Name='FloatDock.Guardian.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Start-Dock {
    $dock = Start-Process -FilePath $exe -PassThru
    $guardian = $null
    $elapsed = Wait-Until { $script:g = Get-Guardian $dock.Id; $script:g -and -not [FloatDockTaskbarProbe]::IsWindowVisible($taskbar) }
    if ($elapsed -lt 0) { throw 'FloatDock did not hide the taskbar with a running Guardian.' }
    return @($dock, $script:g, $elapsed)
}

function Assert-Restored([string]$Context) {
    if ((Wait-Until { [FloatDockTaskbarProbe]::IsWindowVisible($taskbar) -and [FloatDockTaskbarProbe]::GetState() -eq $originalState }) -lt 0) {
        throw "$Context`: taskbar visibility or AppBar state was not restored."
    }
    # The journal is deleted right after the taskbar is shown.
    if ((Wait-Until { -not (Test-Path -LiteralPath $journal) } 3000) -lt 0) { throw "$Context`: the shell-state journal was left behind." }
}

if (Get-Process -Name 'FloatDock.App' -ErrorAction SilentlyContinue) { throw 'Close the existing FloatDock instance before this test.' }
$exe = (Resolve-Path $Executable).Path
$taskbar = [FloatDockTaskbarProbe]::FindWindowW('Shell_TrayWnd', $null)
if ($taskbar -eq [IntPtr]::Zero -or -not [FloatDockTaskbarProbe]::IsWindowVisible($taskbar)) { throw 'The Windows taskbar must be visible before this test.' }
$originalState = [FloatDockTaskbarProbe]::GetState()
if ($originalState -band 1) { throw 'Turn off taskbar auto-hide before this test so restoration can be checked.' }
$screenBottom = [FloatDockTaskbarProbe]::GetSystemMetrics(1)

$settingsDirectory = Join-Path $env:LOCALAPPDATA 'FloatDock'
New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
$settings = Join-Path $settingsDirectory 'settings.json'
$attempts = Join-Path $settingsDirectory 'startup-attempts.json'
$journal = Join-Path $settingsDirectory 'shell-state.json'
$settingsBackup = Join-Path $env:TEMP ('FloatDockSettings-' + [guid]::NewGuid().ToString('N'))
$attemptsBackup = Join-Path $env:TEMP ('FloatDockAttempts-' + [guid]::NewGuid().ToString('N'))
$hadSettings = Test-Path -LiteralPath $settings
$hadAttempts = Test-Path -LiteralPath $attempts
if ($hadSettings) { Copy-Item -LiteralPath $settings -Destination $settingsBackup }
if ($hadAttempts) { Copy-Item -LiteralPath $attempts -Destination $attemptsBackup }
$process = $null
$results = [ordered]@{}

try {
    Remove-Item -LiteralPath $attempts -ErrorAction SilentlyContinue
    '{"ReplaceTaskbar":true,"FirstRunCompleted":true}' | Set-Content -LiteralPath $settings

    # 1. Hide frees the work area and puts the dock at the bottom of the display.
    $process, $guardian, $elapsed = Start-Dock
    $results.HideMs = $elapsed
    if (-not ([FloatDockTaskbarProbe]::GetState() -band 1)) { throw 'The taskbar was hidden without ABS_AUTOHIDE.' }
    if ((Wait-Until { [FloatDockTaskbarProbe]::WorkAreaBottom() -ge $screenBottom - 2 }) -lt 0) { throw 'The work area still reserves the taskbar.' }
    if (-not (Test-Path -LiteralPath $journal)) { throw 'No shell-state journal was written.' }
    $dockBounds = New-Object FloatDockTaskbarProbe+Rect
    $placed = Wait-Until {
        $process.Refresh()
        [FloatDockTaskbarProbe]::GetWindowRect($process.MainWindowHandle, [ref]$script:dockBounds) -and $screenBottom - $script:dockBounds.Bottom -le 32
    } 3000
    if ($placed -lt 0) { throw "The dock stayed above the taskbar reservation: handle=$($process.MainWindowHandle) bottom=$($dockBounds.Bottom)." }
    $results.WorkAreaBottom = [FloatDockTaskbarProbe]::WorkAreaBottom()
    $results.DockBottom = $dockBounds.Bottom

    # 2. A taskbar shown by someone else is hidden again by the Explorer show-event hook.
    [FloatDockTaskbarProbe]::ShowWindow($taskbar, 5) | Out-Null
    $results.RehideMs = Wait-Until { -not [FloatDockTaskbarProbe]::IsWindowVisible($taskbar) } 3000
    if ($results.RehideMs -lt 0) { throw 'FloatDock did not re-hide a shown taskbar.' }

    # 3. Undoing ABS_AUTOHIDE behind FloatDock's back is re-applied.
    [FloatDockTaskbarProbe]::SetState($originalState)
    $results.AutoHideReappliedMs = Wait-Until { [FloatDockTaskbarProbe]::GetState() -band 1 } 3000
    if ($results.AutoHideReappliedMs -lt 0) { throw 'FloatDock did not re-apply auto-hide.' }

    # 4. The first Guardian exit is survived by restarting Guardian; the taskbar stays hidden.
    Stop-Process -Id $guardian.ProcessId -Force
    if ((Wait-Until { $script:g2 = Get-Guardian $process.Id; $script:g2 -and $script:g2.ProcessId -ne $guardian.ProcessId }) -lt 0) { throw 'Guardian was not restarted.' }
    if ([FloatDockTaskbarProbe]::IsWindowVisible($taskbar)) { throw 'The taskbar reappeared while Guardian restarted.' }
    $results.GuardianRestarted = $true

    # 5. A second Guardian exit restores the taskbar, keeps the dock alive and keeps the setting.
    Stop-Process -Id $script:g2.ProcessId -Force
    Assert-Restored 'Second Guardian exit'
    $process.Refresh()
    if ($process.HasExited) { throw 'FloatDock exited after Guardian failed.' }
    if (-not (Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).ReplaceTaskbar) { throw 'The setting was silently turned off.' }
    $results.SecondGuardianExitRestored = $true
    [FloatDockTaskbarProbe]::PostMessageW($process.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    if ((Wait-Until { $process.Refresh(); $process.HasExited }) -lt 0) { throw 'FloatDock did not close.' }

    # 6. A crashed dock is undone by Guardian, including the AppBar state.
    Remove-Item -LiteralPath $attempts -ErrorAction SilentlyContinue
    $process, $guardian, $elapsed = Start-Dock
    Stop-Process -Id $process.Id -Force
    Assert-Restored 'Dock crash'
    $results.DockCrashRestored = $true

    # 7. A change left behind by a session without any survivor is undone by --restore-taskbar.
    [FloatDockTaskbarProbe]::SetState($originalState -bor 1)
    [FloatDockTaskbarProbe]::ShowWindow($taskbar, 0) | Out-Null
    ('{"OriginalAppBarState":' + $originalState + ',"ProcessId":1,"HiddenAt":"2026-01-01T00:00:00+00:00"}') | Set-Content -LiteralPath $journal
    $restore = Start-Process -FilePath $exe -ArgumentList '--restore-taskbar' -PassThru
    if (-not $restore.WaitForExit(15000)) { throw '--restore-taskbar did not exit.' }
    Assert-Restored '--restore-taskbar'
    $results.RestoreCommand = $true

    $results.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
}
finally {
    foreach ($dock in @(Get-Process -Name 'FloatDock.App' -ErrorAction SilentlyContinue)) {
        [FloatDockTaskbarProbe]::PostMessageW($dock.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        if (-not $dock.WaitForExit(3000)) { Stop-Process -Id $dock.Id -Force }
    }
    Get-Process -Name 'FloatDock.Guardian' -ErrorAction SilentlyContinue | Stop-Process -Force
    [FloatDockTaskbarProbe]::SetState($originalState)
    [FloatDockTaskbarProbe]::ShowWindow($taskbar, 5) | Out-Null
    Remove-Item -LiteralPath $journal -ErrorAction SilentlyContinue
    if ($hadSettings) { Copy-Item -LiteralPath $settingsBackup -Destination $settings -Force; Remove-Item -LiteralPath $settingsBackup }
    else { Remove-Item -LiteralPath $settings -ErrorAction SilentlyContinue }
    if ($hadAttempts) { Copy-Item -LiteralPath $attemptsBackup -Destination $attempts -Force; Remove-Item -LiteralPath $attemptsBackup }
    else { Remove-Item -LiteralPath $attempts -ErrorAction SilentlyContinue }
    Write-Output "Final: TaskbarVisible=$([FloatDockTaskbarProbe]::IsWindowVisible($taskbar)) AppBarState=$([FloatDockTaskbarProbe]::GetState()) WorkAreaBottom=$([FloatDockTaskbarProbe]::WorkAreaBottom())"
}
