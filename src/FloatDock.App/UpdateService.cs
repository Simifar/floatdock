using FloatDock.Core;
using Velopack;

namespace FloatDock.App;

internal enum UpdateOutcome { NoSource, BadSource, NotInstalled, UpToDate, Available, Failed }

internal sealed record UpdateCheck(UpdateOutcome Outcome, string? Version = null);

/// <summary>
/// Updates through Velopack, only when the user gave a feed and the app was installed by its installer. Nothing is contacted
/// otherwise. A downloaded update is applied when the dock exits (or at once on request, with a restart); the dock's normal
/// exit puts the Windows taskbar back first, and the new version hides it again.
/// </summary>
internal sealed class UpdateService
{
    private UpdateManager? _manager;
    private UpdateInfo? _pending;
    private bool _downloaded;
    private bool _scheduled;

    public async Task<UpdateCheck> CheckAsync(string feedText)
    {
        switch (UpdateSources.Classify(feedText, out var feed))
        {
            case UpdateFeedKind.None: return new UpdateCheck(UpdateOutcome.NoSource);
            case UpdateFeedKind.Invalid: return new UpdateCheck(UpdateOutcome.BadSource);
        }
        try
        {
            // A GitHub repository address means that repository's releases (how the project publishes); anything else is a plain feed.
            _manager = UpdateSources.GitHubRepository(feed) is { } repository
                ? new UpdateManager(new Velopack.Sources.GithubSource(repository, null, false))
                : new UpdateManager(feed);
            if (!_manager.IsInstalled) return new UpdateCheck(UpdateOutcome.NotInstalled);
            _pending = await _manager.CheckForUpdatesAsync();
            _downloaded = false;
            return _pending is null
                ? new UpdateCheck(UpdateOutcome.UpToDate)
                : new UpdateCheck(UpdateOutcome.Available, _pending.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Text.Json.JsonException or TaskCanceledException)
        {
            LocalLog.Write("Warning", "update-check", ex);
            return new UpdateCheck(UpdateOutcome.Failed);
        }
    }

    /// <summary>Downloads the update found by <see cref="CheckAsync"/>; false when there is none or the download failed.</summary>
    public async Task<bool> DownloadAsync()
    {
        if (_manager is null || _pending is null) return false;
        if (_downloaded) return true;
        try
        {
            await _manager.DownloadUpdatesAsync(_pending);
            _downloaded = true;
            LocalLog.Write("Info", "update-downloaded");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Text.Json.JsonException or TaskCanceledException)
        {
            LocalLog.Write("Warning", "update-download", ex);
            return false;
        }
    }

    /// <summary>Hands the downloaded update to the updater, which applies it once this process has exited. Once only.</summary>
    public bool ApplyOnExit(bool restart)
    {
        if (_manager is null || _pending is null || !_downloaded || _scheduled) return false;
        try
        {
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: restart);
            _scheduled = true;
            LocalLog.Write("Info", restart ? "update-apply-restart" : "update-apply-on-exit");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LocalLog.Write("Warning", "update-apply", ex);
            return false;
        }
    }

    /// <summary>Whether a downloaded update waits for the next exit.</summary>
    public bool HasDownloaded => _downloaded && !_scheduled;
}
