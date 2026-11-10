using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Win2DS5.App.Theming;
using Win2DS5.Core;
using Win2DS5.DualSense;

namespace Win2DS5.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private string _signature = "";

    public DevicesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public event Action? EditRequested;

    public void Refresh()
    {
        var inputs = DualSenseConnection.List();
        var outputs = App.Runtime.Document.Outputs;
        var snapshot = App.Runtime.Capture();
        string selected = App.Runtime.ActiveInputKey;
        string signature = selected + "|" + snapshot.BatteryPercent + (snapshot.Charging ? "+" : "") + "|"
            + string.Join(",", inputs.Select(item => item.Path + ":" + App.Runtime.Document.ProfileFor(item.Path).Id)) + "|"
            + string.Join(",", outputs.Select(output => output.Id + output.Name + output.Slot + string.Join("/", ConfigWarnings.ProfileNamesUsing(App.Runtime.Document, output.Id)))) + "|"
            + string.Join("|", App.Runtime.Warnings.Select(warning => warning.Message));
        if (signature == _signature)
            return;
        _signature = signature;
        Inputs.Children.Clear();
        InputEmpty.Visibility = inputs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var input in inputs)
        {
            bool on = input.Path == selected;
            string path = input.Path;
            bool live = on && snapshot.Connected;
            var card = StudioCard(
                on,
                "CONTROLLER",
                input.Name,
                input.Bluetooth ? "\uE702" : "\uECF0",
                true,
                live ? Math.Clamp(snapshot.BatteryPercent, 0, 100) / 100.0 : 1,
                live && snapshot.BatteryPercent <= 15 && !snapshot.Charging,
                live ? $"{snapshot.BatteryPercent}%" + (snapshot.Charging ? " ⚡" : "") : input.Bluetooth ? "Bluetooth" : "USB",
                "Profile  ·  " + App.Runtime.Document.ProfileFor(path).Name,
                false,
                null);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;
            card.Tapped += (_, _) =>
            {
                App.Runtime.SelectInput(path);
                _signature = "";
                Refresh();
                EditRequested?.Invoke();
            };
            ToolTipService.SetToolTip(card, on ? "Editing this controller" : "Edit this controller");
            Inputs.Children.Add(new HandHost(card));
        }

        Outputs.Children.Clear();
        OutputEmpty.Visibility = outputs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var output in outputs)
        {
            string outputId = output.Id;
            Outputs.Children.Add(OutputTile(output, () =>
            {
                App.Runtime.RemoveOutput(outputId);
                _signature = "";
                Refresh();
            }));
        }

        StyleAdd(AddMouse, OutputDeviceKind.Mouse, "Mouse");
        StyleAdd(AddKeyboard, OutputDeviceKind.Keyboard, "Keyboard");
        StyleAdd(AddDs4, OutputDeviceKind.DualShock4, "DS4");
        StyleAdd(AddXbox, OutputDeviceKind.Xbox, "XBOX");
    }

    private void OnAddMouse(object sender, RoutedEventArgs e) => Add(OutputDeviceKind.Mouse);

    private void OnAddKeyboard(object sender, RoutedEventArgs e) => Add(OutputDeviceKind.Keyboard);

    private void OnAddDs4(object sender, RoutedEventArgs e) => Add(OutputDeviceKind.DualShock4);

    private void OnAddXbox(object sender, RoutedEventArgs e) => Add(OutputDeviceKind.Xbox);

    private void Add(OutputDeviceKind kind)
    {
        App.Runtime.AddOutput(kind);
        _signature = "";
        Refresh();
    }

    private void StyleAdd(Button button, OutputDeviceKind kind, string label)
    {
        var outputs = App.Runtime.Document.Outputs;
        int count = OutputCatalog.Count(outputs, kind);
        int limit = OutputCatalog.Limit(kind);
        bool open = OutputCatalog.CanAdd(outputs, kind);
        button.IsEnabled = open;
        button.Content = open
            ? $"Add {label}"
            : OutputCatalog.IsPad(kind) && count < limit
                ? $"{label}  ·  {OutputCatalog.PadLimit} pads"
                : $"{label}  {count}/{limit}";
        button.Style = (Style)Application.Current.Resources["QuietButtonStyle"];
        button.Opacity = open ? 1 : 0.45;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTipService.SetToolTip(button, open ? $"Add {label}" : $"{label} is at its limit");
    }

    private static Border OutputTile(OutputDevice output, Action remove)
    {
        string footer = OutputCatalog.IsPad(output.Kind)
            ? $"{OutputCatalog.KindLabel(output.Kind)} · Slot {output.Slot}"
            : OutputCatalog.KindLabel(output.Kind);

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = Theme.Brush("PanelAltBrush"),
            BorderBrush = Theme.Brush("LineBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new FontIcon
            {
                Glyph = Glyph(output.Kind),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("MutedBrush"),
            },
        };
        grid.Children.Add(icon);

        var body = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock
        {
            Text = output.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        body.Children.Add(OutputCatalog.IsPad(output.Kind) ? SlotLights(output) : ReadyLight());
        body.Children.Add(new TextBlock
        {
            Text = footer,
            FontSize = 11,
            Foreground = Theme.Brush("MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var close = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
        };
        ToolTipService.SetToolTip(close, "Remove");
        close.Click += (_, _) => remove();
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        card.Child = grid;
        return card;
    }

    private static StackPanel ReadyLight()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Theme.Brush("SuccessBrush"),
        });
        row.Children.Add(new TextBlock
        {
            Text = "Ready",
            FontSize = 11,
            Foreground = Theme.Brush("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static StackPanel SlotLights(OutputDevice output)
    {
        int limit = Math.Max(1, OutputCatalog.Limit(output.Kind));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (int slot = 1; slot <= limit; slot++)
        {
            bool on = slot == output.Slot;
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = Theme.Brush(on ? "SuccessBrush" : "MutedBrush"),
                Opacity = on ? 1 : 0.28,
            };
            ToolTipService.SetToolTip(dot, on ? $"Slot {output.Slot} of {limit}" : $"Slot {slot}");
            row.Children.Add(dot);
        }

        return row;
    }

    private static Border StudioCard(bool selected, string eyebrow, string title, string glyph, bool accentIcon, double fill, bool warn, string meter, string footer, bool caution, Button? action)
    {
        var card = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            BorderBrush = Theme.Brush(selected ? "AccentBrush" : "LineBrush"),
            BorderThickness = new Thickness(selected ? 1.5 : 1),
        };
        var root = new StackPanel { Spacing = 10 };
        var header = new Grid();
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = eyebrow,
            Style = (Style)Application.Current.Resources["EyebrowText"],
            Foreground = Theme.Brush("MutedBrush"),
        });
        titles.Children.Add(new TextBlock
        {
            Text = title,
            Margin = new Thickness(0, 4, 28, 0),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        header.Children.Add(titles);
        header.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = Theme.Brush(accentIcon ? "AccentTextBrush" : "MutedBrush"),
        });
        root.Children.Add(header);

        var meterRow = new Grid { ColumnSpacing = 8 };
        meterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        double fraction = Math.Clamp(fill, 0, 1);
        var fillHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        fillHost.Children.Add(new Rectangle
        {
            RadiusX = 3,
            RadiusY = 3,
            Fill = Theme.Brush(warn ? "AmberBrush" : "AccentGradientBrush"),
        });
        var track = new Grid
        {
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Background = Theme.Brush("PanelAltBrush"),
                    BorderBrush = Theme.Brush("LineBrush"),
                    BorderThickness = new Thickness(1),
                },
                fillHost,
            },
        };
        track.SizeChanged += (_, _) => fillHost.Width = Math.Max(0, track.ActualWidth * fraction);
        meterRow.Children.Add(track);
        var meterText = new TextBlock
        {
            Text = meter,
            FontSize = 11,
            Foreground = Theme.Brush("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(meterText, 1);
        meterRow.Children.Add(meterText);
        root.Children.Add(meterRow);

        var foot = new Grid { ColumnSpacing = 8 };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.Children.Add(new TextBlock
        {
            Text = footer,
            FontSize = 11,
            Foreground = Theme.Brush(caution ? "AmberBrush" : "MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (action != null)
        {
            Grid.SetColumn(action, 1);
            foot.Children.Add(action);
        }

        root.Children.Add(foot);
        card.Child = root;
        return card;
    }

    private static string Glyph(OutputDeviceKind kind) => kind switch
    {
        OutputDeviceKind.Mouse => "\uE962",
        OutputDeviceKind.Keyboard => "\uE765",
        _ => "\uE7FC",
    };
}

public sealed class HandHost : UserControl
{
    public HandHost(UIElement child)
    {
        Content = child;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}

public sealed class CardWrap : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(CardWrap), new PropertyMetadata(280.0, OnLayoutChanged));

    public static readonly DependencyProperty MaxItemWidthProperty = DependencyProperty.Register(
        nameof(MaxItemWidth), typeof(double), typeof(CardWrap), new PropertyMetadata(460.0, OnLayoutChanged));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CardWrap), new PropertyMetadata(10.0, OnLayoutChanged));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double MaxItemWidth
    {
        get => (double)GetValue(MaxItemWidthProperty);
        set => SetValue(MaxItemWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private static void OnLayoutChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CardWrap)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? MaxItemWidth : available.Width;
        var (itemWidth, columns) = Fit(width, Children.Count);
        double height = MeasureRows(itemWidth, columns);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (itemWidth, columns) = Fit(finalSize.Width, Children.Count);
        double y = 0;
        int index = 0;
        while (index < Children.Count)
        {
            int count = Math.Min(columns, Children.Count - index);
            double rowHeight = 0;
            for (int i = 0; i < count; i++)
            {
                var child = Children[index + i];
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            for (int i = 0; i < count; i++)
                Children[index + i].Arrange(new Rect(i * (itemWidth + Gap), y, itemWidth, rowHeight));
            y += rowHeight;
            index += count;
            if (index < Children.Count)
                y += Gap;
        }

        return new Size(finalSize.Width, y);
    }

    private double MeasureRows(double itemWidth, int columns)
    {
        if (Children.Count == 0)
            return 0;
        double y = 0;
        int index = 0;
        while (index < Children.Count)
        {
            int count = Math.Min(columns, Children.Count - index);
            double rowHeight = 0;
            for (int i = 0; i < count; i++)
            {
                var child = Children[index + i];
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            y += rowHeight;
            index += count;
            if (index < Children.Count)
                y += Gap;
        }

        return y;
    }

    private (double Width, int Columns) Fit(double available, int count)
    {
        if (count <= 0)
            return (Math.Max(0, available), 1);
        double min = Math.Max(1, MinItemWidth);
        double max = Math.Max(min, MaxItemWidth);
        double gap = Math.Max(0, Gap);
        if (available <= min)
            return (Math.Max(0, available), 1);

        int columns = Math.Max(1, (int)Math.Floor((available + gap) / (min + gap)));
        columns = Math.Min(columns, count);
        double width = (available - gap * (columns - 1)) / columns;
        while (columns < count && width > max)
        {
            columns++;
            width = (available - gap * (columns - 1)) / columns;
        }

        if (width > max)
            width = max;
        return (width, columns);
    }
}
