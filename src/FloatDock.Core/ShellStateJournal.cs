using System.Text.Json;

namespace FloatDock.Core;

public sealed record ShellState(int OriginalAppBarState, int ProcessId, DateTimeOffset HiddenAt);

/// <summary>
/// Persists the taskbar state FloatDock changed, before it changes it, so any process can undo the change later.
/// </summary>
public sealed class ShellStateJournal
{
    private readonly string _path;

    public ShellStateJournal(string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock");
        _path = Path.Combine(root, "shell-state.json");
    }

    public bool Exists => File.Exists(_path);

    /// <summary>Returns null when the journal is missing or unreadable.</summary>
    public ShellState? Read()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize(File.ReadAllText(_path), StateJson.Default.ShellState) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Write(ShellState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, state, StateJson.Default.ShellState);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _path, overwrite: true);
    }

    public void Delete()
    {
        try { File.Delete(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LocalLog.Write("Warning", "shell-journal-delete", ex); }
    }
}
