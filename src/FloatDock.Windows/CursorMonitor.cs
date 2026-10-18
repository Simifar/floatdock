namespace FloatDock.Windows;

public sealed class CursorMonitor
{
    public (int X, int Y)? Position => Native.GetCursorPos(out var point) ? (point.X, point.Y) : null;
}
