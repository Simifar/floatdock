using Microsoft.Win32;
using Windows.ApplicationModel;

namespace FloatDock.Windows;

public sealed class StartupManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FloatDock";
    /// <summary>The task declared in the package manifest (packaging/msix/AppxManifest.xml).</summary>
    public const string StartupTaskId = "FloatDockStartup";

    public bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return string.Equals(key?.GetValue(ValueName) as string, Quote(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public bool SetEnabled(string executablePath, bool enabled)
    {
        if (!Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath)) return false;
        var command = Quote(executablePath);
        if (command.Length > 260) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (enabled) key.SetValue(ValueName, command, RegistryValueKind.String);
            else if (key.GetValue(ValueName) is string current &&
                     string.Equals(current, command, StringComparison.OrdinalIgnoreCase)) key.DeleteValue(ValueName, false);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }

    /// <summary>Whether FloatDock starts with Windows: the Run key for a plain install, the StartupTask for the Store package.</summary>
    public async Task<bool> IsEnabledAsync(string executablePath)
    {
        if (!PackageIdentity.IsPackaged) return IsEnabled(executablePath);
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException) { return false; }
    }

    /// <summary>True when the request was carried out. The user can still turn the Store package's task off in Task Manager or Settings.</summary>
    public async Task<bool> SetEnabledAsync(string executablePath, bool enabled)
    {
        if (!PackageIdentity.IsPackaged) return SetEnabled(executablePath, enabled);
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (!enabled)
            {
                task.Disable();
                return true;
            }
            return await task.RequestEnableAsync() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException) { return false; }
    }

    private static string Quote(string path) => '"' + path + '"';
}
