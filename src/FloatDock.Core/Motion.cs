namespace FloatDock.Core;

/// <summary>Pure animation maths shared by magnification, sliding and item motion; no UI types, so it is tested directly.</summary>
public static class Motion
{
    /// <summary>Longest time one step may advance; a stalled UI thread must not make icons or the window jump.</summary>
    public const double MaxStepSeconds = 0.05;

    /// <summary>Frames that come sooner than this after the last step are skipped (the time is kept for the next one).</summary>
    public const double MinStepSeconds = 0.006;

    /// <summary>Share of the remaining distance covered in <paramref name="seconds"/> by an exponential approach with the given time constant.</summary>
    public static double Follow(double seconds, double timeConstant) =>
        timeConstant <= 0 ? 1 : 1 - Math.Exp(-Math.Max(0, seconds) / timeConstant);

    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by <paramref name="share"/> of the distance; within <paramref name="epsilon"/> it lands exactly on the target.</summary>
    public static double Approach(double current, double target, double share, double epsilon)
    {
        var next = current + (target - current) * share;
        return Math.Abs(target - next) < epsilon ? target : next;
    }

    /// <summary>Progress from 0 to 1 of an animation that has run for <paramref name="elapsed"/> seconds.</summary>
    public static double Progress(double elapsed, double duration) =>
        duration <= 0 ? 1 : Math.Clamp(elapsed / duration, 0, 1);

    /// <summary>Cubic ease-out: fast start, long soft landing.</summary>
    public static double OutCubic(double progress)
    {
        var rest = 1 - Math.Clamp(progress, 0, 1);
        return 1 - rest * rest * rest;
    }

    /// <summary>Linear blend from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static double Lerp(double from, double to, double amount) => from + (to - from) * amount;
}

/// <summary>
/// Turns the irregular callbacks of a frame source into steps of bounded size and rate: callbacks that arrive too soon are
/// skipped without losing their time, and one long gap is cut to <see cref="Motion.MaxStepSeconds"/>.
/// </summary>
public sealed class FramePacer(double minStepSeconds = Motion.MinStepSeconds, double maxStepSeconds = Motion.MaxStepSeconds)
{
    private double _last;

    /// <summary>Starts counting from <paramref name="nowSeconds"/> (call when the first animation begins).</summary>
    public void Reset(double nowSeconds) => _last = nowSeconds;

    /// <summary>True when a step is due; <paramref name="seconds"/> is the time it covers.</summary>
    public bool TryStep(double nowSeconds, out double seconds)
    {
        var elapsed = nowSeconds - _last;
        if (elapsed < minStepSeconds)
        {
            seconds = 0;
            return false;
        }
        _last = nowSeconds;
        seconds = Math.Min(maxStepSeconds, elapsed);
        return true;
    }
}
