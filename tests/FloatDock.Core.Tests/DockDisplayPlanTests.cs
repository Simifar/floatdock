using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class DockDisplayPlanTests
{
    private static DisplayInfo Display(long id, int x, int y, int width, int height, bool primary = false, int taskbar = 0) =>
        new(id, new ScreenRect(x, y, width, height), new ScreenRect(x, y, width, height - taskbar), primary);

    private static readonly DisplayInfo Main = Display(1, 0, 0, 1920, 1080, primary: true, taskbar: 48);
    private static readonly DisplayInfo Right = Display(2, 1920, 0, 2560, 1440, taskbar: 48);
    private static readonly DisplayInfo Left = Display(3, -1280, 120, 1280, 1024);

    [Fact]
    public void AllDisplaysGetADockExceptThePrimaryOneWhichAlwaysHasIts()
    {
        Assert.Equal([3L, 2L], DockDisplayPlan.Extras([Right, Main, Left], DockDisplays.All));
    }

    [Fact]
    public void PrimaryOnlyMeansNoExtraDocks()
    {
        Assert.Empty(DockDisplayPlan.Extras([Main, Right, Left], DockDisplays.Primary));
    }

    [Fact]
    public void ASingleDisplayNeverHasExtras()
    {
        Assert.Empty(DockDisplayPlan.Extras([Main], DockDisplays.All));
        Assert.Empty(DockDisplayPlan.Extras([], DockDisplays.All));
    }

    [Fact]
    public void ThePrimaryDisplayMayBeAnyOfThem()
    {
        var notPrimary = Main with { IsPrimary = false };
        var primaryOnTheRight = Right with { IsPrimary = true };
        Assert.Equal(primaryOnTheRight, DockDisplayPlan.Primary([notPrimary, primaryOnTheRight, Left]));
        Assert.Equal([3L, 1L], DockDisplayPlan.Extras([notPrimary, primaryOnTheRight, Left], DockDisplays.All));
    }

    [Fact]
    public void WithoutAPrimaryFlagTheDisplayAtTheOriginIsUsedThenTheFirst()
    {
        var unflagged = Main with { IsPrimary = false };
        Assert.Equal(unflagged, DockDisplayPlan.Primary([Right, unflagged]));
        Assert.Equal(Right, DockDisplayPlan.Primary([Right, Left]));
        Assert.Null(DockDisplayPlan.Primary([]));
    }

    [Fact]
    public void ReconcileCreatesMissingDocksAndClosesThoseWhoseDisplayIsGone()
    {
        var change = DockDisplayPlan.Reconcile([2, 9], [Main, Right, Left], DockDisplays.All);
        Assert.Equal([3L], change.Add);
        Assert.Equal([9L], change.Remove);
    }

    [Fact]
    public void ReconcileChangesNothingWhenTheDocksMatch()
    {
        var change = DockDisplayPlan.Reconcile([2, 3], [Main, Right, Left], DockDisplays.All);
        Assert.Empty(change.Add);
        Assert.Empty(change.Remove);
    }

    [Fact]
    public void SwitchingToPrimaryOnlyClosesEveryExtraDock()
    {
        var change = DockDisplayPlan.Reconcile([2, 3], [Main, Right, Left], DockDisplays.Primary);
        Assert.Empty(change.Add);
        Assert.Equal([2L, 3L], change.Remove);
    }

    [Fact]
    public void UnpluggingTheOnlyOtherDisplayClosesItsDock()
    {
        var change = DockDisplayPlan.Reconcile([2], [Main], DockDisplays.All);
        Assert.Empty(change.Add);
        Assert.Equal([2L], change.Remove);
    }

    [Fact]
    public void ChangingThePrimaryDisplayMovesTheExtraDock()
    {
        // The display that had the extra dock is now the primary one (which has the anchor dock); the old primary needs an extra.
        var swapped = new[] { Main with { IsPrimary = false }, Right with { IsPrimary = true } };
        var change = DockDisplayPlan.Reconcile([2], swapped, DockDisplays.All);
        Assert.Equal([1L], change.Add);
        Assert.Equal([2L], change.Remove);
    }

    [Fact]
    public void FindsTheDisplayUnderAPoint()
    {
        var displays = new[] { Main, Right, Left };
        Assert.Equal(1, DockDisplayPlan.At(displays, 10, 10)!.Id);
        Assert.Equal(2, DockDisplayPlan.At(displays, 1920, 0)!.Id);
        Assert.Equal(3, DockDisplayPlan.At(displays, -1, 200)!.Id);
        Assert.Null(DockDisplayPlan.At(displays, -1, 5));
        Assert.Null(DockDisplayPlan.At(displays, 1920 + 2560, 10));
    }

    [Fact]
    public void EachDockIsPlacedByItsOwnDisplay()
    {
        // The same dock size on three differently arranged displays: centred on each one's work area, above its own bottom.
        var main = DockPlacement.Compute(Main.WorkArea, Main.WorkArea.Bottom, 800, 110, 1, hidden: false);
        var right = DockPlacement.Compute(Right.WorkArea, Right.WorkArea.Bottom, 800, 110, 1.5, hidden: false);
        var left = DockPlacement.Compute(Left.WorkArea, Left.WorkArea.Bottom, 800, 110, 1, hidden: false);
        Assert.Equal(new ScreenRect(560, 1032 - 110 - 12, 800, 110), main);
        Assert.Equal(new ScreenRect(1920 + 880, 1392 - 110 - 18, 800, 110), right);
        Assert.Equal(new ScreenRect(-1280 + 240, 120 + 1024 - 110 - 12, 800, 110), left);
        Assert.True(main.Right <= Right.Bounds.X && right.X >= Right.Bounds.X && left.Right <= 0);
    }
}
