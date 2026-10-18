using System.Text.Json;
using FloatDock.Core;

namespace FloatDock.Core.Tests;

/// <summary>The files written before JSON moved to source generation must still load, and the new output must keep their shape.</summary>
public sealed class JsonCompatibilityTests
{
    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "floatdock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task SettingsFromAnEarlierVersionLoadAndSaveWithTheSameShape()
    {
        var directory = TempDirectory();
        try
        {
            const string legacy = """
                {
                  "SchemaVersion": 1, "Theme": 2, "Language": 1, "Mode": 1, "IconSize": 44, "Magnification": 1.2, "Spacing": 8,
                  "HideInFullscreen": true, "ReduceMotion": false, "ReplaceTaskbar": true, "StartWithWindows": false, "FirstRunCompleted": true,
                  "Pins": [ { "Key": "AUMID:X", "DisplayName": "X", "Target": { "Kind": 2, "Value": "Vendor.X_8wekyb3d8bbwe!App" } } ]
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), legacy);
            var store = new SettingsStore(directory);
            var loaded = await store.LoadAsync();
            Assert.Equal(DockTheme.Dark, loaded.Theme);
            Assert.Equal(44, loaded.IconSize);
            Assert.True(loaded.ReplaceTaskbar);
            Assert.Equal("Vendor.X_8wekyb3d8bbwe!App", Assert.Single(loaded.Pins).Target.Value);
            Assert.True(loaded.ShowRecycleBin);
            Assert.Equal(DockDisplays.Primary, loaded.Displays);

            await store.SaveAsync(loaded);
            var written = await File.ReadAllTextAsync(Path.Combine(directory, "settings.json"));
            using var document = JsonDocument.Parse(written);
            Assert.Equal(2, document.RootElement.GetProperty("Theme").GetInt32());
            Assert.Equal(2, document.RootElement.GetProperty("Pins")[0].GetProperty("Target").GetProperty("Kind").GetInt32());
            Assert.Contains("\n", written);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void JournalsKeepTheirFormat()
    {
        var directory = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "shell-state.json"), """{"OriginalAppBarState":1,"ProcessId":42,"HiddenAt":"2026-10-16T17:00:00+00:00"}""");
            var state = new ShellStateJournal(directory).Read();
            Assert.Equal(new ShellState(1, 42, DateTimeOffset.Parse("2026-10-16T17:00:00+00:00")), state);

            File.WriteAllText(Path.Combine(directory, "appbar.json"), """{"Window":65662,"ProcessId":7}""");
            Assert.Equal(new AppBarRegistration(65662, 7), Assert.Single(new AppBarJournal(directory).Read()));

            var journal = new ShellStateJournal(directory);
            journal.Write(new ShellState(3, 9, DateTimeOffset.UnixEpoch));
            Assert.Equal(9, journal.Read()!.ProcessId);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void LogLinesKeepTheirPropertyNames()
    {
        var line = JsonSerializer.Serialize(new LogEntry(DateTimeOffset.UnixEpoch, "Info", "something", "IOException", -5, 123), StateJson.Default.LogEntry);
        using var document = JsonDocument.Parse(line);
        Assert.Equal("Info", document.RootElement.GetProperty("level").GetString());
        Assert.Equal("something", document.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("IOException", document.RootElement.GetProperty("errorType").GetString());
        Assert.Equal(-5, document.RootElement.GetProperty("hresult").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("time", out _));
        Assert.Equal(123, document.RootElement.GetProperty("pid").GetInt32());
    }

    [Fact]
    public void TheAppCatalogCacheFromAnEarlierVersionStillLoads()
    {
        const string cache = """[{"Id":"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App","Name":"Калькулятор","ExecutablePath":null,"IsPackaged":true,"Target":{"Kind":2,"Value":"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"},"Key":"AUMID:MICROSOFT.WINDOWSCALCULATOR_8WEKYB3D8BBWE!APP"}]""";
        var apps = JsonSerializer.Deserialize(cache, StateJson.Default.ListAppEntry);
        Assert.Equal("Калькулятор", Assert.Single(apps!).Name);
        Assert.True(apps![0].IsPackaged);
    }

    [Fact]
    public void StartupAttemptsAndLaunchCountsRoundTrip()
    {
        var attempts = new List<DateTimeOffset> { DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1) };
        var text = JsonSerializer.Serialize(attempts, StateJson.Default.ListDateTimeOffset);
        Assert.Equal(attempts, JsonSerializer.Deserialize(text, StateJson.Default.ListDateTimeOffset));
        var counts = JsonSerializer.Deserialize("""{"a":2}""", StateJson.Default.DictionaryStringInt32);
        Assert.Equal(2, counts!["a"]);
    }
}
