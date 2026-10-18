using System.Text.Json;

namespace FloatDock.Core;

public static class LocalLog
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock", "logs");

    public static void Write(string level, string eventName, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, "floatdock.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                {
                    var old = Path.Combine(DirectoryPath, "floatdock.2.log");
                    var previous = Path.Combine(DirectoryPath, "floatdock.1.log");
                    if (File.Exists(old)) File.Delete(old);
                    if (File.Exists(previous)) File.Move(previous, old);
                    File.Move(path, previous);
                }
                var entry = new LogEntry(DateTimeOffset.UtcNow, level, eventName, error?.GetType().Name, error?.HResult, Environment.ProcessId);
                File.AppendAllText(path, JsonSerializer.Serialize(entry, StateJson.Default.LogEntry) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
}
