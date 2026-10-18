namespace FloatDock.Core;

/// <summary>One display as the dock sees it: a stable id, its whole area, the part the taskbar leaves free, and whether it is the primary one.</summary>
public sealed record DisplayInfo(long Id, ScreenRect Bounds, ScreenRect WorkArea, bool IsPrimary);

/// <summary>Which docks have to exist after the displays changed: extra docks to create and extra docks to close.</summary>
public sealed record DockDisplayChange(IReadOnlyList<long> Add, IReadOnlyList<long> Remove);

/// <summary>
/// Which displays carry a dock. The dock on the primary display always exists (it owns the app's shared shell hooks);
/// <see cref="Extras"/> are the docks for the other displays when the setting asks for all of them. Pure, so every
/// arrangement of displays is tested without a second monitor.
/// </summary>
public static class DockDisplayPlan
{
    /// <summary>The display flagged primary; failing that the one at the origin, then the first.</summary>
    public static DisplayInfo? Primary(IReadOnlyList<DisplayInfo> displays) =>
        displays.FirstOrDefault(d => d.IsPrimary)
        ?? displays.FirstOrDefault(d => d.Bounds.X == 0 && d.Bounds.Y == 0)
        ?? displays.FirstOrDefault();

    /// <summary>Ids of the displays that get a dock besides the primary one: none for <see cref="DockDisplays.Primary"/>, else all others, left to right.</summary>
    public static IReadOnlyList<long> Extras(IReadOnlyList<DisplayInfo> displays, DockDisplays mode)
    {
        if (mode != DockDisplays.All || Primary(displays) is not { } primary) return [];
        return displays.Where(d => d.Id != primary.Id).DistinctBy(d => d.Id)
            .OrderBy(d => d.Bounds.X).ThenBy(d => d.Bounds.Y).Select(d => d.Id).ToList();
    }

    /// <summary>What to create and what to close so that exactly the wanted extra docks exist.</summary>
    public static DockDisplayChange Reconcile(IReadOnlyCollection<long> existing, IReadOnlyList<DisplayInfo> displays, DockDisplays mode)
    {
        var wanted = Extras(displays, mode);
        return new DockDisplayChange(
            wanted.Where(id => !existing.Contains(id)).ToList(),
            existing.Where(id => !wanted.Contains(id)).ToList());
    }

    /// <summary>The display that contains a point, for example the pointer; null when it is outside every display.</summary>
    public static DisplayInfo? At(IReadOnlyList<DisplayInfo> displays, int x, int y) =>
        displays.FirstOrDefault(d => d.Bounds.Contains(x, y));
}
