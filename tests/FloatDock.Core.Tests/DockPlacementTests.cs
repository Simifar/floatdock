using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class DockPlacementTests
{
    [Fact]
    public void CentersAboveTheBottomMargin()
    {
        var rect = DockPlacement.Compute(new ScreenRect(0, 0, 1920, 1032), 1032, 700, 110, 1, hidden: false);
        Assert.Equal(new ScreenRect(610, 910, 700, 110), rect);
    }

    [Fact]
    public void WorksOnDisplaysLeftOfAndAboveThePrimaryOne()
    {
        // A second display to the left has negative coordinates; one above has a negative Y.
        var left = DockPlacement.Compute(new ScreenRect(-1920, 0, 1920, 1040), 1040, 600, 110, 1, hidden: false);
        Assert.Equal(-1920 + 660, left.X);
        Assert.Equal(1040 - 110 - 12, left.Y);
        var above = DockPlacement.Compute(new ScreenRect(0, -1080, 1920, 1080), 0, 600, 110, 1, hidden: false);
        Assert.Equal(-110 - 12, above.Y);
    }

    [Theory]
    [InlineData(1.0, 12)]
    [InlineData(1.5, 18)]
    [InlineData(2.0, 24)]
    public void MarginScalesWithTheDisplay(double scale, int margin)
    {
        var rect = DockPlacement.Compute(new ScreenRect(0, 0, 3840, 2160), 2160, 800, 200, scale, hidden: false);
        Assert.Equal(2160 - 200 - margin, rect.Y);
    }

    [Fact]
    public void HiddenLeavesAThinStripVisible()
    {
        var shown = DockPlacement.Compute(new ScreenRect(0, 0, 1920, 1080), 1080, 700, 110, 2, hidden: false);
        var hidden = DockPlacement.Compute(new ScreenRect(0, 0, 1920, 1080), 1080, 700, 110, 2, hidden: true);
        Assert.Equal(110 - 8, hidden.Y - shown.Y);
        Assert.Equal(shown.X, hidden.X);
    }

    [Fact]
    public void ADockWiderThanTheDisplayIsCutToIt()
    {
        var rect = DockPlacement.Compute(new ScreenRect(1920, 0, 1280, 1000), 1000, 2000, 110, 1, hidden: false);
        Assert.Equal(new ScreenRect(1920, 878, 1280, 110), rect);
    }
}
