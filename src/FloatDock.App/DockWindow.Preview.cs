using FloatDock.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FloatDock.App;

/// <summary>Window previews: shown after hovering an app for a moment, and on click when an app has several windows.</summary>
public sealed partial class DockWindow
{
    private PreviewWindow? _preview;
    private readonly DispatcherTimer _previewShowTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _previewHideTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private DockItem? _hoverItem;
    private DockItem? _previewItem;
    private bool _previewSticky;
    private bool _previewPointerInside;
    private int _previewCount;
    private long _previewLostFocusAt;

    private bool IsPreviewOpen => _preview is { IsOpen: true };

    private void InitializePreview()
    {
        _previewShowTimer.Tick += (_, _) =>
        {
            _previewShowTimer.Stop();
            if (_hoverItem is { } item && !IsPreviewOpen && _openMenus == 0) ShowPreview(item, sticky: false);
        };
        _previewHideTimer.Tick += (_, _) =>
        {
            _previewHideTimer.Stop();
            if (!_previewSticky && !_previewPointerInside && _hoverItem is null) HidePreview();
        };
    }

    private void DisposePreview()
    {
        _previewShowTimer.Stop();
        _previewHideTimer.Stop();
        _preview?.Close();
        _preview = null;
    }

    private void OnItemPointerEntered(DockItem item)
    {
        _hoverItem = item;
        _previewHideTimer.Stop();
        if (Host.DragKey is not null || _openMenus > 0 || item.Group is not { Windows.Count: > 0 })
        {
            _previewShowTimer.Stop();
            return;
        }
        // Moving along the row while previews are up switches them at once.
        if (IsPreviewOpen && !_previewSticky) ShowPreview(item, sticky: false);
        else if (!IsPreviewOpen) _previewShowTimer.Start();
    }

    private void OnItemPointerExited()
    {
        _hoverItem = null;
        _previewShowTimer.Stop();
        if (IsPreviewOpen && !_previewSticky) _previewHideTimer.Start();
    }

    private void ShowPreview(DockItem item, bool sticky)
    {
        if (_closing || item.Group is not { Windows.Count: > 0 } group) return;
        // The click that closed the previews by taking the focus must not open them again.
        if (sticky && ReferenceEquals(_previewItem, item) && Environment.TickCount64 - _previewLostFocusAt < 400) return;
        _previewShowTimer.Stop();
        _previewHideTimer.Stop();
        _preview ??= CreatePreview();
        var scale = Content.XamlRoot?.RasterizationScale ?? 1;
        global::Windows.Foundation.Point center;
        try
        {
            center = item.Element.TransformToVisual((UIElement)Content)
                .TransformPoint(new global::Windows.Foundation.Point(item.Element.ActualWidth / 2, 0));
        }
        catch (ArgumentException)
        {
            // The item left the row (its app closed) between the hover and now.
            return;
        }
        var position = AppWindow.Position;
        var monitor = DisplayRect;
        _previewItem = item;
        _previewSticky = sticky;
        _previewCount = group.Windows.Count;
        _preview.Show(group.Windows, position.X + (int)(center.X * scale), position.Y - (int)(6 * scale), monitor, _light, scale, sticky, T);
        UpdateHitRegion();
    }

    private PreviewWindow CreatePreview()
    {
        var preview = new PreviewWindow();
        preview.WindowChosen += window =>
        {
            HidePreview();
            // Always bring the window forward: choosing the already active one must not minimize it.
            Host.Commands.ActivateOrMinimize(window.Handle, 0);
        };
        preview.WindowCloseRequested += window =>
        {
            Host.Commands.Close(window.Handle);
            HidePreview();
        };
        preview.PointerChanged += inside =>
        {
            _previewPointerInside = inside;
            if (inside) _previewHideTimer.Stop();
            else if (!_previewSticky) _previewHideTimer.Start();
        };
        preview.Dismissed += byFocus =>
        {
            if (byFocus) _previewLostFocusAt = Environment.TickCount64;
            _previewPointerInside = false;
            UpdateHitRegion();
            ScheduleHide();
        };
        return preview;
    }

    private void HidePreview()
    {
        _previewShowTimer.Stop();
        _previewHideTimer.Stop();
        _preview?.Hide();
    }

    /// <summary>Keeps open previews in step with the app's windows.</summary>
    private void RefreshPreview()
    {
        if (!IsPreviewOpen || _previewItem is null) return;
        var count = _previewItem.Group?.Windows.Count ?? 0;
        if (count == 0) HidePreview();
        else if (count != _previewCount) ShowPreview(_previewItem, _previewSticky);
    }
}
