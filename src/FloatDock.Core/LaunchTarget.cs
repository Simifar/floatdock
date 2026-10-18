namespace FloatDock.Core;

/// <summary>Values are stored in settings; append new kinds at the end.</summary>
public enum LaunchTargetKind { Executable, Shortcut, AppUserModelId, Folder, File }

public sealed record LaunchTarget(LaunchTargetKind Kind, string Value)
{
    /// <summary>A dropped or chosen path: executables and shortcuts start apps, folders and other files open.</summary>
    public static LaunchTarget? FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        if (!Path.IsPathFullyQualified(fullPath)) return null;
        if (Directory.Exists(fullPath)) return new(LaunchTargetKind.Folder, Path.TrimEndingDirectorySeparator(fullPath));
        if (!File.Exists(fullPath)) return null;
        return Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".exe" => new(LaunchTargetKind.Executable, fullPath),
            ".lnk" => new(LaunchTargetKind.Shortcut, fullPath),
            _ => new(LaunchTargetKind.File, fullPath)
        };
    }

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Value)) return false;
        try
        {
            return Kind switch
            {
                LaunchTargetKind.Executable => Value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(Value),
                LaunchTargetKind.Shortcut => Value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(Value),
                LaunchTargetKind.Folder or LaunchTargetKind.File => Path.IsPathFullyQualified(Value) && !Value.Contains('\0'),
                // Launched as shell:AppsFolder\{Value}: either an AppUserModelID or a Start menu known-folder item.
                LaunchTargetKind.AppUserModelId => IsAppUserModelId(Value) || IsKnownFolderItem(Value),
                _ => false
            };
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>
    /// An AppUserModelID: a single name such as "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" or
    /// "308046B0AF4A39CB;PrivateBrowsingAUMID", never a path.
    /// </summary>
    public static bool IsAppUserModelId(string value) =>
        value.Length is > 0 and <= 256 && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '!' or ';' or ' ') &&
        !value.Contains("..") && value.Trim().Length == value.Length;

    /// <summary>
    /// A Start menu item named by known folder and relative path, such as "{GUID}\Vendor\app.exe". It points at a file,
    /// so it is only launched when the current app catalog lists it.
    /// </summary>
    public static bool IsKnownFolderItem(string value) =>
        value.Length <= 512 && value.Length > 40 && value[0] == '{' && value[37] == '}' && value[38] == '\\' &&
        Guid.TryParse(value.AsSpan(1, 36), out _) && !value.Contains("..") && !value.Contains('/') &&
        value.IndexOfAny([':', '*', '?', '"', '<', '>', '|', '\0']) < 0;
}

/// <param name="ExecutablePath">The app's executable when the key is not its path (an AppUserModelID or a shortcut), so its
/// windows without that ID still match the pin.</param>
public sealed record PinnedApp(string Key, string DisplayName, LaunchTarget Target, string? ExecutablePath = null);
