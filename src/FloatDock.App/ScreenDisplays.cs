using FloatDock.Core;
using Microsoft.UI.Windowing;

namespace FloatDock.App;

/// <summary>The displays as Core's <see cref="DisplayInfo"/>, read fresh each time because the work area moves with the taskbar and AppBars.</summary>
internal static class ScreenDisplays
{
    private const long FakeDisplayId = 0x7F000001;

    /// <summary>
    /// Test only (<c>--fake-display=x,y,width,height</c>): an extra display that does not exist, so a machine with one monitor
    /// can still create, place and close the docks of a second one.
    /// </summary>
    public static ScreenRect? Fake { get; set; }

    public static DisplayInfo Primary() => From(DisplayArea.Primary);

    public static IReadOnlyList<DisplayInfo> All() { TryAll(out var displays); return displays; }

    /// <summary>All displays; false (with just the primary one) when they could not be listed, so callers do not mistake that for unplugged displays.</summary>
    public static bool TryAll(out IReadOnlyList<DisplayInfo> result)
    {
        var displays = new List<DisplayInfo>();
        var listed = true;
        try
        {
            // By index: enumerating the WinRT list with LINQ fails in this projection (InvalidCastException, class not registered).
            var areas = DisplayArea.FindAll();
            for (var i = 0; i < areas.Count; i++) displays.Add(From(areas[i]));
        }
        catch (Exception ex) when (ex is InvalidCastException or System.Runtime.InteropServices.COMException)
        {
            LocalLog.Write("Warning", "display-enumeration", ex);
            displays.Clear();
            displays.Add(Primary());
            listed = false;
        }
        if (Fake is { } fake) displays.Add(new DisplayInfo(FakeDisplayId, fake, fake, false));
        result = displays;
        return listed;
    }

    public static DisplayInfo? Find(long id) => All().FirstOrDefault(display => display.Id == id);

    public static bool TryParseFake(string argument, out ScreenRect rect)
    {
        rect = default;
        const string prefix = "--fake-display=";
        if (!argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var parts = argument[prefix.Length..].Split(',');
        if (parts.Length != 4 || !parts.All(part => int.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out _))) return false;
        var numbers = parts.Select(part => int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (numbers[2] <= 0 || numbers[3] <= 0) return false;
        rect = new ScreenRect(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    private static DisplayInfo From(DisplayArea area) => new(
        unchecked((long)area.DisplayId.Value),
        new ScreenRect(area.OuterBounds.X, area.OuterBounds.Y, area.OuterBounds.Width, area.OuterBounds.Height),
        new ScreenRect(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height),
        area.IsPrimary);
}
