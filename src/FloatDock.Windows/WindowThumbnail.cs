using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// A live DWM thumbnail of another window, drawn by DWM on top of the destination window's client area.
/// It must be disposed (or the destination closed) to stop the drawing.
/// </summary>
public sealed class WindowThumbnail : IDisposable
{
    private const uint RectDestination = 0x1, Opacity = 0x4, Visible = 0x8, SourceClientAreaOnly = 0x10;
    private nint _handle;

    private WindowThumbnail(nint handle) => _handle = handle;

    /// <summary>Null when DWM refuses (the source is gone, or elevated and the dock is not).</summary>
    public static WindowThumbnail? Create(nint destination, nint source)
    {
        if (!Native.IsWindow(source) || Native.DwmRegisterThumbnail(destination, source, out var handle) != 0 || handle == 0) return null;
        return new WindowThumbnail(handle);
    }

    /// <summary>The window's size in pixels, or null when it cannot be read.</summary>
    public (int Width, int Height)? SourceSize =>
        _handle != 0 && Native.DwmQueryThumbnailSourceSize(_handle, out var size) == 0 && size.Width > 0 && size.Height > 0
            ? (size.Width, size.Height) : null;

    /// <summary>Draws the thumbnail inside a rectangle of the destination window, as large as fits without distortion.</summary>
    public void Show(int left, int top, int right, int bottom)
    {
        if (_handle == 0) return;
        var area = new Native.Rect { Left = left, Top = top, Right = right, Bottom = bottom };
        if (SourceSize is { } source)
        {
            var scale = Math.Min((double)area.Width / source.Width, (double)area.Height / source.Height);
            var width = (int)(source.Width * scale);
            var height = (int)(source.Height * scale);
            area = new Native.Rect
            {
                Left = left + (right - left - width) / 2, Top = top + (bottom - top - height) / 2,
                Right = left + (right - left + width) / 2, Bottom = top + (bottom - top + height) / 2
            };
        }
        var properties = new Native.ThumbnailProperties
        {
            Flags = RectDestination | Opacity | Visible | SourceClientAreaOnly,
            Destination = area, Opacity = 255, Visible = true, SourceClientAreaOnly = false
        };
        if (Native.DwmUpdateThumbnailProperties(_handle, ref properties) != 0) LocalLog.Write("Warning", "thumbnail-update");
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        Native.DwmUnregisterThumbnail(_handle);
        _handle = 0;
    }
}
