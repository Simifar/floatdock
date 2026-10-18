using Microsoft.UI.Xaml;
using FloatDock.Core;
using FloatDock.Windows;

namespace FloatDock.App;

public partial class App : Application
{
    private const string MutexName = "Local\\FloatDock.Main";
    private const string QuitEventName = "Local\\FloatDock.Quit";
    private DockHost? _host;
    private Mutex? _mutex;
    private StartupGuard? _startup;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => OnFatal("unhandled-ui", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => OnFatal("unhandled-domain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LocalLog.Write("Error", "unobserved-task", args.Exception);
            args.SetObserved();
        };
    }

    // The process is about to die; do not leave the user without a taskbar even if Guardian is gone too.
    private static void OnFatal(string eventName, Exception? error)
    {
        LocalLog.Write("Fatal", eventName, error);
        if (CrashDumps.Write() is not null) LocalLog.Write("Info", "crash-dump-written");
        try
        {
            TaskbarController.RecoverOrShowAll();
            AppBarHost.RemoveLeftover();
        }
        catch (Exception ex) { LocalLog.Write("Error", "fatal-taskbar-restore", ex); }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (arguments.Contains("--restore-taskbar"))
        {
            RestoreTaskbarAndExit();
            return;
        }
        _mutex = new Mutex(true, Recovery.MutexName, out var first);
        if (!first)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting("Local\\FloatDock.Activate");
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            _mutex.Dispose();
            Exit();
            return;
        }
        if (TaskbarController.RecoverFromJournal()) LocalLog.Write("Warning", "taskbar-recovered-at-startup");
        if (AppBarHost.RemoveLeftover()) LocalLog.Write("Warning", "appbar-removed-at-startup");
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\FloatDock.Activate");
        var externalQuit = new EventWaitHandle(false, EventResetMode.AutoReset, Recovery.QuitEventName);
        var quit = new EventWaitHandle(false, EventResetMode.ManualReset);
        _startup = new StartupGuard();
        var safeMode = arguments.Contains("--safe-mode") || _startup.Enter();
        var disableTaskbarReplacement = safeMode || arguments.Contains("--disable-taskbar-replacement");
        if (arguments.Contains("--reset-settings")) new SettingsStore().Reset();
        // Test only: pretend a second display exists (see ScreenDisplays.Fake).
        foreach (var argument in arguments)
            if (ScreenDisplays.TryParseFake(argument, out var fake)) ScreenDisplays.Fake = fake;
        _host = new DockHost(safeMode, disableTaskbarReplacement);
        _host.Start();
        if (arguments.Contains("--settings")) _host.OpenSettings();
        _host.Closed += () =>
        {
            _startup.MarkHealthy();
            _host = null;
            quit.Set();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        };
        var dispatcher = _host.DispatcherQueue;
        _ = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    var signaled = WaitHandle.WaitAny([activate, quit, externalQuit]);
                    if (signaled == 1) break;
                    if (signaled == 0) dispatcher.TryEnqueue(() => _host?.OpenSettings());
                    else dispatcher.TryEnqueue(() => _host?.Quit());
                }
            }
            finally
            {
                activate.Dispose();
                externalQuit.Dispose();
                quit.Dispose();
            }
        });
        if (arguments.Contains("--crash-test"))
        {
            // Test only: lets a check confirm that an unhandled exception writes a dump and puts the taskbar back.
            var crash = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            crash.Tick += (_, _) => throw new InvalidOperationException("Deliberate crash for testing the crash handler.");
            crash.Start();
        }
        var healthyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        healthyTimer.Tick += (_, _) => { healthyTimer.Stop(); _startup?.MarkHealthy(); };
        healthyTimer.Start();
    }

    /// <summary>Emergency path: ask a running dock to exit normally, then undo any taskbar change that is left.</summary>
    private void RestoreTaskbarAndExit()
    {
        Recovery.RestoreTaskbar();
        LocalLog.Write("Info", "taskbar-restored-by-command");
        Exit();
    }
}
