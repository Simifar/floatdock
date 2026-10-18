using System.Text.Json;

namespace FloatDock.Core;

public sealed class SettingsStore
{
    // Reflection-based on purpose: source generation would leave a property that is missing from an older file at its
    // type default instead of its initializer (a new switch would read as off). Only the app uses this, not the AOT-compiled Guardian.
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsStore(string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock");
        _path = Path.Combine(root, "settings.json");
    }

    public void Reset()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    public async Task<DockSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new DockSettings();
        try
        {
            await using var stream = File.OpenRead(_path);
            var settings = await JsonSerializer.DeserializeAsync<DockSettings>(stream, JsonOptions, cancellationToken);
            return Migrate(settings ?? new DockSettings()).Validate();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try { File.Move(_path, backup, overwrite: true); }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
            return new DockSettings();
        }
    }

    public async Task SaveAsync(DockSettings settings, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings.Validate() with { SchemaVersion = 1 }, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { _saveGate.Release(); }
    }

    private static DockSettings Migrate(DockSettings settings) => settings.SchemaVersion switch
    {
        1 => settings,
        0 => settings with { SchemaVersion = 1, ReplaceTaskbar = false, Mode = DockMode.Floating },
        _ => new DockSettings()
    };
}
