using Microsoft.Win32;

namespace FloatDock.Windows;

public sealed class DisplayEvents : IDisposable
{
    public event Action? Changed;

    public DisplayEvents()
    {
        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.PowerModeChanged += OnPowerChanged;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Dispose();
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs args) => Changed?.Invoke();

    private void OnPowerChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume) Changed?.Invoke();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
    }
}
