using System.Collections.Concurrent;
using System.Threading.Channels;
using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// Keeps a snapshot of taskbar-eligible windows. Narrow WinEvent hooks and shell hook messages request a rescan;
/// rescans are coalesced, and a slow reconciliation repairs anything missed.
/// Start it on a thread with a message loop: hook callbacks are delivered there.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    public const int ShellWindowCreated = 1, ShellWindowDestroyed = 2, ShellWindowActivated = 4, ShellRedraw = 6,
        ShellWindowReplaced = 13, ShellRudeAppActivated = 0x8004, ShellFlash = 0x8006;
    private const uint EventObjectCloaked = 0x8017, EventObjectUncloaked = 0x8018;
    private const uint GaRoot = 2;
    private readonly WindowScanner _scanner = new();
    private readonly Channel<byte> _events = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Native.WinEventProc _callback;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<nint> _hooks = [];
    private readonly ConcurrentDictionary<nint, byte> _flashing = new();
    private Task? _worker;
    private Task? _reconciler;
    private IReadOnlyList<WindowInfo> _lastSnapshot = [];
    private volatile HashSet<nint> _known = [];
    private nint _lastExternalForeground;

    public event Action<IReadOnlyList<WindowInfo>>? SnapshotChanged;

    /// <summary>
    /// Another process brought a window to the front. Raised on a thread-pool thread, even when the window is not one the dock
    /// lists (a full-screen game, a video player) and so the snapshot does not change.
    /// </summary>
    public event Action? ForegroundChanged;

    /// <summary>
    /// The foreground window of another process, as of the last foreground change. The dock's own activation
    /// is not reported (hooks skip this process), so this still names the app the user was in before clicking the dock.
    /// </summary>
    public nint LastExternalForeground => _lastExternalForeground;

    public WindowTracker() => _callback = OnWinEvent;

    public void Start()
    {
        if (_worker is not null) return;
        var foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out var foregroundProcess);
        if (foregroundProcess != Environment.ProcessId) _lastExternalForeground = foreground;
        // Only events that can change the window list: no location, focus or selection noise.
        Hook(Native.EventSystemForeground, Native.EventSystemForeground);
        Hook(Native.EventObjectDestroy, Native.EventObjectHide);
        Hook(Native.EventSystemMinimizeStart, Native.EventSystemMinimizeEnd);
        Hook(EventObjectCloaked, EventObjectUncloaked);
        _worker = Task.Run(() => ProcessAsync(_stop.Token));
        _reconciler = Task.Run(() => ReconcileAsync(_stop.Token));
        _events.Writer.TryWrite(1);
    }

    public void Refresh() => _events.Writer.TryWrite(1);

    /// <summary>
    /// Shell hook message (RegisterShellHookWindow): the taskbar's own notifications for created, destroyed,
    /// retitled, activated and flashing windows.
    /// </summary>
    public void OnShellEvent(int code, nint hwnd)
    {
        switch (code)
        {
            case ShellFlash:
                _flashing[hwnd] = 0;
                break;
            case ShellWindowActivated or ShellRudeAppActivated:
                _flashing.TryRemove(hwnd, out _);
                break;
            case ShellWindowDestroyed:
                _flashing.TryRemove(hwnd, out _);
                break;
            case not (ShellWindowCreated or ShellRedraw or ShellWindowReplaced):
                return;
        }
        _events.Writer.TryWrite(1);
    }

    private void Hook(uint first, uint last)
    {
        var handle = Native.SetWinEventHook(first, last, 0, _callback, 0, 0,
            Native.WineventOutOfContext | Native.WineventSkipOwnProcess);
        if (handle != 0) _hooks.Add(handle);
        else LocalLog.Write("Warning", "win-event-hook-unavailable");
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (hwnd == 0 || objectId != 0 || childId != 0) return;
        if (eventType == Native.EventSystemForeground)
        {
            _lastExternalForeground = hwnd;
            _flashing.TryRemove(hwnd, out _);
            ForegroundChanged?.Invoke();
        }
        // Child windows show and hide constantly; only top-level windows can change the dock.
        else if (eventType == Native.EventObjectDestroy ? !_known.Contains(hwnd) : Native.GetAncestor(hwnd, GaRoot) != hwnd) return;
        _events.Writer.TryWrite(1);
    }

    private async Task ProcessAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await _events.Reader.WaitToReadAsync(token)) break;
                await Task.Delay(80, token);
                while (_events.Reader.TryRead(out _)) { }
                var snapshot = _scanner.Scan(_flashing.ContainsKey);
                _known = snapshot.Select(window => window.Handle).ToHashSet();
                foreach (var hwnd in _flashing.Keys)
                    if (!_known.Contains(hwnd)) _flashing.TryRemove(hwnd, out _);
                if (!_lastSnapshot.SequenceEqual(snapshot))
                {
                    _lastSnapshot = snapshot;
                    SnapshotChanged?.Invoke(snapshot);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LocalLog.Write("Error", "window-scan", ex);
                try { await Task.Delay(TimeSpan.FromSeconds(1), token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(45));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                _scanner.ForgetIdentities();
                _events.Writer.TryWrite(1);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var hook in _hooks) Native.UnhookWinEvent(hook);
        _hooks.Clear();
        _events.Writer.TryComplete();
        var tasks = new[] { _worker, _reconciler }.OfType<Task>().ToArray();
        _ = Task.WhenAll(tasks).ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
