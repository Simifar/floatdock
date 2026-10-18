using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class ShelfLookSettingsTests
{
    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "floatdock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void TheDefaultsKeepTheLookOfEarlierVersions()
    {
        var settings = new DockSettings();
        Assert.Equal(88, settings.ShelfOpacity);
        Assert.Equal(22, settings.ShelfRadius);
        Assert.Equal(8, settings.Spacing);
        // 88 % of 255 is the alpha 224 the shelf had (225 before the setting existed).
        Assert.Equal(224, (int)Math.Round(255 * settings.ShelfOpacity / 100.0));
    }

    [Fact]
    public void ValuesAreClampedToWhatTheShelfCanShow()
    {
        var low = new DockSettings { ShelfOpacity = 0, ShelfRadius = -5, Spacing = -1 }.Validate();
        Assert.Equal(30, low.ShelfOpacity);
        Assert.Equal(0, low.ShelfRadius);
        Assert.Equal(0, low.Spacing);
        var high = new DockSettings { ShelfOpacity = 500, ShelfRadius = 99, Spacing = 99 }.Validate();
        Assert.Equal(100, high.ShelfOpacity);
        Assert.Equal(32, high.ShelfRadius);
        Assert.Equal(32, high.Spacing);
    }

    [Fact]
    public async Task AFileFromAnEarlierVersionGetsTheDefaultsEvenWhenOtherSwitchesAreSet()
    {
        var directory = TempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), """{"SchemaVersion":1,"Spacing":12,"ShowClock":true}""");
            var loaded = await new SettingsStore(directory).LoadAsync();
            Assert.Equal(88, loaded.ShelfOpacity);
            Assert.Equal(22, loaded.ShelfRadius);
            Assert.Equal(12, loaded.Spacing);
            Assert.True(loaded.ShowClock);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task TheChoicesSurviveARestart()
    {
        var directory = TempDirectory();
        try
        {
            await new SettingsStore(directory).SaveAsync(new DockSettings { ShelfOpacity = 60, ShelfRadius = 10, Spacing = 4 });
            var loaded = await new SettingsStore(directory).LoadAsync();
            Assert.Equal((60, 10, 4), (loaded.ShelfOpacity, loaded.ShelfRadius, loaded.Spacing));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
