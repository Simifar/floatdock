using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>Window frame attributes that Windows 11 draws outside XAML content.</summary>
public static class WindowChrome
{
    private const uint DwmwaBorderColor = 34;
    private const uint DwmwaColorNone = 0xFFFFFFFE;
    private const uint DwmwaWindowCornerPreference = 33;
    private const uint DwmwcpDoNotRound = 1;
    private const long WsDialogFrame = 0x00400000;
    private const long WsThickFrame = 0x00040000;
    private const long WsExWindowEdge = 0x00000100;
    private const long WsExDialogModalFrame = 0x00000001;
    private const long WsExAppWindow = 0x00040000;
    private const uint SwpFrameChanged = 0x0020, SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    /// <summary>
    /// Removes the 1 px frame around the window. A borderless WinUI presenter keeps WS_DLGFRAME,
    /// and Windows draws a light frame for it; DWMWA_BORDER_COLOR alone does not remove that.
    /// Returns false when either step failed.
    /// </summary>
    public static bool RemoveBorder(nint hwnd)
    {
        var style = Native.GetWindowLongPtr(hwnd, Native.GwlStyle).ToInt64();
        var extended = Native.GetWindowLongPtr(hwnd, Native.GwlExStyle).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GwlStyle, (nint)(style & ~(WsDialogFrame | WsThickFrame)));
        Native.SetWindowLongPtr(hwnd, Native.GwlExStyle, (nint)(extended & ~(WsExWindowEdge | WsExDialogModalFrame)));
        var framed = Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
        var color = DwmwaColorNone;
        // DWMWA_BORDER_COLOR exists from Windows 11; on Windows 10 removing the frame style is all there is to do.
        var colored = !WindowsBuild.IsWindows11() || Native.DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(uint)) == 0;
        return framed && colored;
    }

    /// <summary>The dock is not an app window: a tool window gets no Windows taskbar button and no Alt+Tab entry.</summary>
    public static void HideFromSwitchers(nint hwnd)
    {
        var extended = Native.GetWindowLongPtr(hwnd, Native.GwlExStyle).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GwlExStyle, (nint)((extended | Native.WsExToolWindow) & ~WsExAppWindow));
    }

    /// <summary>
    /// Lets DWM blend the client area with per-pixel alpha, so transparent XAML shows the desktop behind it.
    /// Square window corners also stop DWM from drawing its shadow around the invisible rectangle.
    /// </summary>
    public static bool EnableClientTransparency(nint hwnd)
    {
        var square = DwmwcpDoNotRound;
        Native.DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref square, sizeof(uint));
        var margins = new Native.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        return Native.DwmExtendFrameIntoClientArea(hwnd, ref margins) == 0;
    }

    /// <summary>
    /// Gives the window the keyboard focus even when another app is in front: Windows only lets the foreground
    /// app (or a thread sharing its input queue) hand the focus over, as a hotkey press does not always count.
    /// </summary>
    public static void Focus(nint hwnd)
    {
        if (Native.GetForegroundWindow() == hwnd) return;
        var foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var thisThread = Native.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread && Native.AttachThreadInput(thisThread, foregroundThread, true);
        Native.BringWindowToTop(hwnd);
        Native.SetForegroundWindow(hwnd);
        if (attached) Native.AttachThreadInput(thisThread, foregroundThread, false);
    }

    /// <summary>Puts a window at the top of the topmost windows without activating it, so it stays above another overlay.</summary>
    public static void BringToFront(nint hwnd) =>
        Native.SetWindowPos(hwnd, -1, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    /// <summary>
    /// Limits where the window receives input (and draws) to a rectangle in window pixels; null means the whole window.
    /// Clicks outside the region reach the windows underneath.
    /// </summary>
    public static void SetHitRegion(nint hwnd, (int Left, int Top, int Right, int Bottom)? area)
    {
        var region = area is { } rect ? Native.CreateRectRgn(rect.Left, rect.Top, rect.Right, rect.Bottom) : 0;
        // On success the window owns the region.
        if (Native.SetWindowRgn(hwnd, region, true) == 0 && region != 0) Native.DeleteObject(region);
    }
}
