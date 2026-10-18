using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// App icons as PNG bytes, from the shell's own image factory: the same icon Explorer and the Start menu show,
/// including packaged-app logos, at the size asked for. Work runs on the shell STA thread; results are cached.
/// </summary>
public sealed class IconService
{
    // The app menu shows a few hundred apps on top of the dock's icons.
    private const int Capacity = 512;
    private readonly StaWorker _shell;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, byte[]? Bytes)>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Key, byte[]? Bytes)> _order = new();

    public IconService(StaWorker shell) => _shell = shell;

    /// <summary>Parsing name of a packaged or Start menu app, for <see cref="GetAsync"/>.</summary>
    public static string AppsFolderItem(string appId) => @"shell:AppsFolder\" + appId;

    /// <summary>
    /// Returns the first icon found among <paramref name="sources"/> (paths or shell parsing names), or null.
    /// </summary>
    public async Task<byte[]?> GetAsync(IEnumerable<string?> sources, int size)
    {
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            var key = source + "|" + size;
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    if (node.Value.Bytes is not null) return node.Value.Bytes;
                    continue;
                }
            }
            byte[]? bytes;
            try { bytes = await _shell.RunAsync(() => Extract(source, size)); }
            catch (Exception ex) when (ex is COMException or ExternalException or ArgumentException or InvalidOperationException)
            {
                LocalLog.Write("Warning", "icon-extract", ex);
                bytes = null;
            }
            Remember(key, bytes);
            if (bytes is not null) return bytes;
        }
        return null;
    }

    /// <summary>Forgets every size of an icon whose picture changed, such as the Recycle Bin when it is emptied.</summary>
    public void Invalidate(string source)
    {
        lock (_gate)
            foreach (var key in _cache.Keys.Where(key => key.StartsWith(source + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _order.Remove(_cache[key]);
                _cache.Remove(key);
            }
    }

    private void Remember(string key, byte[]? bytes)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var existing)) _order.Remove(existing);
            var node = _order.AddFirst((key, bytes));
            _cache[key] = node;
            while (_cache.Count > Capacity && _order.Last is { } last)
            {
                _cache.Remove(last.Value.Key);
                _order.RemoveLast();
            }
        }
    }

    private static byte[]? Extract(string source, int size)
    {
        object? item = null;
        var bitmap = (nint)0;
        try
        {
            if (ShellInterop.SHCreateItemFromParsingName(source, 0, ShellInterop.IidImageFactory, out item) != 0 ||
                item is not ShellInterop.IShellItemImageFactory factory) return null;
            if (factory.GetImage(new ShellInterop.Size { Width = size, Height = size }, ShellInterop.SiigbfIconOnly, out bitmap) != 0 ||
                bitmap == 0) return null;
            return ToPng(bitmap);
        }
        finally
        {
            if (bitmap != 0) Native.DeleteObject(bitmap);
            ShellInterop.Release(item);
        }
    }

    /// <summary>Copies a 32-bit DIB with its alpha channel; <see cref="Image.FromHbitmap(nint)"/> drops alpha.</summary>
    private static byte[]? ToPng(nint hbitmap)
    {
        if (Native.GetObjectW(hbitmap, Marshal.SizeOf<Native.Bitmap>(), out var info) == 0 || info.Width <= 0 || info.Height <= 0) return null;
        var width = info.Width;
        var height = Math.Abs(info.Height);
        var pixels = new byte[width * height * 4];
        var header = new Native.BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<Native.BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32
        };
        var screen = Native.GetDC(0);
        try
        {
            if (Native.GetDIBits(screen, hbitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally { Native.ReleaseDC(0, screen); }
        // Icons without an alpha channel come back fully transparent; treat them as opaque.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
        if (!hasAlpha) for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        using var image = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var row = 0; row < height; row++)
                Marshal.Copy(pixels, row * width * 4, data.Scan0 + row * data.Stride, width * 4);
        }
        finally { image.UnlockBits(data); }
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
