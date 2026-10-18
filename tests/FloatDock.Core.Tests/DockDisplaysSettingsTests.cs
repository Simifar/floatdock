using System.Text.Json;
using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class DockDisplaysSettingsTests
{
    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "floatdock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void TheDefaultIsTheSingleDockOnThePrimaryDisplay() =>
        Assert.Equal(DockDisplays.Primary, new DockSettings().Displays);

    [Fact]
    public void AnUnknownValueFallsBackToThePrimaryDisplay() =>
        Assert.Equal(DockDisplays.Primary, (new DockSettings { Displays = (DockDisplays)42 }).Validate().Displays);

    [Fact]
    public async Task TheChoiceSurvivesARestartAndIsWrittenAsANumberLikeTheOtherEnums()
    {
        var directory = TempDirectory();
        try
        {
            await new SettingsStore(directory).SaveAsync(new DockSettings { Displays = DockDisplays.All });
            Assert.Equal(DockDisplays.All, (await new SettingsStore(directory).LoadAsync()).Displays);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "settings.json")));
            Assert.Equal(1, document.RootElement.GetProperty("Displays").GetInt32());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AFileWithoutTheFieldKeepsThePrimaryDisplayDefaultEvenWhenOtherSwitchesAreSet()
    {
        var directory = TempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), """{"SchemaVersion":1,"Mode":1,"ShowClock":true}""");
            var loaded = await new SettingsStore(directory).LoadAsync();
            Assert.Equal(DockDisplays.Primary, loaded.Displays);
            Assert.Equal(DockMode.Reserved, loaded.Mode);
            Assert.True(loaded.ShowClock);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
