namespace FloatDock.Core;

/// <summary>Pure edits of the pinned list; each returns a new list and keeps keys unique.</summary>
public static class PinList
{
    public const int MaxPins = 64;

    /// <summary>The key a pin of this target uses, matching the group key of its running windows.</summary>
    public static string KeyFor(LaunchTarget target, string? resolvedExecutable = null) => target.Kind switch
    {
        LaunchTargetKind.AppUserModelId => DockModel.AppIdKey(target.Value),
        LaunchTargetKind.Shortcut => DockModel.PathKey(resolvedExecutable ?? target.Value),
        _ => DockModel.PathKey(target.Value)
    };

    /// <summary>Inserts at <paramref name="index"/> (clamped; past the end appends). An existing pin with the key moves there.</summary>
    public static List<PinnedApp> Insert(IReadOnlyList<PinnedApp> pins, PinnedApp pin, int index)
    {
        var result = pins.ToList();
        var existing = result.FindIndex(p => p.Key.Equals(pin.Key, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            result.RemoveAt(existing);
            if (existing < index) index--;
        }
        else if (result.Count >= MaxPins) return result;
        result.Insert(Math.Clamp(index, 0, result.Count), pin);
        return result;
    }

    public static List<PinnedApp> Remove(IReadOnlyList<PinnedApp> pins, string key) =>
        pins.Where(p => !p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();

    public static int IndexOf(IReadOnlyList<PinnedApp> pins, string key)
    {
        for (var i = 0; i < pins.Count; i++)
            if (pins[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
