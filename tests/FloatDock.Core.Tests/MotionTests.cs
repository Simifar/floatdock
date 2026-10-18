using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class MotionTests
{
    [Fact]
    public void FollowCoversAboutTwoThirdsInOneTimeConstantAndIsFrameRateIndependent()
    {
        Assert.Equal(1 - Math.Exp(-1), Motion.Follow(0.03, 0.03), 9);
        // Two half steps leave the same distance as one whole step.
        var half = Motion.Follow(0.015, 0.03);
        Assert.Equal(1 - Motion.Follow(0.03, 0.03), (1 - half) * (1 - half), 9);
        Assert.Equal(0, Motion.Follow(0, 0.03), 9);
        Assert.Equal(0, Motion.Follow(-1, 0.03), 9);
        Assert.Equal(1, Motion.Follow(0.016, 0), 9);
    }

    [Fact]
    public void ApproachSnapsToTheTargetWithinEpsilon()
    {
        Assert.Equal(1.5, Motion.Approach(1.4, 1.5, 0.5, 0.1), 9);
        Assert.Equal(1.45, Motion.Approach(1.4, 1.5, 0.5, 0.01), 9);
        Assert.Equal(2, Motion.Approach(2, 2, 0.3, 0.001), 9);
    }

    [Fact]
    public void ProgressIsClampedAndOutCubicIsMonotonic()
    {
        Assert.Equal(0, Motion.Progress(-1, 0.2), 9);
        Assert.Equal(0.5, Motion.Progress(0.1, 0.2), 9);
        Assert.Equal(1, Motion.Progress(5, 0.2), 9);
        Assert.Equal(1, Motion.Progress(0, 0), 9);
        Assert.Equal(0, Motion.OutCubic(0), 9);
        Assert.Equal(1, Motion.OutCubic(1), 9);
        Assert.Equal(0.875, Motion.OutCubic(0.5), 9);
        var previous = 0.0;
        for (var i = 1; i <= 20; i++)
        {
            var value = Motion.OutCubic(i / 20.0);
            Assert.True(value > previous);
            previous = value;
        }
        Assert.Equal(1, Motion.OutCubic(3), 9);
    }

    [Fact]
    public void LerpBlendsBetweenTheEnds() => Assert.Equal(15, Motion.Lerp(10, 20, 0.5), 9);

    [Fact]
    public void PacerSkipsFramesThatComeTooSoonWithoutLosingTheirTime()
    {
        var pacer = new FramePacer(0.006, 0.05);
        pacer.Reset(10);
        // 200 Hz source: every second callback is skipped, and the step that does run covers both intervals.
        Assert.False(pacer.TryStep(10.005, out var skipped));
        Assert.Equal(0, skipped);
        Assert.True(pacer.TryStep(10.010, out var seconds));
        Assert.Equal(0.010, seconds, 9);
        Assert.False(pacer.TryStep(10.015, out _));
        Assert.True(pacer.TryStep(10.020, out seconds));
        Assert.Equal(0.010, seconds, 9);
    }

    [Fact]
    public void PacerCutsOneLongGapAndPassesEveryFrameOfASlowSource()
    {
        var pacer = new FramePacer(0.006, 0.05);
        pacer.Reset(0);
        Assert.True(pacer.TryStep(2, out var seconds));
        Assert.Equal(0.05, seconds, 9);
        // 60 Hz: all frames pass.
        for (var i = 1; i <= 5; i++)
        {
            Assert.True(pacer.TryStep(2 + i / 60.0, out seconds));
            Assert.Equal(1 / 60.0, seconds, 9);
        }
    }
}
