using System.Runtime.InteropServices;

namespace FloatDock.Windows;

/// <summary>Whether the process runs from an MSIX package (the Microsoft Store edition) rather than from a plain folder.</summary>
public static class PackageIdentity
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged { get; } = DetectPackage();

    private static bool DetectPackage()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
