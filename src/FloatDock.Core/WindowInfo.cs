namespace FloatDock.Core;

/// <summary>
/// A top-level window as the dock sees it. Geometry is reduced to <see cref="HasArea"/> so moving or resizing
/// a window does not change the snapshot.
/// </summary>
public sealed record WindowInfo(
    nint Handle,
    uint ProcessId,
    string Title,
    string ClassName,
    string? ExecutablePath,
    bool IsVisible,
    bool IsChild,
    bool IsToolWindow,
    bool HasOwner,
    bool IsCloaked,
    bool HasArea,
    bool IsForeground,
    string? AppId = null,
    bool IsAppWindow = false,
    bool IsNoActivate = false,
    bool IsFlashing = false);

public static class WindowEligibility
{
    /// <summary>
    /// The Windows taskbar rule: an unowned window that is not a tool window gets a button,
    /// and WS_EX_APPWINDOW forces one even for owned or tool windows.
    /// </summary>
    public static bool IsEligible(WindowInfo window) =>
        window.Handle != 0 && window.IsVisible && !window.IsChild && !window.IsCloaked && window.HasArea &&
        (window.IsAppWindow || (!window.IsToolWindow && !window.HasOwner && !window.IsNoActivate)) &&
        !string.IsNullOrWhiteSpace(window.Title) &&
        window.ClassName is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") &&
        !window.ClassName.StartsWith("FloatDock", StringComparison.Ordinal);
}
