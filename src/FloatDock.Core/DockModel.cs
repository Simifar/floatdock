namespace FloatDock.Core;

public sealed record AppGroup(
    string Key,
    string DisplayName,
    string? ExecutablePath,
    IReadOnlyList<WindowInfo> Windows,
    bool IsPinned,
    LaunchTarget? LaunchTarget,
    string? AppId = null)
{
    public bool IsRunning => Windows.Count > 0;
    public bool IsActive => Windows.Any(w => w.IsForeground);
    public bool NeedsAttention => Windows.Any(w => w.IsFlashing);
}

public static class DockModel
{
    public const string AppIdPrefix = "AUMID:";

    /// <summary>
    /// Windows group like on the Windows taskbar: by AppUserModelID when one is known, otherwise by executable.
    /// </summary>
    public static string WindowKey(WindowInfo window) =>
        !string.IsNullOrWhiteSpace(window.AppId)
            ? AppIdPrefix + window.AppId.Trim().ToUpperInvariant()
            : IsHostedWindow(window.ExecutablePath)
            ? $"HOSTED:{window.Handle:X}"
            : !string.IsNullOrWhiteSpace(window.ExecutablePath)
            ? PathKey(window.ExecutablePath)
            : $"PID:{window.ProcessId}";

    public static string PathKey(string path) => Path.GetFullPath(path).ToUpperInvariant();

    public static string AppIdKey(string appId) => AppIdPrefix + appId.Trim().ToUpperInvariant();

    private static bool IsHostedWindow(string? executablePath) =>
        string.Equals(Path.GetFileName(executablePath), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

    /// <param name="appFor">Optional Start menu entry for an AppUserModelID or executable path: its name and launch target win.</param>
    /// <param name="nameFor">Optional fallback name for an executable without a Start menu entry.</param>
    public static IReadOnlyList<AppGroup> Build(IReadOnlyList<WindowInfo> windows, IReadOnlyList<PinnedApp> pins,
        Func<string?, string?, AppEntry?>? appFor = null, Func<string, string?>? nameFor = null)
    {
        var eligible = windows.Where(WindowEligibility.IsEligible).GroupBy(WindowKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var groups = new List<AppGroup>();
        foreach (var pin in pins)
        {
            var key = MatchPin(pin, eligible);
            var matches = key is null ? null : eligible[key];
            if (key is not null) eligible.Remove(key);
            groups.Add(new AppGroup(pin.Key, pin.DisplayName, matches?.FirstOrDefault()?.ExecutablePath,
                matches ?? [], true, pin.Target, matches?.FirstOrDefault()?.AppId ?? AppIdOf(pin)));
        }
        foreach (var (key, matches) in eligible.OrderBy(pair => pair.Value[0].Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var first = matches[0];
            var path = IsHostedWindow(first.ExecutablePath) ? null : first.ExecutablePath;
            var app = appFor?.Invoke(first.AppId, path);
            var name = app?.Name ?? (path is null ? null : nameFor?.Invoke(path))
                ?? (path is null ? first.Title : Path.GetFileNameWithoutExtension(path));
            // A window's AppUserModelID comes from another process: launch what the Start menu or the OS reports,
            // and fall back to the ID itself only for a plain ID on a window without an executable (hosted UWP).
            var target = app?.Target
                ?? (path is not null ? new LaunchTarget(LaunchTargetKind.Executable, path)
                : first.AppId is { } id && LaunchTarget.IsAppUserModelId(id) ? new LaunchTarget(LaunchTargetKind.AppUserModelId, id)
                : null);
            groups.Add(new AppGroup(key, name, path, matches, false, target, first.AppId));
        }
        return groups;
    }

    /// <summary>
    /// A pin owns the group with its key; a path pin also owns the windows of that executable when they carry an
    /// AppUserModelID (several groups: the one with most windows, so a browser's main windows win over its web apps).
    /// </summary>
    private static string? MatchPin(PinnedApp pin, Dictionary<string, List<WindowInfo>> groups)
    {
        if (groups.ContainsKey(pin.Key)) return pin.Key;
        var pathKey = pin.ExecutablePath is { } executable && Path.IsPathFullyQualified(executable) ? PathKey(executable)
            : pin.Key.StartsWith(AppIdPrefix, StringComparison.OrdinalIgnoreCase) ? null : pin.Key;
        if (pathKey is null) return null;
        return groups.Where(pair => pair.Value.Any(w => w.ExecutablePath is { } path && SamePath(path, pathKey)))
            .OrderByDescending(pair => pair.Value.Count).Select(pair => pair.Key).FirstOrDefault();
    }

    private static bool SamePath(string path, string key)
    {
        try { return string.Equals(PathKey(path), key, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static string? AppIdOf(PinnedApp pin) =>
        pin.Target.Kind == LaunchTargetKind.AppUserModelId ? pin.Target.Value : null;

    public static IReadOnlyList<PinnedApp> Move(IReadOnlyList<PinnedApp> pins, int from, int to)
    {
        if ((uint)from >= pins.Count || (uint)to >= pins.Count) return pins.ToArray();
        var result = pins.ToList();
        var item = result[from];
        result.RemoveAt(from);
        result.Insert(to, item);
        return result;
    }
}
