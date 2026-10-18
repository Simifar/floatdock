namespace FloatDock.App;

/// <summary>The dock's side of the app menu: its button. The menu itself, its shortcut and what it may ask of the docks are in <see cref="DockHost"/>.</summary>
public sealed partial class DockWindow
{
    /// <summary>Opens the app menu over this dock's display, or closes it when it is open.</summary>
    private void ToggleLauncher() => Host.ToggleLauncher(this);
}
