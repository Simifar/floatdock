using FloatDock.Core;
using Microsoft.UI.Xaml.Media;

namespace FloatDock.App;

/// <summary>
/// One subscription to <see cref="CompositionTarget.Rendering"/> per dock window, shared by every running animation. It exists only
/// while some animation runs. Each step gets the seconds it covers (bounded and rate-limited by <see cref="FramePacer"/>) and says
/// whether it has more to do. A DispatcherTimer cannot do this job: it ticks about every 30 ms and Start restarts it.
/// </summary>
internal sealed class FrameLoop
{
    private readonly FramePacer _pacer = new();
    private readonly List<Func<double, bool>> _steps = [];
    private bool _subscribed;

    private static double Now => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Runs <paramref name="step"/> once per frame until it returns false or <see cref="Stop"/> is called; already running steps are left alone.</summary>
    public void Run(Func<double, bool> step)
    {
        if (_steps.Contains(step)) return;
        _steps.Add(step);
        if (_subscribed) return;
        _subscribed = true;
        _pacer.Reset(Now);
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop(Func<double, bool> step)
    {
        _steps.Remove(step);
        if (_steps.Count == 0) Unsubscribe();
    }

    public void StopAll()
    {
        _steps.Clear();
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, object args)
    {
        if (!_pacer.TryStep(Now, out var seconds)) return;
        // A step may start or stop animations (including itself), so walk a copy and ask whether each one is still wanted.
        foreach (var step in _steps.ToArray())
            if (_steps.Contains(step) && !step(seconds)) _steps.Remove(step);
        if (_steps.Count == 0) Unsubscribe();
    }
}
