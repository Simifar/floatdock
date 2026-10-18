using System.Runtime.InteropServices;
using System.Text;

namespace FloatDock.Windows;

/// <summary>Documented shell COM interfaces and helpers used for app identity, the app catalog and icons.</summary>
internal static class ShellInterop
{
    internal const uint SigdnNormalDisplay = 0;
    internal const uint SigdnParentRelativeParsing = 0x80018001;
    internal const uint SigdnFileSysPath = 0x80058000;
    internal const int SiigbfIconOnly = 0x4;
    internal const ushort VtLpwstr = 31;
    internal static readonly Guid BhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    internal static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    internal static readonly Guid IidShellItem2 = new("7e9fb0d3-919f-4307-ab2e-9b1860310c93");
    internal static readonly Guid IidEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    internal static readonly Guid IidPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    internal static readonly Guid IidImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    internal static PropertyKey AppUserModelId => new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    internal static PropertyKey LinkTargetParsingPath => new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PropertyKey(Guid format, uint id)
    {
        internal readonly Guid Format = format;
        internal readonly uint Id = id;
    }

    /// <summary>PROPVARIANT, read only as a string here.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal nint Pointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size { internal int Width, Height; }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint bindContext, in Guid handler, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint form, out nint name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem2
    {
        [PreserveSig] int BindToHandler(nint bindContext, in Guid handler, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint form, out nint name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
        [PreserveSig] int GetPropertyStore(int flags, in Guid iid, out nint store);
        [PreserveSig] int GetPropertyStoreWithCreateObject(int flags, nint createObject, in Guid iid, out nint store);
        [PreserveSig] int GetPropertyStoreForKeys(nint keys, uint count, int flags, in Guid iid, out nint store);
        [PreserveSig] int GetPropertyDescriptionList(in PropertyKey key, in Guid iid, out nint list);
        [PreserveSig] int Update(nint bindContext);
        [PreserveSig] int GetProperty(in PropertyKey key, out PropVariant value);
        [PreserveSig] int GetCLSID(in PropertyKey key, out Guid clsid);
        [PreserveSig] int GetFileTime(in PropertyKey key, out long time);
        [PreserveSig] int GetInt32(in PropertyKey key, out int value);
        [PreserveSig] int GetString(in PropertyKey key, out nint value);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItem item, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems copy);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, int flags, out nint bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHGetPropertyStoreFromParsingName(string path, nint bindContext, int flags, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object store);

    [DllImport("shell32.dll")]
    internal static extern int SHGetPropertyStoreForWindow(nint hwnd, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out object store);

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder? id);

    /// <summary>Takes ownership of a CoTaskMem string returned by the shell.</summary>
    internal static string? TakeString(nint pointer)
    {
        if (pointer == 0) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    internal static string? ReadString(ref PropVariant value)
    {
        try { return value.Type == VtLpwstr && value.Pointer != 0 ? Marshal.PtrToStringUni(value.Pointer) : null; }
        finally { PropVariantClear(ref value); }
    }

    internal static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }
}
