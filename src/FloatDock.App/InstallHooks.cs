using System.Globalization;
using FloatDock.Core;

namespace FloatDock.App;

/// <summary>What the installer asks the app to do (Velopack hooks): no window, a few seconds at most.</summary>
internal static class InstallHooks
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FloatDock");

    private static string[] ShortcutNames => ["Restore the Windows taskbar.lnk", "Вернуть панель задач.lnk"];

    /// <summary>A Start menu entry that restores the taskbar when it is hidden and FloatDock is not running.</summary>
    public static void CreateRestoreShortcut()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (executable is null) return;
            Directory.CreateDirectory(Folder);
            var russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
            var path = Path.Combine(Folder, ShortcutNames[russian ? 1 : 0]);
            var shell = Type.GetTypeFromProgID("WScript.Shell");
            if (shell is null || Activator.CreateInstance(shell) is not { } instance) return;
            dynamic link = ((dynamic)instance).CreateShortcut(path);
            link.TargetPath = executable;
            link.Arguments = "--restore-taskbar";
            link.WorkingDirectory = Path.GetDirectoryName(executable);
            link.Save();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            LocalLog.Write("Warning", "restore-shortcut", ex);
        }
    }

    /// <summary>Before the files go: the taskbar comes back and the Start menu entry is removed.</summary>
    public static void RestoreBeforeUninstall()
    {
        try
        {
            Recovery.RestoreTaskbar();
            foreach (var name in ShortcutNames) File.Delete(Path.Combine(Folder, name));
            if (Directory.Exists(Folder) && !Directory.EnumerateFileSystemEntries(Folder).Any()) Directory.Delete(Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            LocalLog.Write("Warning", "uninstall-restore", ex);
        }
    }
}
