using System.Text.Json;

namespace FloatDock.Core;

public sealed class StartupGuard
{
    private readonly string _path;

    public StartupGuard(string? directory = null)
    {
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock");
        _path = Path.Combine(root, "startup-attempts.json");
    }

    public bool Enter()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            List<DateTimeOffset> attempts;
            try { attempts = JsonSerializer.Deserialize(File.ReadAllText(_path), StateJson.Default.ListDateTimeOffset) ?? []; }
            catch (Exception ex) when (ex is IOException or JsonException) { attempts = []; }
            attempts = attempts.Where(time => DateTimeOffset.UtcNow - time < TimeSpan.FromMinutes(2)).ToList();
            attempts.Add(DateTimeOffset.UtcNow);
            File.WriteAllText(_path, JsonSerializer.Serialize(attempts, StateJson.Default.ListDateTimeOffset));
            return attempts.Count >= 3;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LocalLog.Write("Warning", "startup-guard-unavailable", ex);
            return true;
        }
    }

    public void MarkHealthy()
    {
        try { File.Delete(_path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
