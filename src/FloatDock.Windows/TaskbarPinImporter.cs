using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// Reads the apps pinned to the Windows taskbar (its User Pinned shortcuts) and turns shortcuts into pins.
/// Shortcuts are copied into FloatDock's own folder, so unpinning or deleting the original does not break the dock
/// and shortcut arguments are kept. Packaged apps pinned to the Windows taskbar are not stored as shortcuts.
/// Use on the shell STA thread.
/// </summary>
public sealed class TaskbarPinImporter(StaWorker shell)
{
    private const string ExplorerAppId = "Microsoft.Windows.Explorer";
    private readonly ShortcutResolver _shortcuts = new();

    public static string PinnedFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

    private static string Store => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock", "pins");

    public Task<IReadOnlyList<PinnedApp>> ReadAsync() => shell.RunAsync(Read);

    /// <summary>A pin for a dropped or imported shortcut, keyed like the windows of the app it starts.</summary>
    public Task<PinnedApp?> PinForShortcutAsync(string shortcutPath) => shell.RunAsync(() => PinForShortcut(shortcutPath));

    private IReadOnlyList<PinnedApp> Read()
    {
        var result = new List<PinnedApp>();
        if (!Directory.Exists(PinnedFolder)) return result;
        // The taskbar's own order lives in an undocumented registry blob; pin time is the closest documented order.
        foreach (var shortcut in new DirectoryInfo(PinnedFolder).EnumerateFiles("*.lnk").OrderBy(file => file.CreationTimeUtc))
            if (PinForShortcut(shortcut.FullName) is { } pin) result.Add(pin);
        return result.DistinctBy(pin => pin.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private PinnedApp? PinForShortcut(string shortcutPath)
    {
        try
        {
            Directory.CreateDirectory(Store);
            var source = Path.GetFullPath(shortcutPath);
            var copy = source.StartsWith(Store + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? source : CopyPathFor(source);
            // Overwriting only ever refreshes the copy of this same source shortcut.
            if (!string.Equals(source, copy, StringComparison.OrdinalIgnoreCase)) File.Copy(source, copy, overwrite: true);
            var target = new LaunchTarget(LaunchTargetKind.Shortcut, copy);
            var appId = AppIdOf(shortcutPath);
            var executable = _shortcuts.GetExecutablePath(shortcutPath) ?? KnownExecutable(appId);
            // Shortcuts that carry an AppUserModelID group with that app's windows, like on the Windows taskbar.
            var key = appId is not null ? DockModel.AppIdKey(appId) : PinList.KeyFor(target, executable);
            var name = DisplayName(shortcutPath) ?? Path.GetFileNameWithoutExtension(shortcutPath);
            return new PinnedApp(key, name, target, executable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LocalLog.Write("Warning", "shortcut-pin", ex);
            return null;
        }
    }

    /// <summary>
    /// A copy name unique to the source path: two different "Setup.lnk" files must not share one copy,
    /// or one pin would start the other's program.
    /// </summary>
    private static string CopyPathFor(string source)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(source.ToUpperInvariant())))[..12];
        return Path.Combine(Store, $"{Path.GetFileNameWithoutExtension(source)}-{hash}.lnk");
    }

    /// <summary>Explorer's shortcut points at a shell folder, not at explorer.exe, but its windows run there.</summary>
    private static string? KnownExecutable(string? appId) =>
        string.Equals(appId, ExplorerAppId, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")
            : null;

    private static string? AppIdOf(string shortcutPath)
    {
        object? store = null;
        try
        {
            if (ShellInterop.SHGetPropertyStoreFromParsingName(shortcutPath, 0, 0, ShellInterop.IidPropertyStore, out store) != 0 ||
                store is not ShellInterop.IPropertyStore properties ||
                properties.GetValue(ShellInterop.AppUserModelId, out var value) != 0) return null;
            var id = ShellInterop.ReadString(ref value)?.Trim();
            return id is not null && LaunchTarget.IsAppUserModelId(id) ? id : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException) { return null; }
        finally { ShellInterop.Release(store); }
    }

    /// <summary>The localized name Explorer shows (for example "Проводник" for File Explorer.lnk).</summary>
    internal static string? DisplayName(string path)
    {
        object? item = null;
        try
        {
            if (ShellInterop.SHCreateItemFromParsingName(path, 0, ShellInterop.IidShellItem, out item) != 0 ||
                item is not ShellInterop.IShellItem shellItem ||
                shellItem.GetDisplayName(ShellInterop.SigdnNormalDisplay, out var name) != 0) return null;
            var text = ShellInterop.TakeString(name)?.Trim();
            if (text is not null && text.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) text = text[..^4];
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (System.Runtime.InteropServices.COMException) { return null; }
        finally { ShellInterop.Release(item); }
    }
}
