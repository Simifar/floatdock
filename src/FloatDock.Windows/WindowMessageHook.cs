using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>Shell and session messages for a top-level window that XAML does not surface.</summary>
public sealed class WindowMessageHook : IDisposable
{
    private readonly nint _hwnd;
    private readonly Native.SubclassProc _callback;
    private readonly uint _taskbarCreated;
    private bool _attached;

    /// <summary>Explorer (re)created its taskbar, for example after an Explorer restart.</summary>
    public event Action? TaskbarCreated;
    /// <summary>WM_QUERYENDSESSION: logoff or shutdown may follow.</summary>
    public event Action? SessionEnding;
    /// <summary>WM_ENDSESSION; true when the session really ends, false when it was cancelled.</summary>
    public event Action<bool>? SessionEnded;
    /// <summary>AppBar notification (ABN_* code, lParam) for the callback message <see cref="AppBarMessage"/>.</summary>
    public event Action<int, nint>? AppBarNotification;

    /// <summary>Shell hook notification (HSHELL_* code, window) after <see cref="Native.RegisterShellHookWindow"/>.</summary>
    public event Action<int, nint>? ShellHook;

    /// <summary>A shortcut from <see cref="RegisterHotKey"/> was pressed; the argument is its id.</summary>
    public event Action<int>? HotKeyPressed;

    /// <summary>The window's display scale changed (WM_DPICHANGED), for example by moving it to another display.</summary>
    public event Action? DpiChanged;

    /// <summary>Callback message to register with <see cref="AppBarHost"/>.</summary>
    public uint AppBarMessage { get; }

    private readonly uint _shellHookMessage;
    private bool _shellHookRegistered;

    public WindowMessageHook(nint hwnd)
    {
        _hwnd = hwnd;
        _callback = OnMessage;
        _taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");
        AppBarMessage = Native.RegisterWindowMessageW("FloatDock.AppBarNotify");
        _shellHookMessage = Native.RegisterWindowMessageW("SHELLHOOK");
        _attached = Native.SetWindowSubclass(hwnd, _callback, 1, 0);
        if (!_attached) LocalLog.Write("Warning", "window-subclass-unavailable");
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (_taskbarCreated != 0 && message == _taskbarCreated) TaskbarCreated?.Invoke();
            else if (message == Native.WmQueryEndSession) SessionEnding?.Invoke();
            else if (message == Native.WmEndSession) SessionEnded?.Invoke(wParam != 0);
            else if (AppBarMessage != 0 && message == AppBarMessage) AppBarNotification?.Invoke((int)wParam, lParam);
            else if (_shellHookMessage != 0 && message == _shellHookMessage) ShellHook?.Invoke((int)wParam, lParam);
            else if (message == Native.WmHotKey) HotKeyPressed?.Invoke((int)wParam);
            else if (message == Native.WmDpiChanged) DpiChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // An exception must not unwind through the native window procedure.
            LocalLog.Write("Error", "window-message", ex);
        }
        return Native.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    /// <summary>Starts <see cref="ShellHook"/> notifications, the ones the taskbar itself relies on.</summary>
    public void RegisterShellHook()
    {
        if (_shellHookRegistered || !_attached) return;
        _shellHookRegistered = Native.RegisterShellHookWindow(_hwnd);
        if (!_shellHookRegistered) LocalLog.Write("Warning", "shell-hook-unavailable");
    }

    /// <summary>Registers a global shortcut; false when another program already owns it. Held keys do not repeat the event.</summary>
    public bool RegisterHotKey(int id, HotKeyGesture gesture)
    {
        UnregisterHotKey(id);
        if (!_attached || !Native.RegisterHotKey(_hwnd, id, (uint)gesture.Modifiers | Native.ModNoRepeat, gesture.VirtualKey)) return false;
        _hotKeys.Add(id);
        return true;
    }

    public void UnregisterHotKey(int id)
    {
        if (_hotKeys.Remove(id)) Native.UnregisterHotKey(_hwnd, id);
    }

    private readonly HashSet<int> _hotKeys = [];

    public void Dispose()
    {
        foreach (var id in _hotKeys.ToList()) UnregisterHotKey(id);
        if (_shellHookRegistered) Native.DeregisterShellHookWindow(_hwnd);
        _shellHookRegistered = false;
        if (!_attached) return;
        Native.RemoveWindowSubclass(_hwnd, _callback, 1);
        _attached = false;
    }
}
