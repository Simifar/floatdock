namespace FloatDock.Core;

/// <summary>
/// An installed app from the Start menu list (shell:AppsFolder). <see cref="Id"/> is the item's parsing name inside
/// that folder: an AppUserModelID for packaged and most registered apps, otherwise a known-folder-relative path.
/// Launching uses shell:AppsFolder\{Id} for both.
/// </summary>
public sealed record AppEntry(string Id, string Name, string? ExecutablePath, bool IsPackaged)
{
    /// <summary>Items outside known folders have a plain absolute path as their name; those launch as executables.</summary>
    public LaunchTarget Target => IsAbsoluteExecutable(Id)
        ? new(LaunchTargetKind.Executable, Id)
        : new(LaunchTargetKind.AppUserModelId, Id);

    private static bool IsAbsoluteExecutable(string id) =>
        id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(id);

    /// <summary>The key a running window of this app groups under (see <see cref="DockModel.WindowKey"/>).</summary>
    public string Key => ExecutablePath is not null && !IsPackaged && LooksLikePath(Id)
        ? DockModel.PathKey(ExecutablePath)
        : DockModel.AppIdKey(Id);

    private static bool LooksLikePath(string id) => id.Contains('\\') || id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The pin for this app. Its executable is kept so windows without the app's AppUserModelID still match, except for
    /// Windows' own programs: many shortcuts start explorer.exe or a host with arguments, which would claim unrelated windows.
    /// </summary>
    public PinnedApp ToPin()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var executable = ExecutablePath is not null && !ExecutablePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
            ? ExecutablePath
            : null;
        return new PinnedApp(Key, Name, Target, executable);
    }
}

public static class AppSearch
{
    private const string English = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
    private const string Russian = "йцукенгшщзхъфывапролджэячсмитьбюё";

    /// <summary>
    /// Ranks apps for a query: whole-name prefix, word prefix, substring, then letters in order. The query also matches
    /// when typed in the wrong keyboard layout (ru/en). Ties keep the more used app first, then the alphabet.
    /// </summary>
    public static IReadOnlyList<AppEntry> Search(IReadOnlyList<AppEntry> apps, string? query,
        IReadOnlyDictionary<string, int>? launchCounts = null, LauncherSort sort = LauncherSort.Name,
        IReadOnlyDictionary<string, long>? firstSeen = null)
    {
        var usage = launchCounts ?? new Dictionary<string, int>();
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0) return Order(apps, sort, usage, firstSeen);
        var variants = new[] { text, SwitchLayout(text) }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return apps
            .Select(app => (App: app, Score: variants.Max(variant => Score(app.Name, variant))))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => usage.GetValueOrDefault(item.App.Id))
            .ThenBy(item => item.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.App)
            .ToList();
    }

    /// <summary>
    /// The whole list in the order the user chose: alphabetical, most launched first, or most recently first seen first.
    /// Ties and apps without a record (or with 0, the baseline) fall back to the alphabet.
    /// </summary>
    public static IReadOnlyList<AppEntry> Order(IReadOnlyList<AppEntry> apps, LauncherSort sort,
        IReadOnlyDictionary<string, int>? launchCounts = null, IReadOnlyDictionary<string, long>? firstSeen = null)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        return sort switch
        {
            LauncherSort.MostUsed => apps.OrderByDescending(app => launchCounts?.GetValueOrDefault(app.Id) ?? 0).ThenBy(app => app.Name, byName).ToList(),
            LauncherSort.Newest => apps.OrderByDescending(app => firstSeen?.GetValueOrDefault(app.Id) ?? 0).ThenBy(app => app.Name, byName).ToList(),
            _ => apps.OrderBy(app => app.Name, byName).ToList()
        };
    }

    /// <summary>The most launched apps, most used first.</summary>
    public static IReadOnlyList<AppEntry> Frequent(IReadOnlyList<AppEntry> apps, IReadOnlyDictionary<string, int> launchCounts, int take) =>
        apps.Where(app => launchCounts.GetValueOrDefault(app.Id) > 0)
            .OrderByDescending(app => launchCounts[app.Id])
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(take).ToList();

    public static int Score(string name, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        if (name.StartsWith(query, comparison)) return 400;
        var words = name.Split([' ', '-', '_', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(word => word.StartsWith(query, comparison))) return 300;
        if (name.Contains(query, comparison)) return 200;
        var initials = string.Concat(words.Select(word => word[0]));
        if (initials.StartsWith(query, comparison)) return 150;
        return IsSubsequence(name, query) ? 100 : 0;
    }

    /// <summary>Maps text typed with the other keyboard layout (ru ↔ en) to what was meant.</summary>
    public static string SwitchLayout(string text) => string.Concat(text.Select(c =>
    {
        var lower = char.ToLowerInvariant(c);
        var english = English.IndexOf(lower);
        if (english >= 0) return Russian[english];
        var russian = Russian.IndexOf(lower);
        return russian >= 0 ? English[russian] : lower;
    }));

    private static bool IsSubsequence(string name, string query)
    {
        var index = 0;
        foreach (var c in name)
            if (index < query.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[index])) index++;
        return index == query.Length;
    }
}
