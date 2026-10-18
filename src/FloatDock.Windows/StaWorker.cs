using System.Collections.Concurrent;
using FloatDock.Core;

namespace FloatDock.Windows;

/// <summary>
/// One background STA thread for shell COM work: shell extensions behind icons and folders expect an STA,
/// and this keeps that work off the UI thread.
/// </summary>
public sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try { completion.SetResult(work()); }
                catch (Exception ex) { completion.SetException(ex); }
            });
        }
        catch (InvalidOperationException ex) { completion.SetException(ex); }
        return completion.Task;
    }

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try { work(); }
            catch (Exception ex) { LocalLog.Write("Error", "sta-worker", ex); }
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}
