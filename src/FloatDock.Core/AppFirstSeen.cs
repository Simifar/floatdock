using System.Text.Json;

namespace FloatDock.Core;

/// <summary>
/// When FloatDock first saw each catalog app, by <see cref="AppEntry.Id"/>, in Unix seconds. It marks newly installed apps "New" and
/// sorts the app menu by recency. The apps that were already installed the first time the file was written are recorded as 0 ("not new").
/// This is what FloatDock knows, not the install date Windows keeps.
/// </summary>
public sealed class AppFirstSeen
{
    private const int MaxEntries = 2000;
    /// <summary>How long a newly seen app is called new.</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(7);
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, long> _seen = new(StringComparer.OrdinalIgnoreCase);
    private bool _existed;

    public AppFirstSeen(string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock");
        _path = Path.Combine(root, "apps-first-seen.json");
    }

    public void Load()
    {
        Dictionary<string, long>? loaded = null;
        var existed = false;
        try
        {
            if (File.Exists(_path))
            {
                existed = true;
                loaded = JsonSerializer.Deserialize(File.ReadAllText(_path), StateJson.Default.DictionaryStringInt64);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { LocalLog.Write("Warning", "apps-first-seen-read", ex); }
        lock (_gate)
        {
            _existed = existed;
            _seen = new Dictionary<string, long>(loaded ?? [], StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>A copy that is safe to read while the catalog changes.</summary>
    public IReadOnlyDictionary<string, long> Snapshot()
    {
        lock (_gate) return new Dictionary<string, long>(_seen, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Records the apps of the catalog that were not known before; returns whether anything was added.</summary>
    public bool Observe(IEnumerable<string> ids, DateTimeOffset now)
    {
        var list = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        // An empty list is a catalog that has not been read yet, not an empty machine.
        if (list.Count == 0) return false;
        Dictionary<string, long> copy;
        lock (_gate)
        {
            // The first list ever seen is the baseline: nothing in it is new.
            var stamp = _existed ? now.ToUnixTimeSeconds() : 0;
            var added = false;
            foreach (var id in list)
                if (_seen.TryAdd(id, stamp)) added = true;
            if (!added && _existed) return false;
            _existed = true;
            if (_seen.Count > MaxEntries)
            {
                var present = list.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var gone in _seen.Keys.Where(key => !present.Contains(key)).OrderBy(key => _seen[key]).Take(_seen.Count - MaxEntries).ToList())
                    _seen.Remove(gone);
            }
            copy = new Dictionary<string, long>(_seen);
        }
        Save(copy);
        return true;
    }

    /// <summary>Whether the app was first seen within <see cref="NewFor"/> before <paramref name="now"/>.</summary>
    public static bool IsNew(IReadOnlyDictionary<string, long> seen, string id, DateTimeOffset now) =>
        seen.TryGetValue(id, out var at) && at > 0 && now.ToUnixTimeSeconds() - at < NewFor.TotalSeconds;

    private void Save(Dictionary<string, long> seen)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(seen, StateJson.Default.DictionaryStringInt64));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Warning", "apps-first-seen-write", ex); }
    }
}
