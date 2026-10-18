using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class TaskbarHidingTests : IDisposable
{
    private const int AlwaysOnTop = 0x2;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FloatDockTest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FakeShell : ITaskbarShell
    {
        public List<nint> Taskbars { get; } = [1, 2];
        public HashSet<nint> Visible { get; } = [1, 2];
        public HashSet<nint> Stuck { get; } = [];
        public int State { get; set; } = AlwaysOnTop;
        public IReadOnlyList<nint> FindTaskbars() => Taskbars.ToArray();
        public int GetAppBarState() => State;
        public void SetAppBarState(int state) => State = state;
        public bool IsVisible(nint taskbar) => Visible.Contains(taskbar);
        public void Hide(nint taskbar) { if (!Stuck.Contains(taskbar)) Visible.Remove(taskbar); }
        public void Show(nint taskbar) => Visible.Add(taskbar);
    }

    [Fact]
    public void HideJournalsOriginalStateAndHidesEveryTaskbar()
    {
        var shell = new FakeShell();
        var journal = new ShellStateJournal(_directory);
        var session = new TaskbarHidingSession(shell, journal);

        Assert.Equal(TaskbarHideResult.Hidden, session.Hide());
        Assert.Empty(shell.Visible);
        Assert.Equal(AlwaysOnTop | TaskbarHidingSession.AutoHide, shell.State);
        Assert.Equal(AlwaysOnTop, journal.Read()?.OriginalAppBarState);
    }

    [Fact]
    public void RestoreReturnsExactOriginalStateAndClearsJournal()
    {
        var shell = new FakeShell();
        var journal = new ShellStateJournal(_directory);
        var session = new TaskbarHidingSession(shell, journal);
        session.Hide();

        session.Restore();

        Assert.Equal(AlwaysOnTop, shell.State);
        Assert.Equal([1, 2], shell.Visible.Order());
        Assert.False(journal.Exists);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void JournalFromCrashedSessionKeepsTheRealOriginal()
    {
        var shell = new FakeShell { State = AlwaysOnTop | TaskbarHidingSession.AutoHide };
        var journal = new ShellStateJournal(_directory);
        journal.Write(new ShellState(AlwaysOnTop, 1234, DateTimeOffset.UtcNow));
        var session = new TaskbarHidingSession(shell, journal);

        session.Hide();
        session.Restore();

        Assert.Equal(AlwaysOnTop, shell.State);
    }

    [Fact]
    public void EnsureHiddenReactsOnlyWhenExplorerUndidTheChange()
    {
        var shell = new FakeShell();
        var session = new TaskbarHidingSession(shell, new ShellStateJournal(_directory));
        Assert.False(session.EnsureHidden());
        session.Hide();
        Assert.False(session.EnsureHidden());

        shell.Visible.Add(2);
        shell.State = AlwaysOnTop;
        Assert.True(session.EnsureHidden());
        Assert.Empty(shell.Visible);
        Assert.Equal(AlwaysOnTop | TaskbarHidingSession.AutoHide, shell.State);
    }

    [Fact]
    public void ReportsFailureWhenATaskbarStaysVisibleOrIsMissing()
    {
        var shell = new FakeShell();
        shell.Stuck.Add(2);
        Assert.Equal(TaskbarHideResult.HideFailed, new TaskbarHidingSession(shell, new ShellStateJournal(_directory)).Hide());

        var empty = new FakeShell();
        empty.Taskbars.Clear();
        Assert.Equal(TaskbarHideResult.TaskbarNotFound, new TaskbarHidingSession(empty, new ShellStateJournal(_directory + "-empty")).Hide());
    }

    [Fact]
    public void DoesNotHideWhenTheJournalCannotBeWritten()
    {
        var blocker = _directory + ".file";
        File.WriteAllText(blocker, string.Empty);
        try
        {
            var shell = new FakeShell();
            var session = new TaskbarHidingSession(shell, new ShellStateJournal(Path.Combine(blocker, "nested")));
            Assert.Equal(TaskbarHideResult.JournalUnavailable, session.Hide());
            Assert.Equal([1, 2], shell.Visible.Order());
            Assert.Equal(AlwaysOnTop, shell.State);
        }
        finally { File.Delete(blocker); }
    }

    [Fact]
    public void RecoverUndoesAnUnrestoredSessionAndToleratesCorruptJournal()
    {
        var shell = new FakeShell { State = AlwaysOnTop | TaskbarHidingSession.AutoHide };
        shell.Visible.Clear();
        var journal = new ShellStateJournal(_directory);
        Assert.False(TaskbarHidingSession.Recover(shell, journal));

        journal.Write(new ShellState(AlwaysOnTop, 1234, DateTimeOffset.UtcNow));
        Assert.True(TaskbarHidingSession.Recover(shell, journal));
        Assert.Equal(AlwaysOnTop, shell.State);
        Assert.Equal([1, 2], shell.Visible.Order());
        Assert.False(journal.Exists);

        shell.State = AlwaysOnTop | TaskbarHidingSession.AutoHide;
        File.WriteAllText(Path.Combine(_directory, "shell-state.json"), "{broken");
        Assert.True(TaskbarHidingSession.Recover(shell, journal));
        Assert.Equal(AlwaysOnTop, shell.State);
    }
}
