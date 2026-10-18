using System.Diagnostics;

namespace FloatDock.App;

internal sealed class GuardianSession : IDisposable
{
    private readonly EventWaitHandle _normal;
    private readonly EventWaitHandle _ready;
    private readonly Process _process;
    private volatile bool _disposed;

    public event Action<GuardianSession>? UnexpectedExit;

    private GuardianSession(EventWaitHandle normal, EventWaitHandle ready, Process process)
    {
        _normal = normal;
        _ready = ready;
        _process = process;
        _process.EnableRaisingEvents = true;
        _process.Exited += OnExited;
        if (_process.HasExited) throw new InvalidOperationException("Guardian exited during startup.");
    }

    public static async Task<GuardianSession?> TryStartAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Guardian", "FloatDock.Guardian.exe");
        if (!File.Exists(path)) return null;
        var token = Guid.NewGuid().ToString("N");
        var normal = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\FloatDock.Normal." + token);
        var ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\FloatDock.Ready." + token);
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("Local\\FloatDock.Normal." + token);
            start.ArgumentList.Add("Local\\FloatDock.Ready." + token);
            process = Process.Start(start);
            if (process is null || !await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(3))))
            {
                normal.Set();
                process?.Dispose();
                normal.Dispose();
                ready.Dispose();
                return null;
            }
            return new GuardianSession(normal, ready, process);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            normal.Set();
            process?.Dispose();
            normal.Dispose();
            ready.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _process.Exited -= OnExited;
        _normal.Set();
        _process.Dispose();
        _normal.Dispose();
        _ready.Dispose();
    }

    private void OnExited(object? sender, EventArgs args)
    {
        if (!_disposed) UnexpectedExit?.Invoke(this);
    }
}
