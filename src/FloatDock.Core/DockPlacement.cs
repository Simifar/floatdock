namespace FloatDock.Core;

/// <summary>Where the dock window goes on a display; pure maths, so displays left of or above the primary one are tested.</summary>
public static class DockPlacement
{
    public const double BottomMargin = 12;
    public const double HiddenReveal = 4;

    /// <summary>
    /// The window rectangle in screen pixels. <paramref name="area"/> is the part of the display the dock may use
    /// (the work area, or the whole display when the taskbar is hidden) and <paramref name="bottom"/> the lowest row it may
    /// occupy (an AppBar band can move it up). A dock wider than the display is cut to it. In auto-hide the window sits
    /// below its place with only a thin strip showing.
    /// </summary>
    public static ScreenRect Compute(ScreenRect area, int bottom, int width, int height, double scale, bool hidden)
    {
        var fitted = Math.Min(Math.Max(1, width), Math.Max(1, area.Width));
        var x = area.X + (area.Width - fitted) / 2;
        var y = bottom - height - (int)(BottomMargin * scale);
        if (hidden) y += height - (int)(HiddenReveal * scale);
        return new ScreenRect(x, y, fitted, height);
    }
}
