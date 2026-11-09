using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Win2DS5.App.Runtime;
using Win2DS5.App.Theming;
using Win2DS5.Core;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Win2DS5.App.Controls;

public sealed partial class ControllerDiagram : UserControl
{
    private const double CanvasWidth = 800;
    private const double CanvasHeight = 560;
    private const double StickTravel = 14;

    private const string BodyData =
        "M 400,65 C 330,63 270,64 253,69 C 220,72 170,74 139,81 C 105,88 80,98 69,112 C 45,145 32,185 34,226 " +
        "C 36,290 30,345 34,395 C 38,445 44,480 58,500 C 74,524 96,532 116,528 C 142,524 160,516 174,502 " +
        "C 194,480 206,452 220,428 C 230,410 240,395 253,387 C 290,372 345,370 400,370 C 455,370 510,372 547,387 " +
        "C 560,395 570,410 580,428 C 594,452 606,480 626,502 C 640,516 658,524 684,528 C 704,532 726,524 742,500 " +
        "C 756,480 762,445 766,395 C 770,345 764,290 766,226 C 768,185 755,145 731,112 C 720,98 695,88 661,81 " +
        "C 630,74 580,72 547,69 C 530,64 470,63 400,65 Z";

    private const string PlateData =
        "M 272,224 L 528,224 C 560,226 586,242 592,272 C 600,318 580,360 547,387 C 510,372 455,370 400,370 " +
        "C 345,370 290,372 253,387 C 220,360 200,318 208,272 C 214,242 240,226 272,224 Z";

    private const string TouchpadData =
        "M 272,70 L 528,70 C 536,70 540,75 539,83 L 530,205 C 529,216 522,222 511,222 L 289,222 " +
        "C 278,222 271,216 270,205 L 261,83 C 260,75 264,70 272,70 Z";

    private const string L1Data =
        "M 118,92 C 140,74 190,64 240,63 C 247,63 250,68 249,74 L 248,80 C 205,80 168,88 136,102 C 126,106 112,100 118,92 Z";

    private const string L2Data =
        "M 140,66 C 142,40 162,22 192,18 C 216,15 236,22 240,38 L 242,62 C 205,61 172,62 140,66 Z";

    private const string ArmData = "M -15,-46 Q -15,-56 -5,-56 L 5,-56 Q 15,-56 15,-46 L 15,-26 L 0,-11 L -15,-26 Z";
    private const string ArrowData = "M -5.5,-36 L 5.5,-36 L 0,-43 Z";

    private static readonly Regex Tokens = new(@"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)", RegexOptions.Compiled);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly Canvas _canvas = new() { Width = CanvasWidth, Height = CanvasHeight };
    private readonly Dictionary<ControlId, PadPart> _parts = [];
    private readonly HashSet<ControlId> _down = [];
    private readonly RadialGradientBrush _halo = new();
    private readonly List<Glow> _modeGlows = [];
    private readonly List<Path> _lightbars = [];
    private readonly List<Grid> _lightbarGlows = [];
    private Path _edge = null!;
    private Grid _edgeGlow = null!;
    private Path _playerLed = null!;
    private Grid _playerLedGlow = null!;
    private TouchDot _touch0 = null!;
    private TouchDot _touch1 = null!;
    private Rect _touchArea;
    private ControlId _selected = ControlId.Cross;
    private int _mode = -1;

    public ControllerDiagram()
    {
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas };
        Build();
        Theme.Changed += RefreshTheme;
        RefreshTheme();
        Select(_selected);
        SetMode(false, true);
    }

    public event Action<ControlId>? ControlChosen;
    public event Action<ControlId?>? HoverChanged;

    public void Select(ControlId id)
    {
        _selected = id;
        foreach (var part in _parts.Values)
        {
            bool selected = part.Id == id;
            if (part.Selected == selected)
                continue;
            part.Selected = selected;
            part.Update();
        }
    }

    public void Apply(RuntimeSnapshot snapshot)
    {
        _down.Clear();
        foreach (var id in snapshot.Pressed)
            _down.Add(id);
        foreach (var part in _parts.Values)
        {
            bool down = _down.Contains(part.Id);
            if (part.Pressed == down)
                continue;
            part.Pressed = down;
            part.Update();
        }

        SetLevel(_parts[ControlId.L2], snapshot.L2);
        SetLevel(_parts[ControlId.R2], snapshot.R2);
        SetStick(_parts[ControlId.L3], snapshot.LeftX, snapshot.LeftY, snapshot.Connected);
        SetStick(_parts[ControlId.R3], snapshot.RightX, snapshot.RightY, snapshot.Connected);
        _touch0.Set(snapshot.TouchActive, snapshot.TouchX, snapshot.TouchY, _touchArea);
        _touch1.Set(snapshot.Touch1Active, snapshot.Touch1X, snapshot.Touch1Y, _touchArea);
        SetMode(snapshot.Connected, snapshot.DesktopEnabled);
    }

    private void Build()
    {
        var halo = new Ellipse
        {
            Width = 820,
            Height = 520,
            Fill = _halo,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(halo, -10);
        Canvas.SetTop(halo, 40);
        _canvas.Children.Add(halo);

        var l2 = AddPart(ControlId.L2, L2Data, Key.Shell, new Point(191, 42), "L2");
        var r2 = AddPart(ControlId.R2, Mirror(L2Data), Key.Shell, new Point(609, 42), "R2");
        foreach (var trigger in new[] { l2, r2 })
        {
            trigger.ActiveStrength = 0.2;
            AddLevel(trigger);
        }

        AddPart(ControlId.L2Full, RoundRect(78, 30, 52, 22, 11), Key.Shell, new Point(104, 41), "FULL", 9);
        AddPart(ControlId.R2Full, RoundRect(670, 30, 52, 22, 11), Key.Shell, new Point(696, 41), "FULL", 9);

        var shadow = GlowLayer(BodyData, 34, () => Theme.IsDark ? Colors.Black : Color.FromArgb(255, 40, 52, 72), 6);
        shadow.Host.Opacity = 1;
        shadow.Glow.SetOpacity(Theme.IsDark ? 0.9f : 0.35f);
        Theme.Changed += () => shadow.Glow.SetOpacity(Theme.IsDark ? 0.9f : 0.35f);
        shadow.Shape.Fill = Theme.Brush("PadShellBrush");

        var edge = GlowLayer(BodyData, 18, ModeColor);
        _edgeGlow = edge.Host;
        Motion.Fade(_edgeGlow, 260);
        _modeGlows.Add(edge.Glow);
        edge.Shape.Fill = Theme.Brush("PadShellBrush");

        var body = Layer(BodyData);
        body.Shape.Fill = Theme.Brush("PadShellBrush");
        body.Shape.Stroke = Theme.Brush("PadEdgeBrush");
        body.Shape.StrokeThickness = 1.2;

        var rim = Layer(BodyData);
        rim.Shape.Stroke = Theme.Brush("AccentBrush");
        rim.Shape.StrokeThickness = 1.4;
        _edge = rim.Shape;
        Motion.Fade(rim.Root, 260);

        var plate = Layer(PlateData);
        plate.Shape.Fill = Theme.Brush("PadPlateBrush");

        foreach (var data in new[] { "M 250,94 L 259,200", "M 550,94 L 541,200" })
        {
            var bar = GlowLayer(data, 14, ModeColor);
            bar.Shape.StrokeThickness = 4;
            bar.Shape.StrokeStartLineCap = PenLineCap.Round;
            bar.Shape.StrokeEndLineCap = PenLineCap.Round;
            Motion.Fade(bar.Host, 260);
            _lightbars.Add(bar.Shape);
            _lightbarGlows.Add(bar.Host);
            _modeGlows.Add(bar.Glow);
        }

        foreach (var cx in new[] { 284.0, 516.0 })
        {
            var well = Layer(Circle(cx, 300, 47));
            well.Shape.Fill = Theme.Brush("WellBrush");
            well.Shape.Stroke = Theme.Brush("PlateKeyLineBrush");
            well.Shape.StrokeThickness = 1;
        }

        var touchpad = AddPart(ControlId.TouchpadClick, TouchpadData, Key.Touch, null, null);
        touchpad.ActiveStrength = 0.32;
        BuildTouchLayer(touchpad);

        AddPart(ControlId.L1, L1Data, Key.Shell, new Point(190, 75), "L1");
        AddPart(ControlId.R1, Mirror(L1Data), Key.Shell, new Point(610, 75), "R1");

        var create = AddPart(ControlId.Create, RoundRect(226, 84, 15, 32, 7.5), Key.Shell, null, null);
        AddDecor(create, "M 230.5,95 L 230.5,105 M 233.5,93 L 233.5,107 M 236.5,95 L 236.5,105", stroke: true, 1.3);
        var options = AddPart(ControlId.Options, RoundRect(559, 84, 15, 32, 7.5), Key.Shell, null, null);
        AddDecor(options, "M 563,95.5 L 570,95.5 M 563,100 L 570,100 M 563,104.5 L 570,104.5", stroke: true, 1.3);

        var dpad = new Point(184, 196);
        var arms = new[] { ControlId.DPadUp, ControlId.DPadRight, ControlId.DPadDown, ControlId.DPadLeft };
        for (int q = 0; q < 4; q++)
        {
            var arm = AddPart(arms[q], Rotate(ArmData, dpad, q), Key.Shell, null, null);
            AddDecor(arm, Rotate(ArrowData, dpad, q), stroke: false);
        }

        var face = new Point(616, 196);
        const double gap = 54;
        AddFace(ControlId.Triangle, face.X, face.Y - gap, (x, y) => Inv($"M {x},{y - 9} L {x + 8.5},{y + 5.5} L {x - 8.5},{y + 5.5} Z"));
        AddFace(ControlId.Circle, face.X + gap, face.Y, (x, y) => Circle(x, y, 8.5));
        AddFace(ControlId.Cross, face.X, face.Y + gap, (x, y) => Inv($"M {x - 7.5},{y - 7.5} L {x + 7.5},{y + 7.5} M {x + 7.5},{y - 7.5} L {x - 7.5},{y + 7.5}"));
        AddFace(ControlId.Square, face.X - gap, face.Y, (x, y) => RoundRect(x - 7.5, y - 7.5, 15, 15, 1.5));

        foreach (var (id, cx) in new[] { (ControlId.L3, 284.0), (ControlId.R3, 516.0) })
        {
            var stick = AddPart(id, Circle(cx, 300, 32), Key.Plate, new Point(cx, 300), null);
            AddDecor(stick, Circle(cx, 300, 23.5), stroke: true, 1.4, ink: false);
            AddDecor(stick, Circle(cx, 300, 3), stroke: false);
            stick.Pulse.StrokeThickness = 2.5;
        }

        AddPart(ControlId.Ps, Circle(400, 300, 16), Key.Plate, new Point(400, 300), "PS", 10);
        AddPart(ControlId.Mute, RoundRect(382, 328, 36, 12, 6), Key.Plate, null, null);

        for (int row = 0; row < 2; row++)
        {
            for (int i = 0; i < 7; i++)
            {
                var dot = new Ellipse { Width = 2.6, Height = 2.6, Fill = Theme.Brush("PlateKeyLineBrush"), IsHitTestVisible = false };
                Canvas.SetLeft(dot, 379 + (i * 7) - 1.3 + (row * 3.5));
                Canvas.SetTop(dot, 249 + (row * 7));
                _canvas.Children.Add(dot);
            }
        }

        for (int i = 0; i < 5; i++)
        {
            if (i == 2)
            {
                var led = GlowLayer(RoundRect(397, 231, 6, 4, 1.5), 8, ModeColor);
                _playerLed = led.Shape;
                _playerLedGlow = led.Host;
                Motion.Fade(_playerLedGlow, 260);
                _modeGlows.Add(led.Glow);
                continue;
            }

            var off = new Rectangle { Width = 6, Height = 4, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.Brush("PlateKeyLineBrush"), IsHitTestVisible = false };
            Canvas.SetLeft(off, 383 + (i * 7));
            Canvas.SetTop(off, 231);
            _canvas.Children.Add(off);
        }
    }

    private void AddFace(ControlId id, double x, double y, Func<double, double, string> symbol)
    {
        var part = AddPart(id, Circle(x, y, 23), Key.Shell, null, null);
        AddDecor(part, symbol(x, y), stroke: true, 2.1);
    }

    private PadPart AddPart(ControlId id, string data, Key key, Point? labelAt, string? label, double labelSize = 11)
    {
        var bounds = Inflate(Bounds(data), 4);
        var root = new Grid
        {
            Width = bounds.Width,
            Height = bounds.Height,
            CenterPoint = new Vector3((float)bounds.Width / 2, (float)bounds.Height / 2, 0),
            ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) },
        };
        Canvas.SetLeft(root, bounds.X);
        Canvas.SetTop(root, bounds.Y);
        var local = Shift(data, bounds.X, bounds.Y);

        var glowHost = new Grid { Opacity = 0, IsHitTestVisible = false };
        Motion.Fade(glowHost, 160);
        var basePath = new Path
        {
            Data = Geo(local),
            Fill = Theme.Brush(key switch { Key.Plate => "PlateKeyBrush", Key.Touch => "TouchSurfaceBrush", _ => "PadKeyBrush" }),
            Stroke = Theme.Brush(key == Key.Plate ? "PlateKeyLineBrush" : "PadKeyLineBrush"),
            StrokeThickness = 1,
        };
        var active = new Path { Data = Geo(local), Fill = Theme.Brush("AccentGradientBrush"), Opacity = 0, IsHitTestVisible = false };
        Motion.Fade(active, 110);
        var ring = new Path { Data = Geo(local), Stroke = Theme.Brush("AccentBrush"), StrokeThickness = 2, Opacity = 0, IsHitTestVisible = false };
        Motion.Fade(ring, 180);
        var pulse = new Path
        {
            Data = Geo(local),
            Stroke = Theme.Brush("AccentBrush"),
            StrokeThickness = 2,
            Opacity = 0,
            IsHitTestVisible = false,
            CenterPoint = new Vector3((float)bounds.Width / 2, (float)bounds.Height / 2, 0),
        };

        root.Children.Add(glowHost);
        root.Children.Add(basePath);
        root.Children.Add(active);

        var part = new PadPart(id, root, glowHost, active, ring, pulse, bounds, local,
            Theme.Brush(key == Key.Plate ? "PlateInkBrush" : "PadInkBrush"));
        part.GlowHandle = new Glow(glowHost, basePath, key == Key.Touch ? 26 : 18);

        if (label != null && labelAt is { } at)
        {
            var text = new TextBlock
            {
                Text = label,
                FontSize = labelSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = labelSize < 10 ? 80 : 20,
                Foreground = part.Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var box = new Border
            {
                Width = 60,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(at.X - bounds.X - 30, at.Y - bounds.Y - 10, 0, 0),
                Child = text,
                IsHitTestVisible = false,
            };
            root.Children.Add(box);
            part.InkText.Add(text);
        }

        root.Children.Add(ring);
        root.Children.Add(pulse);

        root.PointerEntered += (_, _) =>
        {
            part.Hover = true;
            part.Update();
            HoverChanged?.Invoke(id);
        };
        root.PointerExited += (_, _) =>
        {
            part.Hover = false;
            part.PointerDown = false;
            part.Update();
            HoverChanged?.Invoke(null);
        };
        root.PointerPressed += (_, _) =>
        {
            part.PointerDown = true;
            part.Update();
        };
        root.PointerReleased += (_, _) =>
        {
            part.PointerDown = false;
            part.Update();
        };
        root.PointerCaptureLost += (_, _) =>
        {
            part.PointerDown = false;
            part.Update();
        };
        root.Tapped += (_, _) =>
        {
            Select(id);
            ControlChosen?.Invoke(id);
        };

        _canvas.Children.Add(root);
        _parts[id] = part;
        return part;
    }

    private static void AddDecor(PadPart part, string data, bool stroke, double thickness = 1.5, bool ink = true)
    {
        var path = new Path
        {
            Data = Geo(Shift(data, part.Bounds.X, part.Bounds.Y)),
            IsHitTestVisible = false,
        };
        var brush = ink ? part.Ink : Theme.Brush("PlateKeyLineBrush");
        if (stroke)
        {
            path.Stroke = brush;
            path.StrokeThickness = thickness;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }
        else
        {
            path.Fill = brush;
        }

        int index = part.Root.Children.IndexOf(part.Active) + 1;
        part.Root.Children.Insert(index, path);
        if (ink)
            part.InkShapes.Add((path, stroke));
    }

    private static void AddLevel(PadPart part)
    {
        var level = new Path
        {
            Data = Geo(part.LocalData),
            Fill = Theme.Brush("AccentGradientBrush"),
            IsHitTestVisible = false,
            Opacity = 0.9,
            Clip = new RectangleGeometry { Rect = new Rect(0, part.Bounds.Height, part.Bounds.Width, 0) },
        };
        part.Root.Children.Insert(part.Root.Children.IndexOf(part.Active), level);
        part.Level = level;
    }

    private void BuildTouchLayer(PadPart touchpad)
    {
        var b = touchpad.Bounds;
        _touchArea = new Rect(272 - b.X, 74 - b.Y, 256, 144);
        var layer = new Canvas { IsHitTestVisible = false };
        int index = touchpad.Root.Children.IndexOf(touchpad.Active) + 1;
        touchpad.Root.Children.Insert(index, layer);
        _touch0 = new TouchDot(layer);
        _touch1 = new TouchDot(layer);
    }

    private static void SetLevel(PadPart part, double level)
    {
        level = Math.Clamp(level, 0, 1);
        if (Math.Abs(level - part.Live) < 0.01)
            return;
        part.Live = level;
        if (part.Level?.Clip is RectangleGeometry clip)
        {
            double h = part.Bounds.Height * level;
            clip.Rect = new Rect(0, part.Bounds.Height - h, part.Bounds.Width, h);
        }

        part.Update();
    }

    private static void SetStick(PadPart part, double x, double y, bool connected)
    {
        if (!connected)
            x = y = 0;
        double magnitude = Math.Min(1, Math.Sqrt((x * x) + (y * y)));
        if (magnitude < 0.06)
        {
            x = y = 0;
            magnitude = 0;
        }

        var offset = new Vector3((float)(x * StickTravel), (float)(y * StickTravel), 0);
        if (Vector3.DistanceSquared(offset, part.Root.Translation) > 0.04f)
            part.Root.Translation = offset;
        double live = magnitude * 0.7;
        if (Math.Abs(live - part.Live) < 0.02)
            return;
        part.Live = live;
        part.Update();
    }

    private void SetMode(bool connected, bool desktop)
    {
        int mode = !connected ? 0 : desktop ? 2 : 1;
        if (mode == _mode)
            return;
        _mode = mode;
        var brush = Theme.Brush(mode == 1 ? "AmberBrush" : "AccentBrush");
        foreach (var bar in _lightbars)
            bar.Stroke = mode == 0 ? Theme.Brush("LineBrush") : brush;
        foreach (var glow in _lightbarGlows)
            glow.Opacity = mode == 0 ? 0 : 1;
        _edge.Stroke = brush;
        _edge.Opacity = mode switch { 2 => 0.55, 1 => 0.4, _ => 0 };
        _edgeGlow.Opacity = mode switch { 2 => 0.5, 1 => 0.3, _ => 0 };
        _playerLedGlow.Opacity = mode == 0 ? 0 : 1;
        _playerLed.Fill = mode == 0 ? Theme.Brush("PlateKeyLineBrush") : brush;

        foreach (var glow in _modeGlows)
            glow.Refresh();
    }

    private Color ModeColor() => _mode == 1 ? ((SolidColorBrush)Theme.Brush("AmberBrush")).Color : Theme.Accent;

    private void RefreshTheme()
    {
        var accent = Theme.Accent;
        _halo.GradientStops.Clear();
        _halo.GradientStops.Add(new GradientStop { Offset = 0, Color = Theme.Alpha(accent, Theme.IsDark ? (byte)0x2A : (byte)0x30) });
        _halo.GradientStops.Add(new GradientStop { Offset = 0.55, Color = Theme.Alpha(accent, Theme.IsDark ? (byte)0x0C : (byte)0x10) });
        _halo.GradientStops.Add(new GradientStop { Offset = 1, Color = Theme.Alpha(accent, 0) });
        foreach (var glow in _modeGlows)
            glow.Refresh();
    }

    private (Grid Root, Path Shape) Layer(string data)
    {
        var b = Inflate(Bounds(data), 4);
        var root = new Grid { Width = b.Width, Height = b.Height, IsHitTestVisible = false };
        Canvas.SetLeft(root, b.X);
        Canvas.SetTop(root, b.Y);
        var path = new Path { Data = Geo(Shift(data, b.X, b.Y)) };
        root.Children.Add(path);
        _canvas.Children.Add(root);
        return (root, path);
    }

    private (Grid Root, Grid Host, Path Shape, Glow Glow) GlowLayer(string data, float blur, Func<Color> color, float offsetY = 0)
    {
        var (root, path) = Layer(data);
        var host = new Grid { IsHitTestVisible = false };
        root.Children.Insert(0, host);
        var glow = new Glow(host, path, blur, 0.95f, color, offsetY);
        return (root, host, path, glow);
    }

    private static Geometry Geo(string data) =>
        (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    private static string Inv(FormattableString text) => FormattableString.Invariant(text);

    private static Rect Inflate(Rect r, double by) =>
        new(Math.Floor(r.X - by), Math.Floor(r.Y - by), Math.Ceiling(r.Width + (by * 2)), Math.Ceiling(r.Height + (by * 2)));

    private static Rect Bounds(string data)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        Map(data, p =>
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
            return p;
        });
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static string Shift(string data, double dx, double dy) => Map(data, p => new Point(p.X - dx, p.Y - dy));

    private static string Mirror(string data) => Map(data, p => new Point(CanvasWidth - p.X, p.Y));

    private static string Rotate(string data, Point center, int quarter) => Map(data, p =>
    {
        var r = quarter switch
        {
            1 => new Point(-p.Y, p.X),
            2 => new Point(-p.X, -p.Y),
            3 => new Point(p.Y, -p.X),
            _ => p,
        };
        return new Point(r.X + center.X, r.Y + center.Y);
    });

    private static string Map(string data, Func<Point, Point> map)
    {
        var builder = new StringBuilder(data.Length);
        double? pending = null;
        foreach (Match match in Tokens.Matches(data))
        {
            var token = match.Value;
            if (char.IsLetter(token[0]))
            {
                builder.Append(token).Append(' ');
                continue;
            }

            double value = double.Parse(token, Invariant);
            if (pending is not { } x)
            {
                pending = value;
                continue;
            }

            pending = null;
            var p = map(new Point(x, value));
            builder.Append(p.X.ToString("0.###", Invariant)).Append(',').Append(p.Y.ToString("0.###", Invariant)).Append(' ');
        }

        return builder.ToString().TrimEnd();
    }

    private static string RoundRect(double x, double y, double w, double h, double r)
    {
        double k = r * 0.5523;
        return Inv($"M {x + r},{y} L {x + w - r},{y} C {x + w - r + k},{y} {x + w},{y + r - k} {x + w},{y + r} ") +
               Inv($"L {x + w},{y + h - r} C {x + w},{y + h - r + k} {x + w - r + k},{y + h} {x + w - r},{y + h} ") +
               Inv($"L {x + r},{y + h} C {x + r - k},{y + h} {x},{y + h - r + k} {x},{y + h - r} ") +
               Inv($"L {x},{y + r} C {x},{y + r - k} {x + r - k},{y} {x + r},{y} Z");
    }

    private static string Circle(double cx, double cy, double r)
    {
        double k = r * 0.5523;
        return Inv($"M {cx - r},{cy} C {cx - r},{cy - k} {cx - k},{cy - r} {cx},{cy - r} ") +
               Inv($"C {cx + k},{cy - r} {cx + r},{cy - k} {cx + r},{cy} ") +
               Inv($"C {cx + r},{cy + k} {cx + k},{cy + r} {cx},{cy + r} ") +
               Inv($"C {cx - k},{cy + r} {cx - r},{cy + k} {cx - r},{cy} Z");
    }

    private enum Key
    {
        Shell,
        Plate,
        Touch,
    }

    private sealed class PadPart
    {
        private readonly Vector3Transition _grow = new() { Duration = TimeSpan.FromMilliseconds(420) };
        private readonly ScalarTransition _fade = new() { Duration = TimeSpan.FromMilliseconds(420) };
        private readonly float _pulseScale;
        private bool _wasDown;
        private bool _inkDown;

        public PadPart(ControlId id, Grid root, Grid glowHost, Path active, Path ring, Path pulse, Rect bounds, string localData, Brush ink)
        {
            Id = id;
            Root = root;
            GlowHost = glowHost;
            Active = active;
            Ring = ring;
            Pulse = pulse;
            Bounds = bounds;
            LocalData = localData;
            Ink = ink;
            _pulseScale = 1 + (float)(18 / Math.Max(bounds.Width, bounds.Height));
        }

        public ControlId Id { get; }
        public Grid Root { get; }
        public Grid GlowHost { get; }
        public Path Active { get; }
        public Path Ring { get; }
        public Path Pulse { get; }
        public Rect Bounds { get; }
        public Brush Ink { get; }
        public string LocalData { get; }
        public Glow? GlowHandle { get; set; }
        public Path? Level { get; set; }
        public List<(Path Path, bool Stroke)> InkShapes { get; } = [];
        public List<TextBlock> InkText { get; } = [];
        public double ActiveStrength { get; set; } = 1;
        public bool Pressed { get; set; }
        public bool PointerDown { get; set; }
        public bool Hover { get; set; }
        public bool Selected { get; set; }
        public double Live { get; set; }

        public void Update()
        {
            bool down = Pressed || PointerDown;
            float scale = down ? 0.93f : Hover ? 1.06f : 1f;
            var target = new Vector3(scale, scale, 1);
            if (Root.Scale != target)
                Root.Scale = target;
            double glow = down ? 1 : Math.Max(Selected ? 0.55 : Hover ? 0.32 : 0, Live);
            if (Math.Abs(GlowHost.Opacity - glow) > 0.005)
                GlowHost.Opacity = glow;
            double active = down ? ActiveStrength : 0;
            if (Active.Opacity != active)
                Active.Opacity = active;
            double ring = Selected ? 1 : 0;
            if (Ring.Opacity != ring)
                Ring.Opacity = ring;
            bool inkDown = down && ActiveStrength > 0.5;
            if (inkDown != _inkDown)
            {
                _inkDown = inkDown;
                var brush = inkDown ? Theme.Brush("AccentInkBrush") : Ink;
                foreach (var (path, stroke) in InkShapes)
                {
                    if (stroke)
                        path.Stroke = brush;
                    else
                        path.Fill = brush;
                }

                foreach (var text in InkText)
                    text.Foreground = brush;
            }

            if (down && !_wasDown)
                FirePulse();
            _wasDown = down;
        }

        private void FirePulse()
        {
            Pulse.OpacityTransition = null;
            Pulse.ScaleTransition = null;
            Pulse.Opacity = 0.9;
            Pulse.Scale = Vector3.One;
            Pulse.DispatcherQueue.TryEnqueue(() =>
            {
                Pulse.OpacityTransition = _fade;
                Pulse.ScaleTransition = _grow;
                Pulse.Opacity = 0;
                Pulse.Scale = new Vector3(_pulseScale, _pulseScale, 1);
            });
        }
    }

    private sealed class TouchDot
    {
        private readonly Grid _root;
        private bool _active;

        public TouchDot(Canvas layer)
        {
            _root = new Grid { Width = 18, Height = 18, Opacity = 0 };
            Motion.Fade(_root, 120);
            var host = new Grid();
            var dot = new Ellipse { Fill = Theme.Brush("AccentBrush") };
            var core = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)) };
            _root.Children.Add(host);
            _root.Children.Add(dot);
            _root.Children.Add(core);
            _ = new Glow(host, dot, 14);
            layer.Children.Add(_root);
        }

        public void Set(bool active, double x, double y, Rect area)
        {
            if (active)
            {
                var offset = new Vector3(
                    (float)(area.X + (Math.Clamp(x, 0, 1) * area.Width) - 9),
                    (float)(area.Y + (Math.Clamp(y, 0, 1) * area.Height) - 9),
                    0);
                if (Vector3.DistanceSquared(offset, _root.Translation) > 0.04f)
                    _root.Translation = offset;
            }

            if (active == _active)
                return;
            _active = active;
            _root.Opacity = active ? 1 : 0;
        }
    }
}
