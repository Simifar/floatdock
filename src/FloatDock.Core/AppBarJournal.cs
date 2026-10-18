using System.Text.Json;

namespace FloatDock.Core;

public sealed record AppBarRegistration(long Window, int ProcessId);

/// <summary>
/// Remembers the dock's AppBar registrations, one per dock window. Explorer keeps a reserved screen edge for an AppBar
/// whose process died, so any later process can remove it by window handle.
/// </summary>
public sealed class AppBarJournal
{
    private readonly string _path;

    public AppBarJournal(string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock");
        _path = Path.Combine(root, "appbar.json");
    }

    /// <summary>
    /// Every journaled registration. A file written by an earlier version holds a single object instead of a list;
    /// it reads as a list of one. An unreadable file reads as empty.
    /// </summary>
    public IReadOnlyList<AppBarRegistration> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var text = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                return (JsonSerializer.Deserialize(text, StateJson.Default.ListAppBarRegistration) ?? []).Where(r => r is not null).ToList();
            return JsonSerializer.Deserialize(text, StateJson.Default.AppBarRegistration) is { } single ? [single] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    /// <summary>Adds a registration, or replaces the one for the same window.</summary>
    public void Write(AppBarRegistration registration) =>
        Save(Read().Where(r => r.Window != registration.Window).Append(registration).ToList());

    /// <summary>Drops the registration of one window and deletes the file when none is left.</summary>
    public void Remove(long window)
    {
        var current = Read();
        var rest = current.Where(r => r.Window != window).ToList();
        if (rest.Count == current.Count) return;
        Save(rest);
    }

    /// <summary>Drops these registrations and deletes the file when none is left.</summary>
    public void Remove(IEnumerable<AppBarRegistration> removed)
    {
        var windows = removed.Select(r => r.Window).ToHashSet();
        Save(Read().Where(r => !windows.Contains(r.Window)).ToList());
    }

    public void Delete()
    {
        try { File.Delete(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Warning", "appbar-journal-delete", ex); }
    }

    private void Save(List<AppBarRegistration> registrations)
    {
        if (registrations.Count == 0)
        {
            Delete();
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(registrations, StateJson.Default.ListAppBarRegistration));
        File.Move(temporary, _path, overwrite: true);
    }

    /// <summary>
    /// The registrations a start, Guardian or the crash handler may remove: those whose owner is gone, and those of the
    /// calling process (it is about to die, or it is the dock cleaning up before registering again).
    /// </summary>
    public static IReadOnlyList<AppBarRegistration> Leftover(IEnumerable<AppBarRegistration> registrations, int currentProcessId,
        Func<AppBarRegistration, bool> ownerIsAlive) =>
        registrations.Where(r => r.ProcessId == currentProcessId || !ownerIsAlive(r)).ToList();
}
