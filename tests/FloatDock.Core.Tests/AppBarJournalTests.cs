using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class AppBarJournalTests
{
    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "floatdock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void KeepsOneRegistrationPerDockWindow()
    {
        var directory = TempDirectory();
        try
        {
            var journal = new AppBarJournal(directory);
            journal.Write(new AppBarRegistration(100, 7));
            journal.Write(new AppBarRegistration(200, 7));
            Assert.Equal([new AppBarRegistration(100, 7), new AppBarRegistration(200, 7)], journal.Read());

            // A window that registers again replaces its entry; it does not add a second one.
            journal.Write(new AppBarRegistration(100, 8));
            Assert.Equal([new AppBarRegistration(200, 7), new AppBarRegistration(100, 8)], journal.Read());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RemovingOneWindowKeepsTheOthersAndTheLastRemovalDeletesTheFile()
    {
        var directory = TempDirectory();
        try
        {
            var journal = new AppBarJournal(directory);
            journal.Write(new AppBarRegistration(100, 7));
            journal.Write(new AppBarRegistration(200, 7));
            journal.Remove(100);
            Assert.Equal(new AppBarRegistration(200, 7), Assert.Single(journal.Read()));
            journal.Remove(999);
            Assert.Single(journal.Read());
            journal.Remove(200);
            Assert.Empty(journal.Read());
            Assert.False(File.Exists(Path.Combine(directory, "appbar.json")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RemovingSeveralAtOnceLeavesTheRest()
    {
        var directory = TempDirectory();
        try
        {
            var journal = new AppBarJournal(directory);
            foreach (var window in new long[] { 1, 2, 3 }) journal.Write(new AppBarRegistration(window, 7));
            journal.Remove([new AppBarRegistration(1, 7), new AppBarRegistration(3, 7)]);
            Assert.Equal(2, Assert.Single(journal.Read()).Window);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ReadsTheSingleObjectAnEarlierVersionWroteAndRewritesItAsAList()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "appbar.json");
            File.WriteAllText(path, """{"Window":65662,"ProcessId":7}""");
            var journal = new AppBarJournal(directory);
            Assert.Equal(new AppBarRegistration(65662, 7), Assert.Single(journal.Read()));

            journal.Write(new AppBarRegistration(70000, 7));
            Assert.StartsWith("[", File.ReadAllText(path).TrimStart());
            Assert.Equal(2, journal.Read().Count);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("""{"Window":"x"}""")]
    public void AnUnreadableOrEmptyFileReadsAsNothingAndIsReplacedByTheNextWrite(string content)
    {
        var directory = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "appbar.json"), content);
            var journal = new AppBarJournal(directory);
            Assert.Empty(journal.Read());
            journal.Write(new AppBarRegistration(5, 1));
            Assert.Equal(new AppBarRegistration(5, 1), Assert.Single(journal.Read()));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void MissingFileReadsAsNothing()
    {
        var directory = TempDirectory();
        try { Assert.Empty(new AppBarJournal(directory).Read()); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void LeftoverIsWhatBelongsToTheCallerOrToADeadOwner()
    {
        var registrations = new[]
        {
            new AppBarRegistration(1, 10),   // this process
            new AppBarRegistration(2, 11),   // another process, gone
            new AppBarRegistration(3, 12),   // another process, still running
            new AppBarRegistration(4, 10)    // this process, second dock
        };
        var leftover = AppBarJournal.Leftover(registrations, currentProcessId: 10, ownerIsAlive: r => r.ProcessId == 12);
        Assert.Equal([1L, 2L, 4L], leftover.Select(r => r.Window));
    }

    [Fact]
    public void LeftoverOfAnEmptyJournalIsEmpty() =>
        Assert.Empty(AppBarJournal.Leftover([], 10, _ => false));
}
