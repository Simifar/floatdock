using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// Optional, isolated Explorer taskbar integration. Create and use it on a thread with a message loop:
/// the Explorer show-event hook is delivered there.
/// </summary>
public sealed class TaskbarController : IDisposable
{
    private static readonly ShellStateJournal Journal = new();
    private readonly ExplorerTaskbarShell _shell = new();
    private readonly TaskbarHidingSession _session;
    private readonly Native.WinEventProc _callback;
    private nint _hook;
    private uint _hookedProcess;

    /// <summary>Explorer showed one of its taskbars. Raised on the thread that hid the taskbar.</summary>
    public event Action? TaskbarShown;

    public TaskbarController()
    {
        _session = new TaskbarHidingSession(_shell, Journal);
        _callback = OnWinEvent;
    }

    public bool IsHidden => _session.IsActive;

    public TaskbarHideResult TryHide()
    {
        var result = _session.Hide();
        if (result == TaskbarHideResult.Hidden) WatchExplorer();
        else Restore();
        return result;
    }

    /// <summary>Hides taskbars Explorer showed or recreated. Returns true when something changed.</summary>
    public bool EnsureHidden()
    {
        if (!_session.IsActive) return false;
        WatchExplorer();
        return _session.EnsureHidden();
    }

    public void Restore()
    {
        Unwatch();
        _session.Restore();
    }

    /// <summary>Undoes a hide left by a session that crashed. Safe to call from any thread and any process.</summary>
    public static bool RecoverFromJournal() => TaskbarHidingSession.Recover(new ExplorerTaskbarShell(), Journal);

    /// <summary>Last-resort recovery: undo the journaled change, or at least show every taskbar.</summary>
    public static void RecoverOrShowAll()
    {
        var shell = new ExplorerTaskbarShell();
        if (!TaskbarHidingSession.Recover(shell, Journal)) TaskbarHidingSession.ShowAll(shell);
    }

    private void WatchExplorer()
    {
        var taskbar = Native.FindWindowW(ExplorerTaskbarShell.PrimaryClass, null);
        var processId = taskbar == 0 ? 0 : ExplorerTaskbarShell.ProcessOf(taskbar);
        if (processId == 0 || (processId == _hookedProcess && _hook != 0)) return;
        Unwatch();
        _hook = Native.SetWinEventHook(Native.EventObjectShow, Native.EventObjectShow, 0, _callback, processId, 0,
            Native.WineventOutOfContext);
        if (_hook != 0) _hookedProcess = processId;
        else LocalLog.Write("Warning", "taskbar-hook-unavailable");
    }

    private void Unwatch()
    {
        if (_hook != 0) Native.UnhookWinEvent(_hook);
        _hook = 0;
        _hookedProcess = 0;
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (hwnd == 0 || objectId != 0 || childId != 0 || !_session.IsActive) return;
        if (ExplorerTaskbarShell.HasTaskbarClass(hwnd)) TaskbarShown?.Invoke();
    }

    public void Dispose() => Restore();
}
