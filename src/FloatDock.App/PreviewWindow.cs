using FloatDock.Core;
using FloatDock.Windows;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace FloatDock.App;

/// <summary>
/// A row of live previews, one per window of an app, shown above the dock. The picture is a DWM thumbnail that DWM
/// draws over a transparent placeholder in this window; the placeholder takes the click. The window is created once and reused.
/// </summary>
internal sealed class PreviewWindow : Window
{
    private const double CardWidth = 224, CardHeight = 164, Gap = 8, Pad = 10, Margin = 8;
    private readonly nint _hwnd;
    private readonly List<WindowThumbnail> _thumbnails = [];
    private readonly List<(WindowInfo Window, FrameworkElement Placeholder)> _cards = [];
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = Gap };
    private readonly Border _shelf;
    private bool _open;
    private bool _activatedByUser;
    private bool _closed;
    private double _scale = 1;

    public PreviewWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        SystemBackdrop = new TransparentBackdrop();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!WindowChrome.RemoveBorder(_hwnd)) LocalLog.Write("Warning", "dwm-border-unavailable");
        WindowChrome.HideFromSwitchers(_hwnd);
        if (!WindowChrome.EnableClientTransparency(_hwnd)) LocalLog.Write("Warning", "dwm-transparency-unavailable");
        _shelf = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(Pad), Child = _row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var root = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        root.Children.Add(_shelf);
        root.PointerEntered += (_, _) => PointerChanged?.Invoke(true);
        root.PointerExited += (_, _) => PointerChanged?.Invoke(false);
        root.LayoutUpdated += (_, _) => PlaceThumbnails();
        root.KeyDown += (_, args) =>
        {
            if (args.Key != global::Windows.System.VirtualKey.Escape) return;
            args.Handled = true;
            Hide();
        };
        Content = root;
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && _open && _activatedByUser) Dismiss(byFocus: true);
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _open = false;
            DisposeThumbnails();
        };
    }

    public bool IsOpen => _open;

    /// <summary>The pointer entered or left the previews, so the dock can keep them while it is over them.</summary>
    public event Action<bool>? PointerChanged;
    public event Action<WindowInfo>? WindowChosen;
    public event Action<WindowInfo>? WindowCloseRequested;
    /// <summary>The previews went away; true when another window took the focus.</summary>
    public event Action<bool>? Dismissed;

    /// <param name="anchorX">Screen pixel the previews are centered on.</param>
    /// <param name="bottom">Screen pixel row the previews end at.</param>
    /// <param name="activate">Take the focus, so a click elsewhere closes the previews (a click on the dock), instead of showing on hover.</param>
    public void Show(IReadOnlyList<WindowInfo> windows, int anchorX, int bottom, RectInt32 monitor, bool light, double scale, bool activate,
        Func<string, string> text)
    {
        if (_closed || windows.Count == 0) return;
        _scale = scale;
        DisposeThumbnails();
        _shelf.Background = new SolidColorBrush(light
            ? ColorHelper.FromArgb(240, 246, 247, 249)
            : ColorHelper.FromArgb(240, 32, 35, 42));
        // Only the shelf is themed: a themed window root paints an opaque background over the transparent window.
        _shelf.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        _row.Children.Clear();
        _cards.Clear();
        var fit = Math.Max(1, (int)((monitor.Width / scale - 2 * Margin - 2 * Pad + Gap) / (CardWidth + Gap)));
        foreach (var window in windows.Take(fit)) _row.Children.Add(CreateCard(window, light, text));
        var shown = Math.Min(windows.Count, fit);
        var widthDip = shown * CardWidth + (shown - 1) * Gap + 2 * Pad;
        var heightDip = CardHeight + 2 * Pad;
        var width = (int)Math.Ceiling(widthDip * scale);
        var height = (int)Math.Ceiling(heightDip * scale);
        var x = Math.Clamp(anchorX - width / 2, monitor.X + (int)(Margin * scale), Math.Max(monitor.X, monitor.X + monitor.Width - width - (int)(Margin * scale)));
        AppWindow.MoveAndResize(new RectInt32(x, bottom - height, width, height));
        _open = true;
        _activatedByUser = activate;
        if (activate)
        {
            AppWindow.Show();
            Activate();
            WindowChrome.Focus(_hwnd);
        }
        else AppWindow.Show(false);
        WindowChrome.BringToFront(_hwnd);
    }

    public void Hide() => Dismiss(byFocus: false);

    private void Dismiss(bool byFocus)
    {
        if (!_open) return;
        _open = false;
        DisposeThumbnails();
        if (!_closed) AppWindow.Hide();
        Dismissed?.Invoke(byFocus);
    }

    private UIElement CreateCard(WindowInfo window, bool light, Func<string, string> text)
    {
        var placeholder = new Border { Height = CardHeight - 40, Background = new SolidColorBrush(Colors.Transparent) };
        var title = new TextBlock { Text = window.Title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 24, 6) };
        var content = new StackPanel { Padding = new Thickness(8, 6, 8, 8) };
        content.Children.Add(title);
        content.Children.Add(placeholder);
        var activate = new Button
        {
            Content = content,
            Width = CardWidth,
            Height = CardHeight,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(light ? ColorHelper.FromArgb(20, 0, 0, 0) : ColorHelper.FromArgb(30, 255, 255, 255)),
            BorderThickness = new Thickness(0)
        };
        AutomationProperties.SetName(activate, window.Title);
        activate.Click += (_, _) => WindowChosen?.Invoke(window);
        var close = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 10 },
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        AutomationProperties.SetName(close, text("close") + ": " + window.Title);
        close.Click += (_, _) => WindowCloseRequested?.Invoke(window);
        var card = new Grid { Width = CardWidth, Height = CardHeight };
        card.Children.Add(activate);
        card.Children.Add(close);
        _cards.Add((window, placeholder));
        return card;
    }

    /// <summary>Registers the thumbnails once layout has given each placeholder its place; DWM then keeps drawing them.</summary>
    private void PlaceThumbnails()
    {
        if (!_open || _thumbnails.Count > 0 || _cards.Count == 0 || _cards.Any(card => card.Placeholder.ActualWidth <= 0)) return;
        var root = (UIElement)Content;
        foreach (var (window, placeholder) in _cards)
        {
            var bounds = placeholder.TransformToVisual(root).TransformBounds(new global::Windows.Foundation.Rect(0, 0, placeholder.ActualWidth, placeholder.ActualHeight));
            if (WindowThumbnail.Create(_hwnd, window.Handle) is not { } thumbnail) continue;
            _thumbnails.Add(thumbnail);
            thumbnail.Show((int)(bounds.Left * _scale), (int)(bounds.Top * _scale), (int)(bounds.Right * _scale), (int)(bounds.Bottom * _scale));
        }
    }

    private void DisposeThumbnails()
    {
        foreach (var thumbnail in _thumbnails) thumbnail.Dispose();
        _thumbnails.Clear();
    }
}
