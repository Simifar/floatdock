namespace FloatDock.Core;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

public static class FullscreenGeometry
{
    public static bool IsUserFullscreen(ScreenRect window, ScreenRect monitor, string className) =>
        className is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") &&
        Covers(window, monitor);

    public static bool Covers(ScreenRect window, ScreenRect monitor, int tolerance = 3) =>
        window.Width > 0 && window.Height > 0 &&
        window.X <= monitor.X + tolerance && window.Y <= monitor.Y + tolerance &&
        window.Right >= monitor.Right - tolerance && window.Bottom >= monitor.Bottom - tolerance;
}
