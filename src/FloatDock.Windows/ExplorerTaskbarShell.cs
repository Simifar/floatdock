using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// Explorer taskbar windows and AppBar state. Explorer's window classes are not a shell replacement contract.
/// </summary>
internal sealed class ExplorerTaskbarShell : ITaskbarShell
{
    internal const string PrimaryClass = "Shell_TrayWnd";
    internal const string SecondaryClass = "Shell_SecondaryTrayWnd";
    private readonly HashSet<nint> _verified = [];

    public IReadOnlyList<nint> FindTaskbars()
    {
        var result = new List<nint>();
        var primary = Native.FindWindowW(PrimaryClass, null);
        if (IsExplorerTaskbar(primary)) result.Add(primary);
        for (var hwnd = Native.FindWindowExW(0, 0, SecondaryClass, null); hwnd != 0;
             hwnd = Native.FindWindowExW(0, hwnd, SecondaryClass, null))
            if (IsExplorerTaskbar(hwnd)) result.Add(hwnd);
        _verified.IntersectWith(result);
        return result;
    }

    public int GetAppBarState()
    {
        var data = new Native.AppBarData { Size = (uint)Marshal.SizeOf<Native.AppBarData>() };
        return (int)Native.SHAppBarMessage(Native.AbmGetState, ref data);
    }

    public void SetAppBarState(int state)
    {
        var data = new Native.AppBarData
        {
            Size = (uint)Marshal.SizeOf<Native.AppBarData>(),
            Window = Native.FindWindowW(PrimaryClass, null),
            Parameter = state
        };
        Native.SHAppBarMessage(Native.AbmSetState, ref data);
    }

    public bool IsVisible(nint taskbar) => Native.IsWindowVisible(taskbar);

    public void Hide(nint taskbar) => Native.ShowWindow(taskbar, Native.SwHide);

    public void Show(nint taskbar) => Native.ShowWindow(taskbar, Native.SwShowNoActivate);

    internal static bool HasTaskbarClass(nint hwnd)
    {
        var name = new StringBuilder(32);
        Native.GetClassNameW(hwnd, name, name.Capacity);
        return name.ToString() is PrimaryClass or SecondaryClass;
    }

    internal static uint ProcessOf(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var processId);
        return processId;
    }

    private bool IsExplorerTaskbar(nint hwnd)
    {
        if (hwnd == 0 || !Native.IsWindow(hwnd)) return false;
        if (_verified.Contains(hwnd)) return true;
        try
        {
            using var process = Process.GetProcessById((int)ProcessOf(hwnd));
            if (!process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
        _verified.Add(hwnd);
        return true;
    }
}
