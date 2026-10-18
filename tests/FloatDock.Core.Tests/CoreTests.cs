using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class CoreTests
{
    private static WindowInfo Window(nint handle, string path, bool foreground = false) =>
        new(handle, 42, "Document", "EditorWindow", path, true, false, false, false, false, true, foreground);

    [Fact]
    public void EligibilityRejectsToolOwnedCloakedAndHelperWindows()
    {
        var valid = Window(1, @"C:\Apps\Editor.exe");
        Assert.True(WindowEligibility.IsEligible(valid));
        Assert.False(WindowEligibility.IsEligible(valid with { IsToolWindow = true }));
        Assert.False(WindowEligibility.IsEligible(valid with { HasOwner = true }));
        Assert.False(WindowEligibility.IsEligible(valid with { IsCloaked = true }));
        Assert.False(WindowEligibility.IsEligible(valid with { HasArea = false }));
        Assert.True(WindowEligibility.IsEligible(valid with { HasOwner = true, IsAppWindow = true }));
        Assert.False(WindowEligibility.IsEligible(valid with { IsNoActivate = true }));
        Assert.False(WindowEligibility.IsEligible(valid with { ClassName = "Shell_TrayWnd" }));
    }

    [Fact]
    public void GroupsMultipleWindowsByExecutableAndPreservesPinnedOrder()
    {
        var path = @"C:\Apps\Editor.exe";
        var key = Path.GetFullPath(path).ToUpperInvariant();
        var pin = new PinnedApp(key, "My Editor", new LaunchTarget(LaunchTargetKind.Executable, path));
        var groups = DockModel.Build([Window(1, path), Window(2, path, true)], [pin]);
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Windows.Count);
        Assert.True(groups[0].IsPinned);
        Assert.True(groups[0].IsActive);
        Assert.Equal("My Editor", groups[0].DisplayName);
    }

    [Fact]
    public void HostedWindowsRemainSeparateUntilPackagedIdentityIsAvailable()
    {
        var host = @"C:\Windows\System32\ApplicationFrameHost.exe";
        var groups = DockModel.Build([Window(1, host), Window(2, host)], []);
        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Null(group.LaunchTarget));
    }

    [Fact]
    public void ReorderingMovesOnlyTheSelectedPin()
    {
        var pins = Enumerable.Range(0, 3).Select(i => new PinnedApp(i.ToString(), i.ToString(),
            new LaunchTarget(LaunchTargetKind.Executable, $@"C:\Apps\{i}.exe"))).ToArray();
        Assert.Equal(["1", "2", "0"], DockModel.Move(pins, 0, 2).Select(p => p.Key));
        Assert.Equal(["0", "1", "2"], pins.Select(p => p.Key));
    }

    [Fact]
    public void SettingsClampUnsafeValuesAndDeduplicatePins()
    {
        var pin = new PinnedApp("app", "App", new LaunchTarget(LaunchTargetKind.Executable, @"C:\Apps\app.exe"));
        var settings = new DockSettings { IconSize = 900, Magnification = double.NaN, Spacing = -5, Pins = [pin, pin] }.Validate();
        Assert.Equal(96, settings.IconSize);
        Assert.Equal(1.45, settings.Magnification);
        Assert.Equal(0, settings.Spacing);
        Assert.Single(settings.Pins);
        Assert.Single(new DockSettings
        {
            Pins = [new PinnedApp("packaged", "Packaged", new LaunchTarget(LaunchTargetKind.AppUserModelId, "Example.App_8wekyb3d8bbwe!App"))]
        }.Validate().Pins);
        Assert.Empty(new DockSettings
        {
            Pins = [new PinnedApp("escape", "Escape", new LaunchTarget(LaunchTargetKind.AppUserModelId, @"..\..\Windows\System32\cmd.exe"))]
        }.Validate().Pins);
    }

    [Fact]
    public async Task SettingsSurviveRestartAndCorruptJsonIsBackedUp()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatDockTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            await store.SaveAsync(new DockSettings { IconSize = 68 });
            Assert.Equal(68, (await new SettingsStore(directory).LoadAsync()).IconSize);
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{broken");
            Assert.Equal(52, (await store.LoadAsync()).IconSize);
            Assert.Single(Directory.GetFiles(directory, "settings.json.corrupt-*"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SettingsIgnoreNullAndInvalidPinData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatDockTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"),
                "{\"Pins\":[null,{\"Key\":\"bad\",\"Target\":null},{\"Key\":\"bad-path\",\"Target\":{\"Kind\":0,\"Value\":\"bad\\u0000.exe\"}}]}");
            Assert.Empty((await new SettingsStore(directory).LoadAsync()).Pins);
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{\"Pins\":null}");
            Assert.Empty((await new SettingsStore(directory).LoadAsync()).Pins);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void LaunchTargetAcceptsOnlyExistingPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatDockTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var exe = Path.Combine(directory, "app.exe");
            var text = Path.Combine(directory, "notes.txt");
            File.WriteAllBytes(exe, []);
            File.WriteAllBytes(text, []);
            Assert.Equal(LaunchTargetKind.Executable, LaunchTarget.FromFile(exe)?.Kind);
            Assert.Equal(LaunchTargetKind.File, LaunchTarget.FromFile(text)?.Kind);
            Assert.Null(LaunchTarget.FromFile(Path.Combine(directory, "missing.exe")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FullscreenRequiresCoveringTheSelectedMonitor()
    {
        var secondary = new ScreenRect(-1920, 0, 1920, 1080);
        Assert.True(FullscreenGeometry.Covers(new ScreenRect(-1920, 0, 1920, 1080), secondary));
        Assert.False(FullscreenGeometry.Covers(new ScreenRect(0, 0, 1920, 1080), secondary));
        Assert.False(FullscreenGeometry.Covers(new ScreenRect(-1900, 0, 1880, 1080), secondary));
        Assert.False(FullscreenGeometry.IsUserFullscreen(secondary, secondary, "Progman"));
        Assert.True(FullscreenGeometry.IsUserFullscreen(secondary, secondary, "Chrome_WidgetWin_1"));
    }

    [Fact]
    public async Task LegacySettingsMigrateWithoutEnablingTaskbarReplacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatDockTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"),
                "{\"SchemaVersion\":0,\"IconSize\":68,\"ReplaceTaskbar\":true}");
            var settings = await new SettingsStore(directory).LoadAsync();
            Assert.Equal(1, settings.SchemaVersion);
            Assert.Equal(68, settings.IconSize);
            Assert.False(settings.ReplaceTaskbar);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ThreeRapidStartupAttemptsRequireSafeMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatDockTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var guard = new StartupGuard(directory);
            Assert.False(guard.Enter());
            Assert.False(guard.Enter());
            Assert.True(guard.Enter());
            guard.MarkHealthy();
            Assert.False(guard.Enter());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
