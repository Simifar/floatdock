param(
    [string]$Executable = (Join-Path $PSScriptRoot '../../artifacts/FloatDock-win-x64/FloatDock.App.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class FloatDockExitProbe {
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    public static IntPtr DockWindow(uint processId, out int titledWindows) {
        IntPtr dock = IntPtr.Zero;
        int count = 0;
        EnumWindows((hwnd, _) => {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != processId) return true;
            var title = new StringBuilder(256);
            GetWindowTextW(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            count++;
            if (title.ToString() == "FloatDock") dock = hwnd;
            return true;
        }, IntPtr.Zero);
        titledWindows = count;
        return dock;
    }
}
'@

$process = Start-Process -FilePath (Resolve-Path $Executable) -ArgumentList '--safe-mode', '--settings' -PassThru
try {
    $count = 0
    $dock = [IntPtr]::Zero
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 300
        $dock = [FloatDockExitProbe]::DockWindow([uint32]$process.Id, [ref]$count)
        if ($dock -ne [IntPtr]::Zero -and $count -ge 2) { break }
    }
    if ($dock -eq [IntPtr]::Zero -or $count -lt 2) { throw 'Dock and Settings did not both open.' }
    [FloatDockExitProbe]::PostMessageW($dock, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 300
        $process.Refresh()
        if ($process.HasExited) { break }
    }
    if (-not $process.HasExited) { throw 'FloatDock stayed alive after the dock closed.' }
    Write-Output "DockAndSettingsOpened=True ProcessExited=True"
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
