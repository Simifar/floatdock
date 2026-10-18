using System.Diagnostics;
using System.Runtime.InteropServices;
using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// Registers the dock as an AppBar on the bottom screen edge so maximized windows stop above it.
/// Use on the dock window's thread; notifications arrive through <see cref="WindowMessageHook"/>.
/// </summary>
public sealed class AppBarHost : IDisposable
{
    public const int NotifyStateChange = 0, NotifyPositionChanged = 1, NotifyFullscreenApp = 2;
    private const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3;
    private const uint AbeBottom = 3;
    private static readonly AppBarJournal Journal = new();
    private readonly nint _hwnd;
    private readonly uint _callbackMessage;
    private (int Left, int Top, int Right, int Bottom, int Height)? _request;

    public AppBarHost(nint hwnd, uint callbackMessage)
    {
        _hwnd = hwnd;
        _callbackMessage = callbackMessage;
    }

    public bool IsRegistered { get; private set; }

    /// <summary>The bottom edge band currently reserved, in screen pixels.</summary>
    public (int Left, int Top, int Right, int Bottom)? Reserved { get; private set; }

    /// <summary>
    /// Reserves <paramref name="height"/> pixels at the bottom of the monitor. Explorer may move the band up,
    /// for example above a visible taskbar. Repeated calls with the same request do nothing unless forced.
    /// </summary>
    public (int Left, int Top, int Right, int Bottom)? Reserve((int Left, int Top, int Right, int Bottom) monitor, int height, bool force = false)
    {
        var request = (monitor.Left, monitor.Top, monitor.Right, monitor.Bottom, height);
        if (!force && IsRegistered && _request == request) return Reserved;
        if (!Register()) return null;
        var data = Data();
        data.Edge = AbeBottom;
        data.Bounds = new Native.Rect { Left = monitor.Left, Top = monitor.Bottom - height, Right = monitor.Right, Bottom = monitor.Bottom };
        Native.SHAppBarMessage(AbmQueryPos, ref data);
        data.Bounds.Top = data.Bounds.Bottom - height;
        var band = (data.Bounds.Left, data.Bounds.Top, data.Bounds.Right, data.Bounds.Bottom);
        _request = request;
        // Setting an unchanged band would notify every AppBar again, including this one.
        if (Reserved == band) return Reserved;
        Native.SHAppBarMessage(AbmSetPos, ref data);
        Reserved = (data.Bounds.Left, data.Bounds.Top, data.Bounds.Right, data.Bounds.Bottom);
        return Reserved;
    }

    public void Unregister()
    {
        if (!IsRegistered) return;
        var data = Data();
        Native.SHAppBarMessage(AbmRemove, ref data);
        try { Journal.Remove(_hwnd); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Warning", "appbar-journal-remove", ex); }
        IsRegistered = false;
        Reserved = null;
        _request = null;
    }

    /// <summary>
    /// Removes every journaled registration (one per dock window) whose owner process is gone, or that belongs to this
    /// dying process. Safe from any process.
    /// </summary>
    public static bool RemoveLeftover()
    {
        var leftover = AppBarJournal.Leftover(Journal.Read(), Environment.ProcessId, IsAlive);
        foreach (var registration in leftover)
        {
            var data = new Native.AppBarData { Size = (uint)Marshal.SizeOf<Native.AppBarData>(), Window = (nint)registration.Window };
            Native.SHAppBarMessage(AbmRemove, ref data);
        }
        try { if (leftover.Count > 0) Journal.Remove(leftover); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Warning", "appbar-journal-remove", ex); }
        return leftover.Count > 0;
    }

    private bool Register()
    {
        if (IsRegistered) return true;
        var data = Data();
        data.CallbackMessage = _callbackMessage;
        if (Native.SHAppBarMessage(AbmNew, ref data) == 0)
        {
            LocalLog.Write("Warning", "appbar-register-failed");
            return false;
        }
        IsRegistered = true;
        try { Journal.Write(new AppBarRegistration(_hwnd, Environment.ProcessId)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Warning", "appbar-journal-write", ex); }
        return true;
    }

    private Native.AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<Native.AppBarData>(), Window = _hwnd };

    private static bool IsAlive(AppBarRegistration registration)
    {
        var hwnd = (nint)registration.Window;
        if (!Native.IsWindow(hwnd) || ExplorerTaskbarShell.ProcessOf(hwnd) != registration.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById(registration.ProcessId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose() => Unregister();
}
