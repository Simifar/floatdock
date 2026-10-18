namespace FloatDock.Core;

/// <summary>Which Windows the app runs on: Windows 10 (1809 and later) and Windows 11 differ in a few shell details.</summary>
public static class WindowsBuild
{
    public const int FirstWindows11 = 22000;

    public static bool IsWindows11(int build) => build >= FirstWindows11;

    public static bool IsWindows11() => IsWindows11(Environment.OSVersion.Version.Build);

    /// <summary>
    /// The Win key letter that opens the notifications panel: Windows 11 has Win+N (and Win+A for quick settings), Windows 10 has
    /// only Win+A (the action center holds both).
    /// </summary>
    public static char NotificationsKey(int build) => IsWindows11(build) ? 'N' : 'A';
}
