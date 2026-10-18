using System.Text;
using FloatDock.Core;

namespace FloatDock.Windows;

public sealed class WindowScanner
{
    private const long WsExAppWindow = 0x00040000;
    private const long WsExNoActivate = 0x08000000;
    private readonly AppIdentity _identity = new();

    /// <summary>Lists eligible top-level windows; <paramref name="isFlashing"/> marks windows asking for attention.</summary>
    public IReadOnlyList<WindowInfo> Scan(Func<nint, bool>? isFlashing = null)
    {
        var foreground = Native.GetForegroundWindow();
        var result = new List<WindowInfo>();
        var seen = new HashSet<nint>();
        Native.EnumWindows((hwnd, _) =>
        {
            seen.Add(hwnd);
            var info = Read(hwnd, foreground, isFlashing);
            if (info is not null && info.ProcessId != Environment.ProcessId && WindowEligibility.IsEligible(info)) result.Add(info);
            return true;
        }, 0);
        _identity.Forget(seen);
        return result;
    }

    /// <summary>Re-reads AppUserModelIDs on the next scan; apps may set theirs after creating a window.</summary>
    public void ForgetIdentities() => _identity.Clear();

    private WindowInfo? Read(nint hwnd, nint foreground, Func<nint, bool>? isFlashing)
    {
        if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) || !Native.GetWindowRect(hwnd, out var rect)) return null;
        var title = new StringBuilder(512);
        Native.GetWindowTextW(hwnd, title, title.Capacity);
        var className = new StringBuilder(256);
        Native.GetClassNameW(hwnd, className, className.Capacity);
        Native.GetWindowThreadProcessId(hwnd, out var processId);
        var style = Native.GetWindowLongPtr(hwnd, Native.GwlStyle).ToInt64();
        var extended = Native.GetWindowLongPtr(hwnd, Native.GwlExStyle).ToInt64();
        var cloaked = Native.DwmGetWindowAttribute(hwnd, 14, out var value, sizeof(int)) == 0 && value != 0;
        var info = new WindowInfo(hwnd, processId, title.ToString(), className.ToString(), ProcessPath(processId),
            true, (style & Native.WsChild) != 0, (extended & Native.WsExToolWindow) != 0,
            Native.GetWindow(hwnd, Native.GwOwner) != 0, cloaked, rect.Width > 0 && rect.Height > 0, hwnd == foreground,
            IsAppWindow: (extended & WsExAppWindow) != 0, IsNoActivate: (extended & WsExNoActivate) != 0,
            IsFlashing: isFlashing?.Invoke(hwnd) ?? false);
        // Identity costs COM calls; only resolve it for windows that will be shown.
        return WindowEligibility.IsEligible(info) && processId != Environment.ProcessId
            ? info with { AppId = _identity.Resolve(hwnd, processId, info.ClassName) }
            : info;
    }

    private static string? ProcessPath(uint processId)
    {
        if (processId == 0) return null;
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, processId);
        if (handle == 0) return null;
        try
        {
            var buffer = new StringBuilder(32768);
            uint size = (uint)buffer.Capacity;
            return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally { Native.CloseHandle(handle); }
    }
}
