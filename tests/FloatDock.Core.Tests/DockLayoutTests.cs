using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class DockLayoutTests
{
    [Fact]
    public void ScaleIsLargestUnderThePointerAndFadesWithDistance()
    {
        Assert.Equal(1.5, DockLayout.Scale(100, 100, 50, 1.5), 6);
        var near = DockLayout.Scale(100, 140, 50, 1.5);
        var far = DockLayout.Scale(100, 400, 50, 1.5);
        Assert.InRange(near, 1.01, 1.49);
        Assert.Equal(1, far, 3);
        Assert.Equal(1, DockLayout.Scale(100, 100, 50, 1), 6);
        Assert.Equal(1, DockLayout.Scale(100, 100, 50, 0.2), 6);
    }

    [Fact]
    public void NoMagnificationMeansNoShift() =>
        Assert.All(DockLayout.Offsets([60, 60, 60], [1, 1, 1]), offset => Assert.Equal(0, offset, 9));

    [Fact]
    public void EqualScalesSpreadTheRowSymmetrically()
    {
        var offsets = DockLayout.Offsets([60, 60, 60, 60], [1.5, 1.5, 1.5, 1.5]);
        // Each item gains 30; the row grows by 120 and stays centered.
        Assert.Equal(-45, offsets[0], 9);
        Assert.Equal(-15, offsets[1], 9);
        Assert.Equal(15, offsets[2], 9);
        Assert.Equal(45, offsets[3], 9);
    }

    [Fact]
    public void MagnifiedIconsKeepTheGapToTheirNeighbours()
    {
        double[] widths = [60, 60, 60, 60, 60];
        double[] scales = [1, 1.2, 1.6, 1.2, 1];
        var offsets = DockLayout.Offsets(widths, scales);
        var lefts = DockLayout.Lefts(widths, 8);
        for (var i = 0; i < widths.Length - 1; i++)
        {
            var right = lefts[i] + widths[i] / 2 + offsets[i] + widths[i] * scales[i] / 2;
            var nextLeft = lefts[i + 1] + widths[i + 1] / 2 + offsets[i + 1] - widths[i + 1] * scales[i + 1] / 2;
            Assert.Equal(8, nextLeft - right, 9);
        }
        // A symmetric curve leaves the middle item where it was.
        Assert.Equal(0, offsets[2], 9);
    }

    [Fact]
    public void LeftsAddTheGap() => Assert.Equal([0, 68, 136], DockLayout.Lefts([60, 60, 60], 8));
}
