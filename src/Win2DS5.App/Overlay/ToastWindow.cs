using System.Numerics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Win2DS5.App.Theming;
using WinRT.Interop;

namespace Win2DS5.App.Overlay;

public sealed class ToastWindow : Window
{
    private readonly TextBlock _text = new()
    {
        FontSize = 14,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Grid _root = new();
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _dot = new() { Width = 9, Height = 9 };
    private readonly Glow _glow;
    private readonly DispatcherTimer _timer;
    private bool _configured;

    public ToastWindow()
    {
        var dotHost = new Grid { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
        var glowHost = new Grid();
        dotHost.Children.Add(glowHost);
        dotHost.Children.Add(_dot);
        _glow = new Glow(glowHost, _dot, 10);
        _row.Children.Add(dotHost);
        _row.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "WIN2DS5", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 140, Foreground = Theme.Brush("AccentTextBrush") },
                _text,
            },
        });
        _root.Padding = new Thickness(18, 0, 18, 0);
        _root.Children.Add(_row);
        Content = _root;
        RefreshTheme();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (_configured)
                NativeOverlay.Hide(WindowNative.GetWindowHandle(this));
        };
    }

    public void RefreshTheme()
    {
        _root.RequestedTheme = Theme.ElementTheme;
        _root.Background = Theme.Brush("PanelGradientBrush");
        _dot.Fill = Theme.Brush("AccentBrush");
        _glow.Refresh();
        if (_configured)
            NativeOverlay.SetBorderColor(WindowNative.GetWindowHandle(this), Theme.Accent);
    }

    public void Show(string message)
    {
        _text.Text = message;
        var hwnd = WindowNative.GetWindowHandle(this);
        if (!_configured)
        {
            NativeOverlay.Configure(hwnd);
            NativeOverlay.SetBorderColor(hwnd, Theme.Accent);
            _configured = true;
        }

        bool amber = message.Contains("game", StringComparison.OrdinalIgnoreCase);
        _dot.Fill = Theme.Brush(amber ? "AmberBrush" : "AccentBrush");
        _glow.Refresh();

        var area = DisplayArea.Primary.WorkArea;
        double scale = NativeOverlay.Scale(hwnd);
        NativeOverlay.Place(hwnd, area.X + (32 * scale), area.Y + (32 * scale), 300, 60, 0.97);
        NativeOverlay.ShowWithoutActivating(hwnd);

        _row.OpacityTransition = null;
        _row.TranslationTransition = null;
        _row.Opacity = 0;
        _row.Translation = new Vector3(-10, 0, 0);
        DispatcherQueue.TryEnqueue(() =>
        {
            _row.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
            _row.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(240) };
            _row.Opacity = 1;
            _row.Translation = Vector3.Zero;
        });
        _timer.Stop();
        _timer.Start();
    }
}
