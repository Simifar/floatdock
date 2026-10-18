using System.Diagnostics;
using FloatDock.Core;

namespace FloatDock.Windows;

/// <param name="isListedItem">
/// Whether a known-folder Start menu item name is in the current app catalog; such names point at files,
/// so without this check only plain AppUserModelIDs launch through shell:AppsFolder.
/// </param>
public sealed class LaunchService(Func<string, bool>? isListedItem = null)
{
    /// <summary>
    /// Starts a pinned or catalog app. Packaged and Start menu apps start through their shell:AppsFolder item,
    /// exactly as from the Start menu; paths are never parsed as command lines.
    /// </summary>
    public bool Launch(LaunchTarget target, bool asAdministrator = false)
    {
        if (!target.IsValid()) return false;
        if (target.Kind == LaunchTargetKind.AppUserModelId && !LaunchTarget.IsAppUserModelId(target.Value) &&
            isListedItem?.Invoke(target.Value) != true)
        {
            LocalLog.Write("Warning", "launch-unlisted-item");
            return false;
        }
        if (target.Kind == LaunchTargetKind.Folder ? !Directory.Exists(target.Value)
            : target.Kind != LaunchTargetKind.AppUserModelId && !File.Exists(target.Value)) return false;
        try
        {
            var info = target.Kind switch
            {
                LaunchTargetKind.AppUserModelId => new ProcessStartInfo(IconService.AppsFolderItem(target.Value)) { UseShellExecute = true },
                LaunchTargetKind.Shortcut or LaunchTargetKind.Folder or LaunchTargetKind.File =>
                    new ProcessStartInfo(target.Value) { UseShellExecute = true },
                _ => new ProcessStartInfo(target.Value)
                {
                    UseShellExecute = asAdministrator,
                    WorkingDirectory = Path.GetDirectoryName(target.Value)!
                }
            };
            if (asAdministrator)
            {
                info.UseShellExecute = true;
                info.Verb = "runas";
            }
            using var process = Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            LocalLog.Write("Warning", "launch-failed", ex);
            return false;
        }
    }
}
