using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class AppMenuOrderTests
{
    private static AppEntry App(string id, string name) => new(id, name, null, IsPackaged: true);

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "floatdock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static readonly AppEntry[] Apps = [App("c", "Calculator"), App("a", "Archive"), App("b", "Browser")];

    [Fact]
    public void AlphabeticalIsTheDefaultOrder() =>
        Assert.Equal(["Archive", "Browser", "Calculator"], AppSearch.Order(Apps, LauncherSort.Name).Select(app => app.Name));

    [Fact]
    public void MostUsedPutsTheMostLaunchedFirstAndTheAlphabetBreaksTies()
    {
        var counts = new Dictionary<string, int> { ["c"] = 5, ["b"] = 5 };
        Assert.Equal(["Browser", "Calculator", "Archive"], AppSearch.Order(Apps, LauncherSort.MostUsed, counts).Select(app => app.Name));
    }

    [Fact]
    public void NewestPutsTheLatestSeenFirstAndBaselineAppsLast()
    {
        var seen = new Dictionary<string, long> { ["a"] = 0, ["b"] = 2000, ["c"] = 1000 };
        Assert.Equal(["Browser", "Calculator", "Archive"], AppSearch.Order(Apps, LauncherSort.Newest, null, seen).Select(app => app.Name));
    }

    [Fact]
    public void ASearchIgnoresTheChosenOrderAndTheEmptyQueryUsesIt()
    {
        var counts = new Dictionary<string, int> { ["c"] = 9 };
        Assert.Equal("Calculator", AppSearch.Search(Apps, "", counts, LauncherSort.MostUsed)[0].Name);
        // "a" starts only Archive; the order option does not change which app is the best match.
        Assert.Equal("Archive", AppSearch.Search(Apps, "arc", counts, LauncherSort.MostUsed)[0].Name);
    }

    [Fact]
    public void TheFirstListIsTheBaselineAndLaterAppsAreNewForSevenDays()
    {
        var directory = TempDirectory();
        try
        {
            var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            var seen = new AppFirstSeen(directory);
            seen.Load();
            Assert.False(seen.Observe([], start));
            Assert.True(seen.Observe(["a", "b"], start));
            Assert.False(AppFirstSeen.IsNew(seen.Snapshot(), "a", start));
            Assert.True(seen.Observe(["a", "b", "c"], start.AddDays(1)));
            Assert.False(seen.Observe(["a", "b", "c"], start.AddDays(2)));
            var snapshot = seen.Snapshot();
            Assert.False(AppFirstSeen.IsNew(snapshot, "b", start.AddDays(2)));
            Assert.True(AppFirstSeen.IsNew(snapshot, "c", start.AddDays(2)));
            Assert.True(AppFirstSeen.IsNew(snapshot, "c", start.AddDays(7.9)));
            Assert.False(AppFirstSeen.IsNew(snapshot, "c", start.AddDays(8.1)));
            Assert.False(AppFirstSeen.IsNew(snapshot, "unknown", start));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void WhatWasSeenSurvivesARestartWithoutANewBaseline()
    {
        var directory = TempDirectory();
        try
        {
            var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            var first = new AppFirstSeen(directory);
            first.Load();
            first.Observe(["a"], start);
            var second = new AppFirstSeen(directory);
            second.Load();
            second.Observe(["a", "b"], start.AddHours(3));
            Assert.True(AppFirstSeen.IsNew(second.Snapshot(), "b", start.AddHours(4)));
            Assert.False(AppFirstSeen.IsNew(second.Snapshot(), "a", start.AddHours(4)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void HiddenAppsAreCleanedAndAnOldFileHasNone()
    {
        var settings = new DockSettings { HiddenApps = ["x", "X", " ", "", "y"], LauncherSort = (LauncherSort)99 }.Validate();
        Assert.Equal(["x", "y"], settings.HiddenApps);
        Assert.Equal(LauncherSort.Name, settings.LauncherSort);
        Assert.Empty(new DockSettings().HiddenApps);
    }

    [Fact]
    public async Task TheChoicesSurviveARestartAndAnOldFileGetsTheDefaults()
    {
        var directory = TempDirectory();
        try
        {
            await new SettingsStore(directory).SaveAsync(new DockSettings { LauncherSort = LauncherSort.Newest, HiddenApps = ["x"] });
            var loaded = await new SettingsStore(directory).LoadAsync();
            Assert.Equal(LauncherSort.Newest, loaded.LauncherSort);
            Assert.Equal(["x"], loaded.HiddenApps);
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), """{"SchemaVersion":1,"ShowClock":true}""");
            var old = await new SettingsStore(directory).LoadAsync();
            Assert.Equal(LauncherSort.Name, old.LauncherSort);
            Assert.Empty(old.HiddenApps);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
