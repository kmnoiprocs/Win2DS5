using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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

    private const string PlateData =
        "M 246,398 C 222,370 206,322 210,284 C 214,248 238,228 278,228 L 522,228 C 562,228 586,248 590,284 " +
        "C 594,322 578,370 554,398 C 530,376 455,372 400,372 C 345,372 270,376 246,398 Z";

    private const string TouchpadData =
        "M 278,78 L 522,78 Q 530,78 530,86 L 519,200 Q 518,212 506,212 L 294,212 Q 282,212 281,200 L 270,86 Q 270,78 278,78 Z";

    private const string LightbarData = "M 262,98 C 265,132 268,170 271,194 C 273,209 279,217 291,220";

    private const string ArmData = "M -15,-46 Q -15,-56 -5,-56 L 5,-56 Q 15,-56 15,-46 L 15,-26 L 0,-11 L -15,-26 Z";
    private const string ArrowData = "M -5.5,-36 L 5.5,-36 L 0,-43 Z";

    private static readonly Regex Tokens = new(@"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)", RegexOptions.Compiled);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly Point ShellTop = new(400, 72);

    private static readonly (Point C1, Point C2, Point End)[] LeftShell =
    [
        (new(350, 72), new(300, 72), new(262, 72)),
        (new(215, 72), new(160, 76), new(122, 86)),
        (new(88, 95), new(64, 114), new(52, 146)),
        (new(38, 182), new(34, 226), new(35, 280)),
        (new(36, 350), new(40, 430), new(56, 480)),
        (new(70, 520), new(98, 536), new(128, 532)),
        (new(156, 528), new(176, 510), new(190, 490)),
        (new(208, 462), new(226, 416), new(246, 398)),
        (new(270, 376), new(345, 372), new(400, 372)),
    ];

    private static readonly (Point P0, Point C1, Point C2, Point P3)[] ShoulderEdge =
    [
        (new(262, 72), new(215, 72), new(160, 76), new(122, 86)),
        (new(122, 86), new(88, 95), new(64, 114), new(52, 146)),
    ];

    private static readonly string BodyData = Symmetric(ShellTop, LeftShell);

    private readonly Canvas _canvas = new() { Width = CanvasWidth, Height = CanvasHeight };
    private readonly Dictionary<ControlId, PadPart> _parts = [];
    private readonly HashSet<ControlId> _down = [];
    private readonly RadialGradientBrush _halo = new();
    private readonly List<Glow> _modeGlows = [];
    private readonly List<Path> _lightbars = [];
    private readonly List<Path> _lightCores = [];
    private readonly List<Grid> _lightHosts = [];
    private readonly List<Glow> _lightGlows = [];
    private Path _body = null!;
    private Path _shade = null!;
    private Path _edge = null!;
    private Grid _edgeGlow = null!;
    private Path _playerLed = null!;
    private Grid _playerLedGlow = null!;
    private TouchDot _touch0 = null!;
    private TouchDot _touch1 = null!;
    private Rect _touchArea;
    private ControlId _selected = ControlId.Cross;
    private int _mode = -1;
    private Color? _layerLight;
    private string _shellColor = "";
    private string _shellImage = "";
    private DateTime _shellStamp;
    private string _light = "";
    private readonly LinearGradientBrush _shadeBrush = new()
    {
        StartPoint = new Point(0.5, 0),
        EndPoint = new Point(0.5, 1),
        GradientStops =
        {
            new GradientStop { Offset = 0, Color = Color.FromArgb(0x40, 255, 255, 255) },
            new GradientStop { Offset = 0.38, Color = Color.FromArgb(0x00, 255, 255, 255) },
            new GradientStop { Offset = 1, Color = Color.FromArgb(0x72, 0, 0, 0) },
        },
    };

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
        SetShell(snapshot.ShellColor, snapshot.ShellImage);
        SetLight(snapshot.Light);
    }

    public void SetShell(string? color, string? image)
    {
        color ??= "";
        image ??= "";
        string path = ShellFiles.Existing(image);
        var stamp = path.Length == 0 ? default : File.GetLastWriteTimeUtc(path);
        if (color == _shellColor && image == _shellImage && stamp == _shellStamp)
            return;
        _shellColor = color;
        _shellImage = image;
        _shellStamp = stamp;
        if (path.Length > 0)
        {
            var bitmap = new BitmapImage();
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path);
            _body.Fill = new ImageBrush
            {
                ImageSource = bitmap,
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };
            _shade.Opacity = 1;
            return;
        }

        if (Theme.TryHex(color, out var paint))
        {
            _body.Fill = new SolidColorBrush(paint);
            _shade.Opacity = 1;
            return;
        }

        _body.Fill = Theme.Brush("PadShellBrush");
        _shade.Opacity = 0;
    }

    public void SetLight(string? hex)
    {
        hex ??= "";
        if (hex == _light)
            return;
        _light = hex;
        _layerLight = Theme.TryHex(hex, out var color) ? color : null;
        PaintLightbars();
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
        _body = body.Shape;

        var shade = Layer(BodyData);
        shade.Shape.Fill = _shadeBrush;
        shade.Shape.Opacity = 0;
        shade.Shape.IsHitTestVisible = false;
        _shade = shade.Shape;

        var rim = Layer(BodyData);
        rim.Shape.Stroke = Theme.Brush("AccentBrush");
        rim.Shape.StrokeThickness = 1.4;
        _edge = rim.Shape;
        Motion.Fade(rim.Root, 260);

        var plate = Layer(PlateData);
        plate.Shape.Fill = Theme.Brush("PadPlateBrush");

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

        foreach (var data in new[] { LightbarData, Mirror(LightbarData) })
        {
            var soft = GlowLayer(data, 24, LightColor);
            soft.Glow.SetOpacity(1f);
            var tight = GlowLayer(data, 8, LightColor);
            foreach (var bar in new[] { soft, tight })
            {
                bar.Shape.StrokeThickness = 5.5;
                bar.Shape.StrokeStartLineCap = PenLineCap.Round;
                bar.Shape.StrokeEndLineCap = PenLineCap.Round;
                bar.Shape.StrokeLineJoin = PenLineJoin.Round;
                _lightbars.Add(bar.Shape);
                _lightHosts.Add(bar.Host);
                _lightGlows.Add(bar.Glow);
            }

            var core = Layer(data);
            core.Shape.Stroke = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
            core.Shape.StrokeThickness = 1.4;
            core.Shape.StrokeStartLineCap = PenLineCap.Round;
            core.Shape.StrokeEndLineCap = PenLineCap.Round;
            _lightCores.Add(core.Shape);
        }

        var bumper = Band(ShoulderEdge, 0.12, 1.42, 9, 7);
        AddPart(ControlId.L1, bumper.Data, Key.Shell, bumper.Mid, "L1", 10);
        AddPart(ControlId.R1, Mirror(bumper.Data), Key.Shell, Flip(bumper.Mid), "R1", 10);

        var trigger = Band(ShoulderEdge, 0.28, 1.2, 31, 12);
        var l2 = AddPart(ControlId.L2, trigger.Data, Key.Shell, trigger.Mid, "L2");
        var r2 = AddPart(ControlId.R2, Mirror(trigger.Data), Key.Shell, Flip(trigger.Mid), "R2");
        foreach (var part in new[] { l2, r2 })
        {
            part.ActiveStrength = 0.2;
            AddLevel(part);
        }

        var full = new Point(trigger.End.X - 44, trigger.End.Y - 12);
        AddPart(ControlId.L2Full, RoundRect(full.X - 24, full.Y - 10, 48, 20, 10), Key.Shell, full, "FULL", 9);
        AddPart(ControlId.R2Full, RoundRect(CanvasWidth - full.X - 24, full.Y - 10, 48, 20, 10), Key.Shell, Flip(full), "FULL", 9);

        var create = AddPart(ControlId.Create, RoundRect(227, 90, 14, 30, 7), Key.Shell, null, null, standout: true);
        AddDecor(create, "M 231,100 L 231,110 M 234,98 L 234,112 M 237,100 L 237,110", stroke: true, 1.3);
        var options = AddPart(ControlId.Options, RoundRect(559, 90, 14, 30, 7), Key.Shell, null, null, standout: true);
        AddDecor(options, "M 562.5,100.5 L 569.5,100.5 M 562.5,105 L 569.5,105 M 562.5,109.5 L 569.5,109.5", stroke: true, 1.3);

        const double cluster = 1.2;
        var dpad = new Point(156, 172);
        string arm = ScalePath(ArmData, cluster);
        string arrow = ScalePath(ArrowData, cluster);
        var arms = new[] { ControlId.DPadUp, ControlId.DPadRight, ControlId.DPadDown, ControlId.DPadLeft };
        for (int q = 0; q < 4; q++)
        {
            var piece = AddPart(arms[q], Rotate(arm, dpad, q), Key.Shell, null, null, standout: true);
            AddDecor(piece, Rotate(arrow, dpad, q), stroke: false);
        }

        var face = new Point(CanvasWidth - dpad.X, dpad.Y);
        const double faceRadius = 21;
        const double gap = 45;
        double mark = faceRadius / 23.0;
        double glyph = 8.5 * mark;
        double cross = 7.5 * mark;
        double rise = 9 * mark;
        double drop = 5.5 * mark;
        AddFace(ControlId.Triangle, face.X, face.Y - gap, (x, y) => Inv($"M {x},{y - rise} L {x + glyph},{y + drop} L {x - glyph},{y + drop} Z"), faceRadius);
        AddFace(ControlId.Circle, face.X + gap, face.Y, (x, y) => Circle(x, y, glyph), faceRadius);
        AddFace(ControlId.Cross, face.X, face.Y + gap, (x, y) => Inv($"M {x - cross},{y - cross} L {x + cross},{y + cross} M {x + cross},{y - cross} L {x - cross},{y + cross}"), faceRadius);
        AddFace(ControlId.Square, face.X - gap, face.Y, (x, y) => RoundRect(x - cross, y - cross, cross * 2, cross * 2, 1.6), faceRadius);

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

    private void AddFace(ControlId id, double x, double y, Func<double, double, string> symbol, double radius)
    {
        var part = AddPart(id, Circle(x, y, radius), Key.Shell, null, null, standout: true);
        AddDecor(part, symbol(x, y), stroke: true, 2.4);
    }

    private PadPart AddPart(ControlId id, string data, Key key, Point? labelAt, string? label, double labelSize = 11, bool standout = false)
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
            Stroke = standout
                ? new SolidColorBrush(Color.FromArgb(230, 255, 255, 255))
                : Theme.Brush(key == Key.Plate ? "PlateKeyLineBrush" : "PadKeyLineBrush"),
            StrokeThickness = standout ? 1.6 : 1,
        };
        if (standout)
        {
            root.Children.Add(new Path
            {
                Data = Geo(local),
                Stroke = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)),
                StrokeThickness = 3.2,
                IsHitTestVisible = false,
            });
        }
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
        _touchArea = new Rect(278 - b.X, 82 - b.Y, 244, 126);
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
        PaintLightbars();
        _edge.Stroke = brush;
        _edge.Opacity = mode switch { 2 => 0.55, 1 => 0.4, _ => 0 };
        _edgeGlow.Opacity = mode switch { 2 => 0.5, 1 => 0.3, _ => 0 };
        _playerLedGlow.Opacity = mode == 0 ? 0 : 1;
        _playerLed.Fill = mode == 0 ? Theme.Brush("PlateKeyLineBrush") : brush;

        foreach (var glow in _modeGlows)
            glow.Refresh();
        PaintLightbars();
    }

    private void PaintLightbars()
    {
        Brush brush = _mode == 0 ? Theme.Brush("LineBrush") : new SolidColorBrush(LightColor());
        foreach (var bar in _lightbars)
            bar.Stroke = brush;
        foreach (var host in _lightHosts)
            host.Opacity = _mode == 0 ? 0 : 1;
        foreach (var core in _lightCores)
            core.Opacity = _mode == 0 ? 0 : 1;
        foreach (var glow in _lightGlows)
            glow.Refresh();
    }

    private Color LightColor() => _mode == 1 ? ModeColor() : _layerLight ?? Theme.Accent;

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
        PaintLightbars();
        if (_shellColor.Length == 0 && _shellImage.Length == 0)
            _body.Fill = Theme.Brush("PadShellBrush");
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

    private static string ScalePath(string data, double factor) => Map(data, p => new Point(p.X * factor, p.Y * factor));

    private static string F(Point p) =>
        p.X.ToString("0.###", CultureInfo.InvariantCulture) + "," + p.Y.ToString("0.###", CultureInfo.InvariantCulture);

    private static Point Flip(Point p) => new(CanvasWidth - p.X, p.Y);

    private static string Symmetric(Point start, (Point C1, Point C2, Point End)[] left)
    {
        var builder = new StringBuilder("M ").Append(F(start));
        foreach (var (c1, c2, end) in left)
            builder.Append(" C ").Append(F(c1)).Append(' ').Append(F(c2)).Append(' ').Append(F(end));
        for (int i = left.Length - 1; i >= 0; i--)
        {
            var from = i == 0 ? start : left[i - 1].End;
            builder.Append(" C ").Append(F(Flip(left[i].C2))).Append(' ').Append(F(Flip(left[i].C1))).Append(' ').Append(F(Flip(from)));
        }

        return builder.Append(" Z").ToString();
    }

    private static (string Data, Point Mid, Point End) Band((Point P0, Point C1, Point C2, Point P3)[] curve, double from, double to, double offset, double half)
    {
        const int samples = 64;
        var outer = new List<Point>(samples + 1);
        var inner = new List<Point>(samples + 1);
        Point mid = default, end = default;
        for (int i = 0; i <= samples; i++)
        {
            double u = (1 - Math.Cos(Math.PI * i / samples)) / 2;
            double s = from + ((to - from) * u);
            int index = Math.Min((int)s, curve.Length - 1);
            double t = s - index;
            var (p0, c1, c2, p3) = curve[index];
            double a = 1 - t;
            var p = new Point(
                (a * a * a * p0.X) + (3 * a * a * t * c1.X) + (3 * a * t * t * c2.X) + (t * t * t * p3.X),
                (a * a * a * p0.Y) + (3 * a * a * t * c1.Y) + (3 * a * t * t * c2.Y) + (t * t * t * p3.Y));
            double dx = (a * a * (c1.X - p0.X)) + (2 * a * t * (c2.X - c1.X)) + (t * t * (p3.X - c2.X));
            double dy = (a * a * (c1.Y - p0.Y)) + (2 * a * t * (c2.Y - c1.Y)) + (t * t * (p3.Y - c2.Y));
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            double nx = -dy / length;
            double ny = dx / length;
            double width = half * Math.Pow(1 - Math.Pow(Math.Abs((2 * u) - 1), 4), 0.25);
            outer.Add(new Point(p.X + (nx * (offset + width)), p.Y + (ny * (offset + width))));
            inner.Add(new Point(p.X + (nx * (offset - width)), p.Y + (ny * (offset - width))));
            var center = new Point(p.X + (nx * offset), p.Y + (ny * offset));
            if (i == samples / 2)
                mid = center;
            if (i == samples)
                end = center;
        }

        var builder = new StringBuilder("M ").Append(F(outer[0]));
        for (int i = 1; i < outer.Count; i++)
            builder.Append(" L ").Append(F(outer[i]));
        for (int i = inner.Count - 1; i >= 0; i--)
            builder.Append(" L ").Append(F(inner[i]));
        return (builder.Append(" Z").ToString(), mid, end);
    }

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
