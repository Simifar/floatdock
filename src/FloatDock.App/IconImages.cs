using Microsoft.UI.Xaml.Media.Imaging;

namespace FloatDock.App;

internal static class IconImages
{
    /// <summary>Decodes PNG bytes from <see cref="FloatDock.Windows.IconService"/>.</summary>
    public static async Task<BitmapImage> FromPngAsync(byte[] bytes)
    {
        using var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new global::Windows.Storage.Streams.DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
