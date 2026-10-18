namespace FloatDock.Windows;

public sealed class WindowCommands
{
    /// <summary>
    /// Minimizes the window if it was the foreground app before the dock was clicked, otherwise restores and activates it.
    /// Comparing with the live foreground window would always see the dock itself.
    /// </summary>
    public bool ActivateOrMinimize(nint hwnd, nint foregroundBeforeDock)
    {
        if (!Native.IsWindow(hwnd)) return false;
        if (foregroundBeforeDock == hwnd && !Native.IsIconic(hwnd)) return Native.ShowWindowAsync(hwnd, Native.SwMinimize);
        if (Native.IsIconic(hwnd)) Native.ShowWindowAsync(hwnd, Native.SwRestore);
        return Native.SetForegroundWindow(hwnd);
    }

    public bool Close(nint hwnd) => Native.IsWindow(hwnd) && Native.PostMessageW(hwnd, Native.WmClose, 0, 0);
}
