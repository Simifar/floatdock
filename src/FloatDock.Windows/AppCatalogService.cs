using System.Text.Json;
using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// All apps of the Start menu list, read from shell:AppsFolder on the shell STA thread. The last list is cached
/// on disk for a fast first show; Start menu folder changes and app installs trigger a background reload.
/// </summary>
public sealed class AppCatalogService : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    // The shell lists a new Start menu entry a few seconds after its file appears, later than the first reload.
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);
    private static readonly string[] UninstallPrefixes = ["Uninstall", "Деинсталл", "Удалить "];
    private readonly StaWorker _shell;
    private readonly string _cachePath;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Lock _gate = new();
    private Timer? _reloadTimer;
    private Timer? _settleTimer;
    private global::Windows.ApplicationModel.PackageCatalog? _packages;
    private IReadOnlyList<AppEntry> _apps = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public AppCatalogService(StaWorker shell, string? cacheDirectory = null)
    {
        _shell = shell;
        var root = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock");
        _cachePath = Path.Combine(root, "apps-cache.json");
    }

    /// <summary>Raised on a thread-pool thread when the list changed.</summary>
    public event Action<IReadOnlyList<AppEntry>>? Changed;

    public IReadOnlyList<AppEntry> Apps { get { lock (_gate) return _apps; } }

    /// <summary>Shows the disk cache at once, then reloads from the shell and starts watching for changes.</summary>
    public void Start()
    {
        var cached = ReadCache();
        if (cached.Count > 0) Publish(cached, save: false);
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
            Watch(Environment.GetFolderPath(folder));
        try
        {
            _packages = global::Windows.ApplicationModel.PackageCatalog.OpenForCurrentUser();
            _packages.PackageInstalling += (_, args) => { if (args.IsComplete) ScheduleReload(); };
            _packages.PackageUninstalling += (_, args) => { if (args.IsComplete) ScheduleReload(); };
            _packages.PackageUpdating += (_, args) => { if (args.IsComplete) ScheduleReload(); };
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Without package events, packaged installs show up on the next refresh-if-stale.
            LocalLog.Write("Warning", "package-catalog-unavailable", ex);
        }
        _ = ReloadAsync();
    }

    /// <summary>Reloads when the list is older than <paramref name="maxAge"/>; for example when the launcher opens.</summary>
    public Task RefreshIfStaleAsync(TimeSpan maxAge) =>
        DateTimeOffset.UtcNow - _loadedAt > maxAge ? ReloadAsync() : Task.CompletedTask;

    public async Task ReloadAsync()
    {
        try
        {
            var apps = await _shell.RunAsync(Enumerate);
            if (apps.Count > 0) Publish(apps, save: true);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or InvalidOperationException)
        {
            LocalLog.Write("Error", "app-catalog-load", ex);
        }
    }

    private void Publish(IReadOnlyList<AppEntry> apps, bool save)
    {
        bool changed;
        lock (_gate)
        {
            changed = !_apps.SequenceEqual(apps);
            _apps = apps;
            if (save) _loadedAt = DateTimeOffset.UtcNow;
        }
        if (save && changed) WriteCache(apps);
        if (changed) Changed?.Invoke(apps);
    }

    private static IReadOnlyList<AppEntry> Enumerate()
    {
        var result = new List<AppEntry>();
        object? folder = null, enumerator = null;
        try
        {
            if (ShellInterop.SHCreateItemFromParsingName("shell:AppsFolder", 0, ShellInterop.IidShellItem, out folder) != 0 ||
                folder is not ShellInterop.IShellItem apps ||
                apps.BindToHandler(0, ShellInterop.BhidEnumItems, ShellInterop.IidEnumShellItems, out enumerator) != 0 ||
                enumerator is not ShellInterop.IEnumShellItems items) return result;
            while (items.Next(1, out var item, out var fetched) == 0 && fetched == 1)
            {
                try { if (Read(item) is { } entry) result.Add(entry); }
                finally { ShellInterop.Release(item); }
            }
        }
        finally
        {
            ShellInterop.Release(enumerator);
            ShellInterop.Release(folder);
        }
        return result.DistinctBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static AppEntry? Read(ShellInterop.IShellItem item)
    {
        item.GetDisplayName(ShellInterop.SigdnNormalDisplay, out var namePointer);
        item.GetDisplayName(ShellInterop.SigdnParentRelativeParsing, out var idPointer);
        var name = ShellInterop.TakeString(namePointer);
        var id = ShellInterop.TakeString(idPointer);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) return null;
        string? path = null;
        if (item is ShellInterop.IShellItem2 rich && rich.GetString(ShellInterop.LinkTargetParsingPath, out var target) == 0)
            path = ShellInterop.TakeString(target);
        if (path is not null && (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(path))) path = null;
        var packaged = id.Contains('!') && !id.Contains('\\');
        // Uninstallers and help links clutter an app list; keep entries that start an app.
        if (!packaged && path is null && !id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !LooksLikeAppId(id)) return null;
        if (UninstallPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))) return null;
        return new AppEntry(id, name.Trim(), path, packaged);
    }

    private static bool LooksLikeAppId(string id) => !id.Contains('\\') && !id.Contains('/') && !id.Contains("://");

    private void Watch(string folder)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
            watcher.Created += (_, _) => ScheduleReload();
            watcher.Deleted += (_, _) => ScheduleReload();
            watcher.Renamed += (_, _) => ScheduleReload();
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            LocalLog.Write("Warning", "start-menu-watch", ex);
        }
    }

    private void ScheduleReload()
    {
        lock (_gate)
        {
            _reloadTimer ??= new Timer(_ => _ = ReloadAsync());
            _reloadTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            _settleTimer ??= new Timer(_ => _ = ReloadAsync());
            _settleTimer.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    private IReadOnlyList<AppEntry> ReadCache()
    {
        try { return File.Exists(_cachePath) ? JsonSerializer.Deserialize(File.ReadAllText(_cachePath), StateJson.Default.ListAppEntry) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    private void WriteCache(IReadOnlyList<AppEntry> apps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(apps.ToList(), StateJson.Default.ListAppEntry));
            File.Move(temporary, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Warning", "app-cache-write", ex); }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
        lock (_gate)
        {
            _reloadTimer?.Dispose();
            _settleTimer?.Dispose();
        }
    }
}
