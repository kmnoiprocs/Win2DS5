using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Win2DS5.App.Theming;
using Win2DS5.Core;

namespace Win2DS5.App.Controls;

public sealed class ZoneEditor : UserControl
{
    private const double Width0 = 360;
    private const double Height0 = 200;
    private const double Pad = 8;
    private const double Gap = 8;
    private const double Grab = 12;

    private readonly Canvas _canvas = new() { Width = Width0, Height = Height0, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Dictionary<TouchZone, (Rectangle Tile, TextBlock Label)> _tiles = [];
    private readonly List<(Grid Root, Rectangle Bar)> _handles = [];
    private readonly Grid _finger = new() { Width = 16, Height = 16, Opacity = 0, IsHitTestVisible = false };
    private string _drag = "";
    private string _hoverHandle = "";
    private TouchZone _selected = TouchZone.Center;
    private TouchZone? _hover;
    private double _topFraction = 0.22;
    private double _leftFraction = 0.33;
    private double _rightFraction = 0.33;
    private bool _fingerOn;

    public ZoneEditor()
    {
        foreach (var zone in new[] { TouchZone.Center, TouchZone.Top, TouchZone.Left, TouchZone.Right })
        {
            var tile = new Rectangle { RadiusX = 10, RadiusY = 10, StrokeThickness = 1 };
            var label = new TextBlock
            {
                Text = zone.ToString().ToUpperInvariant(),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 140,
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(tile);
            _canvas.Children.Add(label);
            _tiles[zone] = (tile, label);
        }

        foreach (var name in new[] { "top", "left", "right" })
        {
            var root = new Grid { IsHitTestVisible = false, Opacity = 0.65 };
            Motion.Fade(root, 140);
            var glow = new Grid();
            var bar = new Rectangle { RadiusX = 2, RadiusY = 2, Fill = Theme.Brush("AccentBrush") };
            root.Children.Add(glow);
            root.Children.Add(bar);
            _ = new Glow(glow, bar, 10);
            _canvas.Children.Add(root);
            _handles.Add((root, bar));
        }

        var fingerGlow = new Grid();
        var dot = new Ellipse { Fill = Theme.Brush("AccentBrush") };
        _finger.Children.Add(fingerGlow);
        _finger.Children.Add(dot);
        _finger.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(230, 255, 255, 255)) });
        _ = new Glow(fingerGlow, dot, 14);
        Motion.Fade(_finger, 120);
        _canvas.Children.Add(_finger);

        _canvas.PointerPressed += OnPressed;
        _canvas.PointerMoved += OnMoved;
        _canvas.PointerReleased += (_, e) =>
        {
            _drag = "";
            _canvas.ReleasePointerCapture(e.Pointer);
            Paint();
        };
        _canvas.PointerExited += (_, _) =>
        {
            _hover = null;
            _hoverHandle = "";
            Paint();
        };

        Content = new Border
        {
            Child = _canvas,
            CornerRadius = new CornerRadius(18),
            Background = Theme.Brush("TouchSurfaceBrush"),
            BorderBrush = Theme.Brush("PadKeyLineBrush"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Theme.Changed += Paint;
        LayoutZones();
    }

    public event Action<double, double, double>? ZonesChanged;
    public event Action<TouchZone>? ZoneSelected;

    public void SetFractions(double top, double left, double right)
    {
        _topFraction = top;
        _leftFraction = left;
        _rightFraction = right;
        LayoutZones();
    }

    public void SelectZone(TouchZone zone)
    {
        _selected = zone;
        Paint();
    }

    public void SetFinger(bool active, double x, double y)
    {
        if (active)
        {
            var offset = new Vector3((float)((x * Width0) - 8), (float)((y * Height0) - 8), 0);
            if (Vector3.DistanceSquared(offset, _finger.Translation) > 0.04f)
                _finger.Translation = offset;
        }

        if (active == _fingerOn)
            return;
        _fingerOn = active;
        _finger.Opacity = active ? 1 : 0;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas).Position;
        _drag = HandleAt(point.X, point.Y);
        if (_drag.Length > 0)
        {
            _canvas.CapturePointer(e.Pointer);
            Paint();
            return;
        }

        _selected = ZoneAt(point.X, point.Y);
        Paint();
        ZoneSelected?.Invoke(_selected);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas).Position;
        if (_drag.Length == 0 || !e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            var handle = HandleAt(point.X, point.Y);
            var zone = handle.Length > 0 ? (TouchZone?)null : ZoneAt(point.X, point.Y);
            ProtectedCursor = InputSystemCursor.Create(handle switch
            {
                "top" => InputSystemCursorShape.SizeNorthSouth,
                "left" or "right" => InputSystemCursorShape.SizeWestEast,
                _ => InputSystemCursorShape.Hand,
            });
            if (handle != _hoverHandle || zone != _hover)
            {
                _hoverHandle = handle;
                _hover = zone;
                Paint();
            }

            return;
        }

        if (_drag == "top")
            _topFraction = Math.Clamp(point.Y / Height0, 0.12, 0.45);
        else if (_drag == "left")
            _leftFraction = Math.Clamp(point.X / Width0, 0.12, 0.45);
        else if (_drag == "right")
            _rightFraction = Math.Clamp(1 - (point.X / Width0), 0.12, 0.45);
        LayoutZones();
        ZonesChanged?.Invoke(_topFraction, _leftFraction, _rightFraction);
    }

    private string HandleAt(double x, double y)
    {
        double topY = _topFraction * Height0;
        double leftX = _leftFraction * Width0;
        double rightX = (1 - _rightFraction) * Width0;
        if (Math.Abs(y - topY) < Grab)
            return "top";
        if (y > topY && Math.Abs(x - leftX) < Grab)
            return "left";
        if (y > topY && Math.Abs(x - rightX) < Grab)
            return "right";
        return "";
    }

    private TouchZone ZoneAt(double x, double y)
    {
        double nx = x / Width0, ny = y / Height0;
        if (ny < _topFraction)
            return TouchZone.Top;
        if (nx < _leftFraction)
            return TouchZone.Left;
        return nx > 1 - _rightFraction ? TouchZone.Right : TouchZone.Center;
    }

    private void LayoutZones()
    {
        double top = _topFraction * Height0;
        double left = _leftFraction * Width0;
        double right = (1 - _rightFraction) * Width0;
        double half = Gap / 2;
        Place(TouchZone.Top, Pad, Pad, Width0 - (Pad * 2), top - half - Pad);
        Place(TouchZone.Left, Pad, top + half, left - half - Pad, Height0 - top - half - Pad);
        Place(TouchZone.Center, left + half, top + half, right - left - Gap, Height0 - top - half - Pad);
        Place(TouchZone.Right, right + half, top + half, Width0 - right - half - Pad, Height0 - top - half - Pad);

        double lowerMid = top + ((Height0 - top) / 2);
        PlaceHandle(0, (Width0 / 2) - 24, top - 2, 48, 4);
        PlaceHandle(1, left - 2, lowerMid - 18, 4, 36);
        PlaceHandle(2, right - 2, lowerMid - 18, 4, 36);
        Paint();
    }

    private void Place(TouchZone zone, double x, double y, double w, double h)
    {
        var (tile, label) = _tiles[zone];
        tile.Width = Math.Max(0, w);
        tile.Height = Math.Max(0, h);
        Canvas.SetLeft(tile, x);
        Canvas.SetTop(tile, y);
        label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, x + ((w - label.DesiredSize.Width) / 2));
        Canvas.SetTop(label, y + ((h - label.DesiredSize.Height) / 2));
    }

    private void PlaceHandle(int index, double x, double y, double w, double h)
    {
        var (root, _) = _handles[index];
        root.Width = w;
        root.Height = h;
        Canvas.SetLeft(root, x);
        Canvas.SetTop(root, y);
    }

    private void Paint()
    {
        foreach (var (zone, (tile, label)) in _tiles)
        {
            bool selected = zone == _selected;
            bool hover = zone == _hover;
            tile.Fill = Theme.Brush(selected ? "AccentSoftBrush" : hover ? "HoverBrush" : "PanelAltBrush");
            tile.Stroke = Theme.Brush(selected ? "AccentHaloBrush" : "LineBrush");
            label.Foreground = Theme.Brush(selected ? "AccentTextBrush" : "MutedBrush");
        }

        string[] names = ["top", "left", "right"];
        for (int i = 0; i < _handles.Count; i++)
        {
            bool hot = _drag == names[i] || _hoverHandle == names[i];
            _handles[i].Root.Opacity = hot ? 1 : 0.55;
        }
    }
}
