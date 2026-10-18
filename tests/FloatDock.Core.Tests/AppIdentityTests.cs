using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class AppIdentityTests
{
    private static WindowInfo Window(nint handle, string path, string? appId = null, string title = "Window") =>
        new(handle, 42, title, "AppWindow", path, true, false, false, false, false, true, false, appId);

    [Fact]
    public void WindowsWithAnAppIdGroupByItInsteadOfExecutable()
    {
        var chrome = @"C:\Apps\Chrome\chrome.exe";
        var groups = DockModel.Build([Window(1, chrome, "Chrome"), Window(2, chrome, "Chrome"), Window(3, chrome, "Chrome.PWA.Mail")], []);
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Key == DockModel.AppIdKey("Chrome") && g.Windows.Count == 2);
        // The window-supplied ID only groups; without a Start menu entry the app launches by its executable.
        Assert.All(groups, g => Assert.Equal(new LaunchTarget(LaunchTargetKind.Executable, chrome), g.LaunchTarget));
    }

    [Fact]
    public void StartMenuEntryProvidesNameAndLaunchTarget()
    {
        var entry = new AppEntry("Chrome", "Google Chrome", @"C:\Apps\Chrome\chrome.exe", false);
        var groups = DockModel.Build([Window(1, @"C:\Apps\Chrome\chrome.exe", "Chrome")], [],
            (appId, _) => appId == "Chrome" ? entry : null);
        Assert.Equal("Google Chrome", groups[0].DisplayName);
        Assert.Equal(entry.Target, groups[0].LaunchTarget);
    }

    [Fact]
    public void LaunchTargetsAcceptOnlyAppIdsOrStartMenuItems()
    {
        Assert.True(LaunchTarget.IsAppUserModelId("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
        Assert.True(LaunchTarget.IsAppUserModelId("308046B0AF4A39CB;PrivateBrowsingAUMID"));
        Assert.False(LaunchTarget.IsAppUserModelId(@"C:\Windows\System32\cmd.exe"));
        Assert.False(LaunchTarget.IsAppUserModelId(@"..\cmd.exe"));
        Assert.False(LaunchTarget.IsAppUserModelId("a/b"));
        Assert.True(LaunchTarget.IsKnownFolderItem(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Vendor\app.exe"));
        Assert.False(LaunchTarget.IsKnownFolderItem(@"{6D809377-6AF0-444B-8957-A3773F02200E}\..\..\cmd.exe"));
        Assert.False(LaunchTarget.IsKnownFolderItem(@"{6D809377-6AF0-444B-8957-A3773F02200E}\C:\cmd.exe"));
        Assert.False(new LaunchTarget(LaunchTargetKind.AppUserModelId, @"\\server\share\x.exe").IsValid());
    }

    [Fact]
    public void HostedWindowsGroupByTheirPackagedAppId()
    {
        var host = @"C:\Windows\System32\ApplicationFrameHost.exe";
        var groups = DockModel.Build([Window(1, host, "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            Window(2, host, "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")], []);
        Assert.Single(groups);
        Assert.Null(groups[0].ExecutablePath);
    }

    [Fact]
    public void PathPinOwnsTheMainWindowsOfAnExecutableWithAnAppId()
    {
        var chrome = @"C:\Apps\Chrome\chrome.exe";
        var pin = new PinnedApp(DockModel.PathKey(chrome), "Chrome", new LaunchTarget(LaunchTargetKind.Executable, chrome));
        var groups = DockModel.Build([Window(1, chrome, "Chrome"), Window(2, chrome, "Chrome"), Window(3, chrome, "Chrome.PWA.Mail")], [pin]);
        Assert.Equal(2, groups.Count);
        Assert.True(groups[0].IsPinned);
        Assert.Equal(2, groups[0].Windows.Count);
        Assert.False(groups[1].IsPinned);
    }

    [Fact]
    public void AppIdPinMatchesRunningWindowsAndCatalogNamesAreUsed()
    {
        const string calculator = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        var pin = new PinnedApp(DockModel.AppIdKey(calculator), "Калькулятор", new LaunchTarget(LaunchTargetKind.AppUserModelId, calculator));
        var host = @"C:\Windows\System32\ApplicationFrameHost.exe";
        var groups = DockModel.Build([Window(1, host, calculator), Window(2, @"C:\Apps\tool.exe", "Vendor.Tool")], [pin],
            (appId, _) => appId == "Vendor.Tool" ? new AppEntry("Vendor.Tool", "Vendor Tool", null, false) : null);
        Assert.Single(groups[0].Windows);
        Assert.Equal("Vendor Tool", groups[1].DisplayName);
    }

    [Fact]
    public void FlashingWindowMakesItsGroupNeedAttention()
    {
        var groups = DockModel.Build([Window(1, @"C:\Apps\chat.exe") with { IsFlashing = true }], []);
        Assert.True(groups[0].NeedsAttention);
    }

    [Fact]
    public void SearchRanksPrefixWordAndSubsequenceAndHandlesWrongLayout()
    {
        AppEntry App(string name) => new(name, name, null, true);
        var apps = new[] { App("Visual Studio Code"), App("Calculator"), App("Paint"), App("Калькулятор"), App("Steam") };
        Assert.Equal("Calculator", AppSearch.Search(apps, "calc")[0].Name);
        Assert.Equal("Visual Studio Code", AppSearch.Search(apps, "code")[0].Name);
        Assert.Equal("Visual Studio Code", AppSearch.Search(apps, "vsc")[0].Name);
        Assert.Equal("Калькулятор", AppSearch.Search(apps, "rfkm")[0].Name);
        Assert.Equal("Steam", AppSearch.Search(apps, "ыеуфь")[0].Name);
        Assert.Empty(AppSearch.Search(apps, "zzz"));
        Assert.Equal(5, AppSearch.Search(apps, " ").Count);
    }

    [Fact]
    public void SearchTiesPreferMoreUsedApps()
    {
        var apps = new[] { new AppEntry("a", "Terminal Preview", null, true), new AppEntry("b", "Terminal", null, true) };
        Assert.Equal("Terminal", AppSearch.Search(apps, "term")[0].Name);
        var usage = new Dictionary<string, int> { ["a"] = 5 };
        Assert.Equal("Terminal Preview", AppSearch.Search(apps, "term", usage)[0].Name);
    }

    [Fact]
    public void CatalogEntriesMapToTheGroupKeysOfTheirWindows()
    {
        var packaged = new AppEntry("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Terminal", null, true);
        Assert.Equal(DockModel.AppIdKey(packaged.Id), packaged.Key);
        Assert.Equal(LaunchTargetKind.AppUserModelId, packaged.Target.Kind);
        var desktop = new AppEntry(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Vendor\app.exe", "App", @"C:\Program Files\Vendor\app.exe", false);
        Assert.Equal(DockModel.PathKey(@"C:\Program Files\Vendor\app.exe"), desktop.Key);
        var plain = new AppEntry(@"D:\Tools\tool.exe", "Tool", @"D:\Tools\tool.exe", false);
        Assert.Equal(LaunchTargetKind.Executable, plain.Target.Kind);
    }
}
