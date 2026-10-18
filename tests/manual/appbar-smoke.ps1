param(
    [string]$Executable = (Join-Path $PSScriptRoot '../../artifacts/FloatDock-win-x64/FloatDock.App.exe')
)

# Reserved mode: the dock reserves the bottom edge, is hidden from Alt+Tab, and a crash leaves no reserved band behind.
# Changes the real work area and taskbar of this session and always puts them back.
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FloatDockAppBarProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string className, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint action, uint param, out Rect rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
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

function Start-Dock([string]$Json) {
    Remove-Item -LiteralPath $attempts -ErrorAction SilentlyContinue
    $Json | Set-Content -LiteralPath $settings
    $dock = Start-Process -FilePath $exe -PassThru
    if ((Wait-Until { $dock.Refresh(); $dock.MainWindowHandle -ne 0 }) -lt 0) { throw 'FloatDock did not show a window.' }
    return $dock
}

function Close-Dock($Dock) {
    [FloatDockAppBarProbe]::PostMessageW($Dock.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    if (-not $Dock.WaitForExit(5000)) { throw 'FloatDock did not close.' }
}

if (Get-Process -Name 'FloatDock.App' -ErrorAction SilentlyContinue) { throw 'Close the existing FloatDock instance before this test.' }
$exe = (Resolve-Path $Executable).Path
$taskbar = [FloatDockAppBarProbe]::FindWindowW('Shell_TrayWnd', $null)
if (-not [FloatDockAppBarProbe]::IsWindowVisible($taskbar)) { throw 'The Windows taskbar must be visible before this test.' }
$originalBottom = [FloatDockAppBarProbe]::WorkAreaBottom()
$taskbarRect = New-Object FloatDockAppBarProbe+Rect
[FloatDockAppBarProbe]::GetWindowRect($taskbar, [ref]$taskbarRect) | Out-Null
$screenBottom = $taskbarRect.Bottom

$settingsDirectory = Join-Path $env:LOCALAPPDATA 'FloatDock'
$settings = Join-Path $settingsDirectory 'settings.json'
$attempts = Join-Path $settingsDirectory 'startup-attempts.json'
$appBarJournal = Join-Path $settingsDirectory 'appbar.json'
$settingsBackup = Join-Path $env:TEMP ('FloatDockSettings-' + [guid]::NewGuid().ToString('N'))
$hadSettings = Test-Path -LiteralPath $settings
if ($hadSettings) { Copy-Item -LiteralPath $settings -Destination $settingsBackup }
$process = $null
$results = [ordered]@{ OriginalWorkAreaBottom = $originalBottom }

try {
    # 1. Reserved with the taskbar visible: the band sits above the taskbar.
    $process = Start-Dock '{"Mode":1,"FirstRunCompleted":true,"IconSize":52}'
    if ((Wait-Until { [FloatDockAppBarProbe]::WorkAreaBottom() -lt $originalBottom - 40 }) -lt 0) { throw 'Reserved mode did not shrink the work area.' }
    $results.ReservedAboveTaskbar = [FloatDockAppBarProbe]::WorkAreaBottom()
    $ex = [FloatDockAppBarProbe]::GetWindowLongPtr($process.MainWindowHandle, -20).ToInt64()
    $results.ToolWindow = [bool]($ex -band 0x80)
    if (-not $results.ToolWindow) { throw 'The dock window is not a tool window, so it appears in Alt+Tab.' }
    $dock = New-Object FloatDockAppBarProbe+Rect
    [FloatDockAppBarProbe]::GetWindowRect($process.MainWindowHandle, [ref]$dock) | Out-Null
    $results.DockBottom = $dock.Bottom
    if ($dock.Bottom -gt $originalBottom) { throw 'The dock overlaps the visible taskbar.' }
    Close-Dock $process
    if ((Wait-Until { [FloatDockAppBarProbe]::WorkAreaBottom() -eq $originalBottom }) -lt 0) { throw 'Closing did not release the band.' }
    if (Test-Path -LiteralPath $appBarJournal) { throw 'Closing left the AppBar journal.' }
    $results.ReleasedOnClose = $true

    # 2. Reserved with taskbar replacement: the same band moves down to the screen bottom.
    $band = $originalBottom - $results.ReservedAboveTaskbar
    $process = Start-Dock '{"Mode":1,"ReplaceTaskbar":true,"FirstRunCompleted":true,"IconSize":52}'
    if ((Wait-Until { -not [FloatDockAppBarProbe]::IsWindowVisible($taskbar) -and [FloatDockAppBarProbe]::WorkAreaBottom() -eq $screenBottom - $band }) -lt 0) {
        throw "With the taskbar replaced, the work area ended at $([FloatDockAppBarProbe]::WorkAreaBottom()), expected $($screenBottom - $band)."
    }
    $results.ReservedReplacing = [FloatDockAppBarProbe]::WorkAreaBottom()

    # 3. A crashed dock: Guardian undoes the taskbar change and removes the band.
    Stop-Process -Id $process.Id -Force
    if ((Wait-Until { [FloatDockAppBarProbe]::IsWindowVisible($taskbar) -and [FloatDockAppBarProbe]::WorkAreaBottom() -eq $originalBottom }) -lt 0) {
        throw "After a crash the work area stayed at $([FloatDockAppBarProbe]::WorkAreaBottom())."
    }
    if ((Wait-Until { -not (Test-Path -LiteralPath $appBarJournal) } 3000) -lt 0) { throw 'The AppBar journal was left after a crash.' }
    $results.CrashRestored = $true

    $results.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
}
finally {
    foreach ($dock in @(Get-Process -Name 'FloatDock.App' -ErrorAction SilentlyContinue)) {
        [FloatDockAppBarProbe]::PostMessageW($dock.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        if (-not $dock.WaitForExit(3000)) { Stop-Process -Id $dock.Id -Force }
    }
    Start-Sleep -Milliseconds 500
    # Undo anything a failed step left: same recovery path users get.
    Start-Process -FilePath $exe -ArgumentList '--restore-taskbar' -Wait
    Get-Process -Name 'FloatDock.Guardian' -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($hadSettings) { Copy-Item -LiteralPath $settingsBackup -Destination $settings -Force; Remove-Item -LiteralPath $settingsBackup }
    else { Remove-Item -LiteralPath $settings -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $attempts -ErrorAction SilentlyContinue
    Write-Output "Final: TaskbarVisible=$([FloatDockAppBarProbe]::IsWindowVisible($taskbar)) WorkAreaBottom=$([FloatDockAppBarProbe]::WorkAreaBottom())"
}
