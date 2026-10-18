namespace FloatDock.Core;

public enum UpdateFeedKind { None, Https, Folder, Invalid }

/// <summary>
/// Where updates come from: an https address or an absolute folder (a share, or a folder for testing). Plain http is refused,
/// because the update would travel unprotected. An empty setting means no update check at all, so nothing leaves the PC.
/// </summary>
public static class UpdateSources
{
    public static UpdateFeedKind Classify(string? text, out string feed)
    {
        feed = string.Empty;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0) return UpdateFeedKind.None;
        if (value.Any(char.IsControl)) return UpdateFeedKind.Invalid;
        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0)
                return UpdateFeedKind.Invalid;
            feed = uri.AbsoluteUri;
            return UpdateFeedKind.Https;
        }
        if (!Path.IsPathFullyQualified(value) || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return UpdateFeedKind.Invalid;
        feed = Path.GetFullPath(value);
        return UpdateFeedKind.Folder;
    }
    /// <summary>
    /// A GitHub repository address (<c>https://github.com/owner/repo</c>): updates are the releases of that repository, which
    /// is how the project publishes them. Returns the normalized address, or null for any other feed.
    /// </summary>
    public static string? GitHubRepository(string feed)
    {
        if (!Uri.TryCreate(feed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0)) return null;
        var repository = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return repository.Length == 0 ? null : $"https://github.com/{parts[0]}/{repository}";
    }
}
