using FloatDock.Windows;

namespace FloatDock.App;

/// <summary>Puts the shell back as it was: used by <c>--restore-taskbar</c> and by the uninstaller.</summary>
internal static class Recovery
{
    public const string MutexName = "Local\\FloatDock.Main";
    public const string QuitEventName = "Local\\FloatDock.Quit";

    /// <summary>Asks a running dock to exit normally, then undoes any taskbar change and AppBar band that is left.</summary>
    public static void RestoreTaskbar()
    {
        try
        {
            using (var quit = EventWaitHandle.OpenExisting(QuitEventName)) quit.Set();
            using var running = Mutex.OpenExisting(MutexName);
            try { if (running.WaitOne(TimeSpan.FromSeconds(5))) running.ReleaseMutex(); }
            catch (AbandonedMutexException) { running.ReleaseMutex(); }
        }
        catch (WaitHandleCannotBeOpenedException) { }
        TaskbarController.RecoverOrShowAll();
        AppBarHost.RemoveLeftover();
    }
}
