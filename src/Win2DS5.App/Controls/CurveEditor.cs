using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Win2DS5.App.Theming;
using Windows.Foundation;

namespace Win2DS5.App.Controls;

public sealed class CurveEditor : UserControl
{
    private const double Inset = 10;

    private readonly Grid _surface = new() { Height = 150 };
    private readonly Canvas _grid = new() { IsHitTestVisible = false };
    private readonly Polygon _area = new() { IsHitTestVisible = false };
    private readonly Polyline _line = new() { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
    private readonly Grid _knob = new() { Width = 14, Height = 14, IsHitTestVisible = false };
    private readonly TextBlock _value = new() { FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(12, 8, 0, 0) };
    private readonly LinearGradientBrush _areaBrush = new() { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
    private double _exponent = 1.3;

    public CurveEditor()
    {
        _areaBrush.GradientStops.Add(new GradientStop { Offset = 0 });
        _areaBrush.GradientStops.Add(new GradientStop { Offset = 1 });
        _area.Fill = _areaBrush;
        _line.Stroke = Theme.Brush("AccentBrush");

        var lineGlowHost = new Grid { IsHitTestVisible = false };
        var knobGlow = new Grid();
        var knobDot = new Ellipse { Fill = Theme.Brush("AccentBrush"), Stroke = Theme.Brush("PanelBrush"), StrokeThickness = 2 };
        _knob.Children.Add(knobGlow);
        _knob.Children.Add(knobDot);
        _knob.ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(140) };
        _knob.CenterPoint = new Vector3(7, 7, 0);
        var knobLayout = new Canvas { IsHitTestVisible = false };
        knobLayout.Children.Add(_knob);

        _surface.Background = Theme.Brush("PanelAltBrush");
        _surface.Children.Add(_grid);
        _surface.Children.Add(_area);
        _surface.Children.Add(lineGlowHost);
        _surface.Children.Add(_line);
        _surface.Children.Add(knobLayout);
        _surface.Children.Add(_value);
        _ = new Glow(lineGlowHost, _line, 10, 0.8f);
        _ = new Glow(knobGlow, knobDot, 10);

        _surface.PointerPressed += (_, e) =>
        {
            _surface.CapturePointer(e.Pointer);
            _knob.Scale = new Vector3(1.3f, 1.3f, 1);
            OnPointer(e);
        };
        _surface.PointerMoved += (_, e) => OnPointer(e);
        _surface.PointerReleased += (_, e) =>
        {
            _surface.ReleasePointerCapture(e.Pointer);
            _knob.Scale = Vector3.One;
        };
        _surface.PointerEntered += (_, _) => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        _surface.SizeChanged += (_, _) => Draw();
        Theme.Changed += Draw;

        Content = new Border
        {
            Child = _surface,
            CornerRadius = new CornerRadius(12),
            BorderBrush = Theme.Brush("LineBrush"),
            BorderThickness = new Thickness(1),
        };
    }

    public double Exponent
    {
        get => _exponent;
        set
        {
            _exponent = Math.Clamp(value, 0.35, 3.2);
            Draw();
        }
    }

    public event Action<double>? ExponentChanged;

    private void OnPointer(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_surface);
        if (!point.Properties.IsLeftButtonPressed && !point.IsInContact)
            return;
        double height = Math.Max(1, _surface.ActualHeight - (Inset * 2));
        double amount = 1 - ((point.Position.Y - Inset) / height);
        Exponent = 0.35 + (Math.Clamp(amount, 0, 1) * 2.85);
        ExponentChanged?.Invoke(_exponent);
    }

    private void Draw()
    {
        double width = Math.Max(10, _surface.ActualWidth - (Inset * 2));
        double height = Math.Max(10, _surface.ActualHeight - (Inset * 2));
        var accent = Theme.Accent;
        _areaBrush.GradientStops[0].Color = Theme.Alpha(accent, Theme.IsDark ? (byte)0x55 : (byte)0x45);
        _areaBrush.GradientStops[1].Color = Theme.Alpha(accent, 0);

        _grid.Children.Clear();
        var lineBrush = Theme.Brush("LineBrush");
        for (int i = 1; i < 4; i++)
        {
            double x = Inset + (width * i / 4);
            double y = Inset + (height * i / 4);
            _grid.Children.Add(new Line { X1 = x, X2 = x, Y1 = Inset, Y2 = Inset + height, Stroke = lineBrush, StrokeThickness = 1 });
            _grid.Children.Add(new Line { X1 = Inset, X2 = Inset + width, Y1 = y, Y2 = y, Stroke = lineBrush, StrokeThickness = 1 });
        }

        _grid.Children.Add(new Line
        {
            X1 = Inset,
            Y1 = Inset + height,
            X2 = Inset + width,
            Y2 = Inset,
            Stroke = Theme.Brush("MutedBrush"),
            StrokeThickness = 1,
            Opacity = 0.4,
            StrokeDashArray = [4, 4],
        });

        var points = new PointCollection();
        var area = new PointCollection { new Point(Inset, Inset + height) };
        for (int i = 0; i <= 48; i++)
        {
            double t = i / 48.0;
            var p = new Point(Inset + (t * width), Inset + height - (Math.Pow(t, _exponent) * height));
            points.Add(p);
            area.Add(p);
        }

        area.Add(new Point(Inset + width, Inset + height));
        _line.Points = points;
        _area.Points = area;
        double knobY = Inset + height - (Math.Pow(0.5, _exponent) * height);
        _knob.Translation = new Vector3((float)(Inset + (width / 2) - 7), (float)(knobY - 7), 0);
        _value.Text = $"CURVE  {_exponent:0.00}";
        _value.Foreground = Theme.Brush("AccentTextBrush");
    }
}
