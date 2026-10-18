using Velopack;

namespace FloatDock.App;

/// <summary>
/// The entry point (the XAML-generated one is switched off in the project). Velopack runs first: when the process was started
/// by the installer or the updater with one of its command lines it handles it here and exits, before any window exists.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => InstallHooks.CreateRestoreShortcut())
            .OnAfterUpdateFastCallback(_ => InstallHooks.CreateRestoreShortcut())
            .OnBeforeUninstallFastCallback(_ => InstallHooks.RestoreBeforeUninstall())
            .Run();
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
