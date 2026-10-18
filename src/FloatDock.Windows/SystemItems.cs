using System.Runtime.InteropServices;

namespace FloatDock.Windows;

/// <summary>The Recycle Bin, as the Shell reports and empties it.</summary>
public static class RecycleBin
{
    public const string FolderName = "shell:RecycleBinFolder";

    /// <summary>Number of items in all Recycle Bins, or null when the Shell cannot tell.</summary>
    public static long? ItemCount()
    {
        var info = new Native.RecycleBinInfo { Size = (uint)Marshal.SizeOf<Native.RecycleBinInfo>() };
        return Native.SHQueryRecycleBinW(null, ref info) == 0 ? info.Items : null;
    }

    /// <summary>
    /// Empties every Recycle Bin. The Shell asks the user to confirm and shows its own progress, so nothing is
    /// deleted unless the user agrees in that dialog.
    /// </summary>
    public static bool Empty(nint owner) => Native.SHEmptyRecycleBinW(owner, null, 0) == 0;
}

public static class KnownFolders
{
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>The user's Downloads folder (it can be moved, so it is asked of the Shell).</summary>
    public static string? DownloadsPath()
    {
        var id = Downloads;
        if (Native.SHGetKnownFolderPath(ref id, 0, 0, out var pointer) != 0) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Native.CoTaskMemFree(pointer); }
    }
}
