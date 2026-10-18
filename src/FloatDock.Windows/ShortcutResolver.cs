using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace FloatDock.Windows;

public sealed class ShortcutResolver
{
    public string? GetExecutablePath(string shortcutPath)
    {
        if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(shortcutPath)) return null;
        object? instance = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
            if (type is null) return null;
            instance = Activator.CreateInstance(type);
            if (instance is not IPersistFile persist || instance is not IShellLinkW link) return null;
            persist.Load(shortcutPath, 0);
            var path = new StringBuilder(260);
            var result = link.GetPath(path, path.Capacity, 0, 0);
            return result == 0 && path.Length > 0 && File.Exists(path.ToString()) ? path.ToString() : null;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally { if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, nint findData, uint flags);
    }
}
