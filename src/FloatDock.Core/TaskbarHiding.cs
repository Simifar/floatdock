namespace FloatDock.Core;

/// <summary>Explorer taskbar operations. Implemented with Win32 in FloatDock.Windows and faked in tests.</summary>
public interface ITaskbarShell
{
    /// <summary>All Explorer taskbar windows, primary first. Empty when Explorer has no taskbar.</summary>
    IReadOnlyList<nint> FindTaskbars();
    /// <summary>ABM_GETSTATE bits.</summary>
    int GetAppBarState();
    void SetAppBarState(int state);
    bool IsVisible(nint taskbar);
    void Hide(nint taskbar);
    void Show(nint taskbar);
}

public enum TaskbarHideResult { Hidden, TaskbarNotFound, HideFailed, JournalUnavailable }

/// <summary>
/// Hides every Explorer taskbar and frees its work area with ABS_AUTOHIDE, then undoes exactly that.
/// The original state is journaled first, because ABS_AUTOHIDE is a persistent user setting.
/// </summary>
public sealed class TaskbarHidingSession(ITaskbarShell shell, ShellStateJournal journal)
{
    public const int AutoHide = 0x1;

    private int _originalState;

    public bool IsActive { get; private set; }

    public TaskbarHideResult Hide()
    {
        var taskbars = shell.FindTaskbars();
        if (taskbars.Count == 0) return TaskbarHideResult.TaskbarNotFound;
        if (!IsActive)
        {
            // A journal left by a crashed session holds the real original; the current state may be ours.
            var original = journal.Exists
                ? journal.Read()?.OriginalAppBarState ?? shell.GetAppBarState() & ~AutoHide
                : shell.GetAppBarState();
            try { journal.Write(new ShellState(original, Environment.ProcessId, DateTimeOffset.UtcNow)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LocalLog.Write("Error", "shell-journal-write", ex);
                return TaskbarHideResult.JournalUnavailable;
            }
            _originalState = original;
            IsActive = true;
        }
        shell.SetAppBarState(_originalState | AutoHide);
        foreach (var taskbar in taskbars) shell.Hide(taskbar);
        return taskbars.Any(shell.IsVisible) ? TaskbarHideResult.HideFailed : TaskbarHideResult.Hidden;
    }

    /// <summary>Re-applies hiding after Explorer showed or recreated a taskbar. Returns true when something changed.</summary>
    public bool EnsureHidden()
    {
        if (!IsActive) return false;
        var changed = false;
        if ((shell.GetAppBarState() & AutoHide) == 0)
        {
            shell.SetAppBarState(_originalState | AutoHide);
            changed = true;
        }
        foreach (var taskbar in shell.FindTaskbars().Where(shell.IsVisible))
        {
            shell.Hide(taskbar);
            changed = true;
        }
        return changed;
    }

    public void Restore()
    {
        if (!IsActive) return;
        shell.SetAppBarState(_originalState);
        ShowAll(shell);
        journal.Delete();
        IsActive = false;
    }

    /// <summary>Undoes a change left by a session that did not restore. Returns false when there was nothing to undo.</summary>
    public static bool Recover(ITaskbarShell shell, ShellStateJournal journal)
    {
        if (!journal.Exists) return false;
        shell.SetAppBarState(journal.Read()?.OriginalAppBarState ?? shell.GetAppBarState() & ~AutoHide);
        ShowAll(shell);
        journal.Delete();
        return true;
    }

    public static void ShowAll(ITaskbarShell shell)
    {
        foreach (var taskbar in shell.FindTaskbars()) shell.Show(taskbar);
    }
}
