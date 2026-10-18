namespace FloatDock.Core;

public enum DockTheme { System, Light, Dark }
public enum DockLanguage { System, English, Russian }
public enum DockMode { Floating, Reserved, AutoHide }
/// <summary>Which displays carry a dock: only the primary one (the default), or every display.</summary>
public enum DockDisplays { Primary, All }
/// <summary>The order of the app menu without a search: alphabetical, most launched first, newest first.</summary>
public enum LauncherSort { Name, MostUsed, Newest }

public sealed record DockSettings
{
    public int SchemaVersion { get; init; } = 1;
    public DockTheme Theme { get; init; } = DockTheme.System;
    public DockLanguage Language { get; init; } = DockLanguage.System;
    public DockMode Mode { get; init; } = DockMode.Floating;
    public DockDisplays Displays { get; init; } = DockDisplays.Primary;
    public int IconSize { get; init; } = 52;
    public double Magnification { get; init; } = 1.45;
    /// <summary>Gap between the icons, in dips.</summary>
    public int Spacing { get; init; } = 8;
    /// <summary>How opaque the shelf is, in percent.</summary>
    public int ShelfOpacity { get; init; } = DefaultShelfOpacity;
    /// <summary>Corner radius of the shelf, in dips.</summary>
    public int ShelfRadius { get; init; } = DefaultShelfRadius;
    public bool HideInFullscreen { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public bool ReplaceTaskbar { get; init; }
    public bool StartWithWindows { get; init; }
    /// <summary>The user answered (or dismissed) the first-run taskbar replacement question.</summary>
    public bool FirstRunCompleted { get; init; }
    public List<PinnedApp> Pins { get; init; } = [];
    public LauncherSort LauncherSort { get; init; } = LauncherSort.Name;
    /// <summary>Catalog app ids (<see cref="AppEntry.Id"/>) the user hid from the app menu.</summary>
    public List<string> HiddenApps { get; init; } = [];
    /// <summary>The "all apps" button, first in the dock.</summary>
    public bool ShowAppsButton { get; init; } = true;
    // System items, each a switch in Settings; none is a pin.
    public bool ShowSeparator { get; init; } = true;
    public bool ShowRecycleBin { get; init; } = true;
    public bool ShowDownloads { get; init; } = true;
    public bool ShowDesktopButton { get; init; }
    public bool ShowClock { get; init; }
    public bool ShowQuickSettings { get; init; }
    public bool ShowNotifications { get; init; }
    /// <summary>Where updates come from, see <see cref="UpdateSources"/>; empty means the dock never checks. Nothing is contacted until the user presses the check button or turns on checking at startup.</summary>
    public string UpdateFeed { get; init; } = DefaultUpdateFeed;
    /// <summary>Check the feed when the dock starts and install a found update when the dock next exits.</summary>
    public bool CheckForUpdatesAtStartup { get; init; }
    /// <summary>Global shortcut that opens the app menu, see <see cref="HotKeyGesture"/>; empty means none.</summary>
    public string LauncherHotKey { get; init; } = DefaultLauncherHotKey;

    // Chosen to be free on a developer machine: not Alt+Space (PowerToys Run, the window menu), Ctrl+Space (IME switch,
    // PowerToys Peek), Ctrl+Alt+Space (Claude desktop), Ctrl+Shift+Space (parameter hints in editors) or a Win combination.
    public const string DefaultLauncherHotKey = "Ctrl+Alt+A";

    public const int DefaultShelfOpacity = 88;
    public const int DefaultShelfRadius = 22;

    /// <summary>The project's own releases.</summary>
    public const string DefaultUpdateFeed = "https://github.com/Simifar/floatdock";

    public DockSettings Validate() => this with
    {
        LauncherHotKey = string.IsNullOrWhiteSpace(LauncherHotKey) ? string.Empty
            : HotKeyGesture.TryParse(LauncherHotKey, out var gesture) ? gesture.ToString() : DefaultLauncherHotKey,
        UpdateFeed = (UpdateFeed ?? string.Empty).Trim() is { Length: <= 500 } feed ? feed : string.Empty,
        IconSize = Math.Clamp(IconSize, 32, 96),
        Magnification = Math.Clamp(double.IsFinite(Magnification) ? Magnification : 1.45, 1, 2),
        Spacing = Math.Clamp(Spacing, 0, 32),
        ShelfOpacity = Math.Clamp(ShelfOpacity, 30, 100),
        ShelfRadius = Math.Clamp(ShelfRadius, 0, 32),
        Theme = Enum.IsDefined(Theme) ? Theme : DockTheme.System,
        Language = Enum.IsDefined(Language) ? Language : DockLanguage.System,
        Mode = Enum.IsDefined(Mode) ? Mode : DockMode.Floating,
        Displays = Enum.IsDefined(Displays) ? Displays : DockDisplays.Primary,
        LauncherSort = Enum.IsDefined(LauncherSort) ? LauncherSort : LauncherSort.Name,
        HiddenApps = (HiddenApps ?? []).Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 512).Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToList(),
        Pins = (Pins ?? []).Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Key) &&
                p.Target is not null && Enum.IsDefined(p.Target.Kind) &&
                p.Target.IsValid())
            .Select(p => p with { DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Key : p.DisplayName.Trim() })
            .DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Take(64).ToList()
    };
}
