using FloatDock.Core;
using System.Text;

namespace FloatDock.Windows;

public sealed class FullscreenDetector
{
    private const int QunsBusy = 2, QunsD3dFullScreen = 3, QunsPresentationMode = 4;

    /// <summary>
    /// The Shell's own answer (the one that mutes notifications): a full-screen app, an exclusive Direct3D game or presentation mode.
    /// It is not per display, so it is only one of the dock's witnesses.
    /// </summary>
    public static bool ShellSaysFullscreen() =>
        Native.SHQueryUserNotificationState(out var state) == 0 && state is QunsBusy or QunsD3dFullScreen or QunsPresentationMode;

    /// <summary>
    /// The middle of the foreground window in screen pixels, which tells the display it is on; null when there is none.
    /// The Shell's verdict is not per display, so with several docks it counts only for the display of the foreground window.
    /// </summary>
    public static (int X, int Y)? ForegroundCenter()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == 0 || !Native.GetWindowRect(hwnd, out var rect)) return null;
        return (rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
    }

    public bool IsForegroundFullscreen(ScreenRect monitor)
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == 0 || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return false;
        Native.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == Environment.ProcessId || !Native.GetWindowRect(hwnd, out var rect)) return false;
        var className = new StringBuilder(128);
        Native.GetClassNameW(hwnd, className, className.Capacity);
        return FullscreenGeometry.IsUserFullscreen(
            new ScreenRect(rect.Left, rect.Top, rect.Width, rect.Height), monitor, className.ToString());
    }
}
