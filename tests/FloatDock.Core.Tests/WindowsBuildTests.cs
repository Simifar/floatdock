using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class WindowsBuildTests
{
    [Theory]
    [InlineData(17763, false)]   // Windows 10 1809
    [InlineData(19045, false)]   // Windows 10 22H2
    [InlineData(21999, false)]
    [InlineData(22000, true)]    // Windows 11 21H2
    [InlineData(26300, true)]
    public void Windows11StartsAtBuild22000(int build, bool expected) => Assert.Equal(expected, WindowsBuild.IsWindows11(build));

    [Fact]
    public void WindowsTenHasOnlyTheActionCenterShortcut()
    {
        Assert.Equal('A', WindowsBuild.NotificationsKey(19045));
        Assert.Equal('N', WindowsBuild.NotificationsKey(22631));
    }
}
