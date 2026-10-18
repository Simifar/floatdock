using System.Diagnostics;
using FloatDock.Windows;

if (args.Length != 3 || !int.TryParse(args[0], out var pid) || pid <= 0 ||
    !args[1].StartsWith("Local\\FloatDock.Normal.", StringComparison.Ordinal) ||
    !args[2].StartsWith("Local\\FloatDock.Ready.", StringComparison.Ordinal))
    return 2;

try
{
    using var normal = EventWaitHandle.OpenExisting(args[1]);
    using var ready = EventWaitHandle.OpenExisting(args[2]);
    using var process = Process.GetProcessById(pid);
    ready.Set();
    var exited = Task.Run(() => { process.WaitForExit(); return true; });
    var clean = Task.Run(() => { normal.WaitOne(); return false; });
    if (await await Task.WhenAny(exited, clean))
    {
        TaskbarController.RecoverOrShowAll();
        AppBarHost.RemoveLeftover();
    }
    return 0;
}
catch (ArgumentException)
{
    TaskbarController.RecoverOrShowAll();
    AppBarHost.RemoveLeftover();
    return 0;
}
catch (WaitHandleCannotBeOpenedException)
{
    return 3;
}
