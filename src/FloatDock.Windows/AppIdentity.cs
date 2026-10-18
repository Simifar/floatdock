using System.Collections.Concurrent;
using System.Text;

namespace FloatDock.Windows;

/// <summary>
/// AppUserModelID of a window, as the taskbar uses for grouping: the window's own property, then the packaged
/// identity of its process, then of the process behind an ApplicationFrameHost frame. Null for plain desktop apps.
/// </summary>
internal sealed class AppIdentity
{
    private const int AppModelErrorNoApplication = 15703;
    private readonly ConcurrentDictionary<nint, (uint ProcessId, string? AppId)> _cache = new();

    public string? Resolve(nint hwnd, uint processId, string className)
    {
        if (_cache.TryGetValue(hwnd, out var cached) && cached.ProcessId == processId) return cached.AppId;
        var appId = FromWindow(hwnd) ?? FromProcess(processId) ??
            (className == "ApplicationFrameWindow" ? FromHostedCoreWindow(hwnd, processId) : null);
        _cache[hwnd] = (processId, appId);
        return appId;
    }

    /// <summary>Drops entries for windows that no longer exist, and lets the rest be re-read later.</summary>
    public void Forget(IReadOnlySet<nint> alive)
    {
        foreach (var hwnd in _cache.Keys)
            if (!alive.Contains(hwnd)) _cache.TryRemove(hwnd, out _);
    }

    public void Clear() => _cache.Clear();

    private static string? FromWindow(nint hwnd)
    {
        object? store = null;
        try
        {
            if (ShellInterop.SHGetPropertyStoreForWindow(hwnd, ShellInterop.IidPropertyStore, out store) != 0 ||
                store is not ShellInterop.IPropertyStore properties) return null;
            if (properties.GetValue(ShellInterop.AppUserModelId, out var value) != 0) return null;
            // Any process can set this property on its own windows; accept only a plain AppUserModelID, never a path.
            var id = ShellInterop.ReadString(ref value)?.Trim();
            return id is not null && Core.LaunchTarget.IsAppUserModelId(id) ? id : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException) { return null; }
        finally { ShellInterop.Release(store); }
    }

    internal static string? FromProcess(uint processId)
    {
        if (processId == 0) return null;
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, processId);
        if (handle == 0) return null;
        try
        {
            uint length = 0;
            var result = ShellInterop.GetApplicationUserModelId(handle, ref length, null);
            if (result == AppModelErrorNoApplication || length == 0) return null;
            var buffer = new StringBuilder((int)length);
            return ShellInterop.GetApplicationUserModelId(handle, ref length, buffer) == 0 ? buffer.ToString() : null;
        }
        finally { Native.CloseHandle(handle); }
    }

    /// <summary>A UWP frame window hosts the app's CoreWindow, which belongs to the app process.</summary>
    private static string? FromHostedCoreWindow(nint frame, uint frameProcess)
    {
        string? found = null;
        Native.EnumChildWindows(frame, (child, _) =>
        {
            Native.GetWindowThreadProcessId(child, out var childProcess);
            if (childProcess == frameProcess) return true;
            found = FromProcess(childProcess);
            return found is null;
        }, 0);
        return found;
    }
}
