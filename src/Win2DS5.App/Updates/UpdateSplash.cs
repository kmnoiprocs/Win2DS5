using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Win2DS5.App.Theming;
using Windows.Graphics;
using WinRT.Interop;

namespace Win2DS5.App.Updates;

public sealed class UpdateSplash : Window
{
    private readonly TextBlock _status = new()
    {
        Text = "Checking for a published build…",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
    };

    private IntPtr _icon;

    public UpdateSplash()
    {
        Title = "Win2DS5";
        var root = new Grid
        {
            Padding = new Thickness(24),
            RowSpacing = 16,
            RequestedTheme = Theme.ElementTheme,
            Background = Theme.Brush("CanvasGradientBrush"),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var tile = new Grid { Width = 30, Height = 30 };
        tile.Children.Add(new Rectangle
        {
            RadiusX = 9,
            RadiusY = 9,
            Fill = Theme.Brush("AccentGradientBrush"),
        });
        tile.Children.Add(new FontIcon
        {
            Glyph = "\uE7FC",
            FontSize = 15,
            Foreground = Theme.Brush("AccentInkBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "Win2DS5",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontFamily = (FontFamily)Application.Current.Resources["DisplayFont"],
        });
        titles.Children.Add(new TextBlock
        {
            Text = "Updates",
            Style = (Style)Application.Current.Resources["CaptionText"],
        });
        header.Children.Add(titles);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _status.Foreground = Theme.Brush("TextBrush");
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);

        var progress = new ProgressBar { IsIndeterminate = true, Height = 4 };
        Grid.SetRow(progress, 2);
        root.Children.Add(progress);
        Content = root;

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.Resize(new SizeInt32(460, 228));
        var area = DisplayArea.Primary.WorkArea;
        AppWindow.Move(new PointInt32(
            area.X + (area.Width - 460) / 2,
            area.Y + (area.Height - 228) / 2));
        ApplyIcon();
        Closed += (_, _) => LogoArt.Destroy(_icon);
    }

    public void Set(string message)
    {
        if (DispatcherQueue.HasThreadAccess)
            _status.Text = message;
        else
            DispatcherQueue.TryEnqueue(() => _status.Text = message);
    }

    private void ApplyIcon()
    {
        var color = Theme.Accent;
        _icon = LogoArt.CreateIcon(color.R, color.G, color.B, 256);
        AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(_icon));
    }
}
