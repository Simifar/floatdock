using System.ComponentModel;
using FloatDock.Core;
using Microsoft.UI.Xaml.Media;

namespace FloatDock.App;

/// <summary>An app in the app menu grid. The icon arrives later and is shown over a placeholder glyph.</summary>
internal sealed class LauncherItem(AppEntry app) : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public AppEntry App { get; } = app;
    public string Name => App.Name;
    public bool IconRequested { get; set; }
    private bool _isNew;

    /// <summary>Recently first seen by FloatDock: the tile carries a "New" mark.</summary>
    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew == value) return;
            _isNew = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNew)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewVisibility)));
        }
    }

    public Microsoft.UI.Xaml.Visibility NewVisibility => _isNew ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>The localized word for the mark, added to the name a screen reader reads.</summary>
    public string NewLabel { get; set; } = string.Empty;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // The name UI Automation reports for the grid cell.
    public override string ToString() => _isNew ? Name + ", " + NewLabel : Name;
}
