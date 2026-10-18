param(
    [string]$Executable = (Join-Path $PSScriptRoot '../../artifacts/FloatDock-win-x64/FloatDock.App.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FloatDockAutoHideProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
}
'@

$settingsDirectory = Join-Path $env:LOCALAPPDATA 'FloatDock'
New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
$settings = Join-Path $settingsDirectory 'settings.json'
$attempts = Join-Path $settingsDirectory 'startup-attempts.json'
$settingsBackup = Join-Path $env:TEMP ('FloatDockSettings-' + [guid]::NewGuid().ToString('N'))
$attemptsBackup = Join-Path $env:TEMP ('FloatDockAttempts-' + [guid]::NewGuid().ToString('N'))
$hadSettings = Test-Path -LiteralPath $settings
$hadAttempts = Test-Path -LiteralPath $attempts
if ($hadSettings) { Copy-Item -LiteralPath $settings -Destination $settingsBackup }
if ($hadAttempts) { Copy-Item -LiteralPath $attempts -Destination $attemptsBackup }
$cursor = New-Object FloatDockAutoHideProbe+Point
[FloatDockAutoHideProbe]::GetCursorPos([ref]$cursor) | Out-Null
$process = $null

try {
    Remove-Item -LiteralPath $attempts -ErrorAction SilentlyContinue
    '{"Mode":2,"FirstRunCompleted":true}' | Set-Content -LiteralPath $settings
    [FloatDockAutoHideProbe]::SetCursorPos(20, 20) | Out-Null
    $process = Start-Process -FilePath (Resolve-Path $Executable) -PassThru
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) { break }
    }
    if ($process.MainWindowHandle -eq 0) { throw 'FloatDock did not show a window.' }

    $initial = New-Object FloatDockAutoHideProbe+Rect
    [FloatDockAutoHideProbe]::GetWindowRect($process.MainWindowHandle, [ref]$initial) | Out-Null
    [FloatDockAutoHideProbe]::SetCursorPos([int](($initial.Left + $initial.Right) / 2), [int](($initial.Top + $initial.Bottom) / 2)) | Out-Null
    [FloatDockAutoHideProbe]::mouse_event(1, 1, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Seconds 1
    [FloatDockAutoHideProbe]::SetCursorPos(20, 20) | Out-Null
    Start-Sleep -Seconds 2
    $hidden = New-Object FloatDockAutoHideProbe+Rect
    [FloatDockAutoHideProbe]::GetWindowRect($process.MainWindowHandle, [ref]$hidden) | Out-Null
    $targetX = [int](($hidden.Left + $hidden.Right) / 2)
    $targetY = $hidden.Top + 2
    [FloatDockAutoHideProbe]::SetCursorPos($targetX, $targetY) | Out-Null
    [FloatDockAutoHideProbe]::mouse_event(1, 1, 0, 0, [UIntPtr]::Zero)
    $hitPoint = New-Object FloatDockAutoHideProbe+Point
    $hitPoint.X = $targetX
    $hitPoint.Y = $targetY
    $hit = [FloatDockAutoHideProbe]::WindowFromPoint($hitPoint)
    $hitPid = [uint]0
    [FloatDockAutoHideProbe]::GetWindowThreadProcessId($hit, [ref]$hitPid) | Out-Null
    Start-Sleep -Seconds 1
    $revealed = New-Object FloatDockAutoHideProbe+Rect
    [FloatDockAutoHideProbe]::GetWindowRect($process.MainWindowHandle, [ref]$revealed) | Out-Null
    Write-Output "InitialY=$($initial.Top) HiddenY=$($hidden.Top) RevealedY=$($revealed.Top) HitPid=$hitPid DockPid=$($process.Id) Target=$targetX,$targetY"
    if ($hidden.Top -le $initial.Top -or $revealed.Top -ge $hidden.Top) { throw 'Auto-hide movement failed.' }
}
finally {
    [FloatDockAutoHideProbe]::SetCursorPos($cursor.X, $cursor.Y) | Out-Null
    if ($process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            [FloatDockAutoHideProbe]::PostMessageW($process.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            Start-Sleep -Seconds 1
            $process.Refresh()
            if (-not $process.HasExited) { Stop-Process -Id $process.Id }
        }
    }
    if ($hadSettings) { Copy-Item -LiteralPath $settingsBackup -Destination $settings -Force; Remove-Item -LiteralPath $settingsBackup }
    else { Remove-Item -LiteralPath $settings -ErrorAction SilentlyContinue }
    if ($hadAttempts) { Copy-Item -LiteralPath $attemptsBackup -Destination $attempts -Force; Remove-Item -LiteralPath $attemptsBackup }
    else { Remove-Item -LiteralPath $attempts -ErrorAction SilentlyContinue }
}
