using System.Text.Json;

namespace FloatDock.Core;

/// <summary>How often each catalog app was started from FloatDock, by <see cref="AppEntry.Id"/>; it ranks search ties and fills "Frequent".</summary>
public sealed class LaunchCounts
{
    private const int MaxEntries = 500;
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public LaunchCounts(string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock");
        _path = Path.Combine(root, "launch-counts.json");
    }

    public void Load()
    {
        Dictionary<string, int>? loaded = null;
        try { if (File.Exists(_path)) loaded = JsonSerializer.Deserialize(File.ReadAllText(_path), StateJson.Default.DictionaryStringInt32); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { LocalLog.Write("Warning", "launch-counts-read", ex); }
        lock (_gate)
            _counts = new Dictionary<string, int>((loaded ?? []).Where(pair => pair.Value > 0), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A copy that is safe to read while launches are counted.</summary>
    public IReadOnlyDictionary<string, int> Snapshot()
    {
        lock (_gate) return new Dictionary<string, int>(_counts, StringComparer.OrdinalIgnoreCase);
    }

    public void Increment(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        Dictionary<string, int> copy;
        lock (_gate)
        {
            _counts[id] = _counts.GetValueOrDefault(id) + 1;
            if (_counts.Count > MaxEntries)
                foreach (var stale in _counts.OrderBy(pair => pair.Value).Take(_counts.Count - MaxEntries).Select(pair => pair.Key).ToList())
                    _counts.Remove(stale);
            copy = new Dictionary<string, int>(_counts);
        }
        Save(copy);
    }

    private void Save(Dictionary<string, int> counts)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(counts, StateJson.Default.DictionaryStringInt32));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Warning", "launch-counts-write", ex); }
    }
}
