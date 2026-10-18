using System.Diagnostics;
using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>Things the shell does for an app entry: show its file in Explorer, open Windows' list of installed apps.</summary>
public static class ShellActions
{
    /// <summary>Opens Explorer with the file selected. The path is never part of a command line parsed by anything but Explorer.</summary>
    public static bool RevealInExplorer(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"') || !(File.Exists(path) || Directory.Exists(path))) return false;
        return Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"" });
    }

    /// <summary>Settings > Apps > Installed apps, where an app is uninstalled.</summary>
    public static bool OpenInstalledApps() => Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });

    /// <summary>
    /// Opens a file, folder or shell: name the way a double click in Explorer does (ShellExecute), so the Shell's own
    /// checks apply: the Mark-of-the-Web warning and SmartScreen for a downloaded program.
    /// </summary>
    public static bool Open(string pathOrShellName) => Start(new ProcessStartInfo(pathOrShellName) { UseShellExecute = true });

    public static bool OpenFolder(string folderOrShellName) => Open(folderOrShellName);

    /// <summary>Shows the desktop, or brings the windows back, like the corner button of the taskbar.</summary>
    public static bool ToggleDesktop()
    {
        try
        {
            var shell = Type.GetTypeFromProgID("Shell.Application");
            if (shell is null || Activator.CreateInstance(shell) is not { } instance) return false;
            try
            {
                shell.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, instance, null);
                return true;
            }
            finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(instance); }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Reflection.TargetInvocationException or MissingMethodException)
        {
            LocalLog.Write("Warning", "toggle-desktop-failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Presses Win plus a letter, to open what Windows ties to that shortcut (A: quick settings, N: notifications and calendar).
    /// There is no other supported way to open those panels.
    /// </summary>
    public static void PressWinChord(char letter)
    {
        const byte leftWin = 0x5B;
        const uint keyUp = 2;
        var key = (byte)char.ToUpperInvariant(letter);
        Native.keybd_event(leftWin, 0, 0, 0);
        Native.keybd_event(key, 0, 0, 0);
        Native.keybd_event(key, 0, keyUp, 0);
        Native.keybd_event(leftWin, 0, keyUp, 0);
    }

    private static bool Start(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            LocalLog.Write("Warning", "shell-action-failed", ex);
            return false;
        }
    }
}
