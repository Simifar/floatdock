using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class PinListTests
{
    private static PinnedApp Pin(string name) =>
        new(DockModel.PathKey($@"C:\Apps\{name}.exe"), name, new LaunchTarget(LaunchTargetKind.Executable, $@"C:\Apps\{name}.exe"));

    [Fact]
    public void InsertPlacesNewPinAndMovesExistingOne()
    {
        var pins = new[] { Pin("a"), Pin("b"), Pin("c") };
        Assert.Equal(["a", "x", "b", "c"], PinList.Insert(pins, Pin("x"), 1).Select(p => p.DisplayName));
        Assert.Equal(["a", "b", "c", "x"], PinList.Insert(pins, Pin("x"), 99).Select(p => p.DisplayName));
        // Moving "a" in front of "c" (index 2 in the original list).
        Assert.Equal(["b", "a", "c"], PinList.Insert(pins, Pin("a"), 2).Select(p => p.DisplayName));
        Assert.Equal(["c", "a", "b"], PinList.Insert(pins, Pin("c"), 0).Select(p => p.DisplayName));
        Assert.Equal(["a", "b", "c"], pins.Select(p => p.DisplayName));
    }

    [Fact]
    public void InsertStopsAtTheLimitButStillMoves()
    {
        var full = Enumerable.Range(0, PinList.MaxPins).Select(i => Pin(i.ToString())).ToList();
        Assert.Equal(PinList.MaxPins, PinList.Insert(full, Pin("extra"), 0).Count);
        Assert.Equal("5", PinList.Insert(full, Pin("5"), 0)[0].DisplayName);
    }

    [Fact]
    public void RemoveAndIndexUseKeysCaseInsensitively()
    {
        var pins = new[] { Pin("a"), Pin("b") };
        Assert.Equal(1, PinList.IndexOf(pins, pins[1].Key.ToLowerInvariant()));
        Assert.Equal(["b"], PinList.Remove(pins, pins[0].Key.ToLowerInvariant()).Select(p => p.DisplayName));
    }

    [Fact]
    public void KeysMatchRunningWindowGroups()
    {
        Assert.Equal(DockModel.AppIdKey("Vendor.App"), PinList.KeyFor(new LaunchTarget(LaunchTargetKind.AppUserModelId, "Vendor.App")));
        Assert.Equal(DockModel.PathKey(@"C:\Apps\app.exe"),
            PinList.KeyFor(new LaunchTarget(LaunchTargetKind.Shortcut, @"C:\Links\app.lnk"), @"C:\Apps\app.exe"));
    }

    [Fact]
    public void AppIdPinWithExecutableOwnsWindowsThatDoNotCarryTheId()
    {
        var explorer = @"C:\Windows\explorer.exe";
        var pin = new PinnedApp(DockModel.AppIdKey("Microsoft.Windows.Explorer"), "Проводник",
            new LaunchTarget(LaunchTargetKind.Shortcut, @"C:\Pins\File Explorer.lnk"), explorer);
        var window = new WindowInfo(1, 7, "Downloads", "CabinetWClass", explorer, true, false, false, false, false, true, false);
        var groups = DockModel.Build([window], [pin]);
        Assert.Single(groups);
        Assert.True(groups[0].IsRunning);
    }

    [Fact]
    public void FoldersAndFilesBecomeOpenTargets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatDockTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var note = Path.Combine(directory, "note.txt");
            File.WriteAllText(note, string.Empty);
            Assert.Equal(new LaunchTarget(LaunchTargetKind.Folder, directory), LaunchTarget.FromFile(directory + Path.DirectorySeparatorChar));
            Assert.Equal(LaunchTargetKind.File, LaunchTarget.FromFile(note)?.Kind);
            Assert.True(new LaunchTarget(LaunchTargetKind.Folder, directory).IsValid());
            Assert.False(new LaunchTarget(LaunchTargetKind.File, "relative.txt").IsValid());
        }
        finally { Directory.Delete(directory, true); }
    }
}
