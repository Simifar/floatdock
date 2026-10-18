using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class LauncherTests
{
    private static AppEntry App(string id, string name, string? path = null, bool packaged = false) => new(id, name, path, packaged);

    [Theory]
    [InlineData("Ctrl+Alt+Space", HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x20u)]
    [InlineData("alt + f1", HotKeyModifiers.Alt, 0x70u)]
    [InlineData("Win+Shift+A", HotKeyModifiers.Win | HotKeyModifiers.Shift, 0x41u)]
    [InlineData("Control+9", HotKeyModifiers.Control, 0x39u)]
    public void HotKeyParses(string text, HotKeyModifiers modifiers, uint key)
    {
        Assert.True(HotKeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Space")]
    [InlineData("Shift+A")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Nonsense")]
    public void HotKeyRejectsUnusableText(string? text) => Assert.False(HotKeyGesture.TryParse(text, out _));

    [Theory]
    [InlineData("Ctrl+Alt+Space")]
    [InlineData("Ctrl+Shift+F12")]
    [InlineData("Alt+Win+Q")]
    public void HotKeyFormatsWhatItParses(string text)
    {
        Assert.True(HotKeyGesture.TryParse(text, out var gesture));
        Assert.Equal(text, gesture.ToString());
    }

    [Fact]
    public void SettingsKeepAUsableHotKeyAndFixABrokenOne()
    {
        Assert.Equal(DockSettings.DefaultLauncherHotKey, new DockSettings().Validate().LauncherHotKey);
        Assert.Equal("Ctrl+Alt+L", (new DockSettings { LauncherHotKey = "control + alt + l" }).Validate().LauncherHotKey);
        Assert.Equal(DockSettings.DefaultLauncherHotKey, (new DockSettings { LauncherHotKey = "Space" }).Validate().LauncherHotKey);
        Assert.Equal(string.Empty, (new DockSettings { LauncherHotKey = " " }).Validate().LauncherHotKey);
    }

    [Fact]
    public void LaunchCountsSurviveARestartAndIgnoreDamage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "floatdock-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var counts = new LaunchCounts(directory);
            counts.Load();
            Assert.Empty(counts.Snapshot());
            counts.Increment("A");
            counts.Increment("a");
            counts.Increment("B");
            counts.Increment(" ");

            var reloaded = new LaunchCounts(directory);
            reloaded.Load();
            Assert.Equal(2, reloaded.Snapshot()["A"]);
            Assert.Equal(1, reloaded.Snapshot()["B"]);
            Assert.Equal(2, reloaded.Snapshot().Count);

            File.WriteAllText(Path.Combine(directory, "launch-counts.json"), "{ not json");
            var damaged = new LaunchCounts(directory);
            damaged.Load();
            Assert.Empty(damaged.Snapshot());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void FrequentListsUsedAppsMostUsedFirst()
    {
        var apps = new[] { App("a", "Alpha"), App("b", "Bravo"), App("c", "Charlie"), App("d", "Delta") };
        var counts = new Dictionary<string, int> { ["a"] = 2, ["c"] = 9, ["d"] = 2, ["gone"] = 5 };
        Assert.Equal(["Charlie", "Alpha", "Delta"], AppSearch.Frequent(apps, counts, 6).Select(a => a.Name));
        Assert.Equal(["Charlie"], AppSearch.Frequent(apps, counts, 1).Select(a => a.Name));
        Assert.Empty(AppSearch.Frequent(apps, new Dictionary<string, int>(), 6));
    }

    [Fact]
    public void CatalogAppBecomesAPinThatMatchesItsRunningWindows()
    {
        var classic = App(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Vendor\tool.exe", "Tool", @"C:\Vendor\tool.exe");
        var pin = classic.ToPin();
        Assert.Equal(DockModel.PathKey(@"C:\Vendor\tool.exe"), pin.Key);
        Assert.Equal("Tool", pin.DisplayName);
        Assert.Equal(classic.Target, pin.Target);
        Assert.Equal(@"C:\Vendor\tool.exe", pin.ExecutablePath);

        var packaged = App("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator", null, packaged: true).ToPin();
        Assert.Equal(DockModel.AppIdKey("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"), packaged.Key);
        Assert.Null(packaged.ExecutablePath);
        Assert.True(packaged.Target.IsValid());

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = App("Microsoft.Windows.Explorer", "File Explorer", Path.Combine(windows, "explorer.exe")).ToPin();
        Assert.Null(system.ExecutablePath);
    }
}
