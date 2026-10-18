namespace FloatDock.Core;

public sealed record FolderEntry(string Path, string Name, bool IsFolder, DateTime Modified);

public static class FolderListing
{
    /// <summary>
    /// A name safe to show: bidirectional control characters, which can make "gpj.exe" read as "exe.jpg", become a visible placeholder.
    /// </summary>
    public static string DisplayName(string name) =>
        string.Create(name.Length, name, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++) span[i] = IsBidiControl(source[i]) ? (char)0xFFFD : source[i];
        });

    // Written as numbers: such characters must not appear in the source itself.
    private static bool IsBidiControl(char c) => c is (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069) or (char)0x200E or (char)0x200F;

    /// <summary>The newest entries of a folder, folders and files together, without hidden or system items.</summary>
    public static IReadOnlyList<FolderEntry> Recent(string folder, int take)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFileSystemInfos()
                .Where(info => (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 &&
                    !info.Name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) &&
                    !info.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(take)
                .Select(info => new FolderEntry(info.FullName, DisplayName(info.Name), info is DirectoryInfo, info.LastWriteTime))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LocalLog.Write("Warning", "folder-listing", ex);
            return [];
        }
    }
}
