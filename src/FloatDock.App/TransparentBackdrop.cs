using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FloatDock.App;

/// <summary>
/// Fully transparent window backdrop, so only XAML content with its own background is visible.
/// The system backdrop slot takes a Windows.UI.Composition brush, which needs a system dispatcher queue on this thread.
/// </summary>
internal sealed partial class TransparentBackdrop : SystemBackdrop
{
    private static nint _dispatcherQueueController;
    private static global::Windows.UI.Composition.Compositor? _compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        EnsureSystemDispatcherQueue();
        _compositor ??= new global::Windows.UI.Composition.Compositor();
        connectedTarget.SystemBackdrop = _compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    private static void EnsureSystemDispatcherQueue()
    {
        if (_dispatcherQueueController != 0 || global::Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _dispatcherQueueController));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { internal int Size; internal int ThreadType; internal int ApartmentType; }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
