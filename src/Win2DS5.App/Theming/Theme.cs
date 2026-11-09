using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Win2DS5.App.Theming;

public sealed class Palette
{
    public Color Canvas { get; init; }
    public Color CanvasTop { get; init; }
    public Color Rail { get; init; }
    public Color Panel { get; init; }
    public Color PanelTop { get; init; }
    public Color PanelAlt { get; init; }
    public Color PanelAltTop { get; init; }
    public Color Line { get; init; }
    public Color Text { get; init; }
    public Color Muted { get; init; }
    public Color Amber { get; init; }
    public Color Success { get; init; }
    public Color Hover { get; init; }
    public Color PadShell { get; init; }
    public Color PadShellTop { get; init; }
    public Color PadPlate { get; init; }
    public Color PadEdge { get; init; }
    public Color PadKey { get; init; }
    public Color PadKeyTop { get; init; }
    public Color PadKeyLine { get; init; }
    public Color PadInk { get; init; }
    public Color PlateKey { get; init; }
    public Color PlateKeyTop { get; init; }
    public Color PlateKeyLine { get; init; }
    public Color PlateInk { get; init; }
    public Color TouchSurface { get; init; }
    public Color Well { get; init; }
}

public static class Theme
{
    private static readonly Palette DarkPalette = new()
    {
        Canvas = Hex("#0A0C10"),
        CanvasTop = Hex("#11141B"),
        Rail = Hex("#0E1116"),
        Panel = Hex("#13161C"),
        PanelTop = Hex("#1A1E26"),
        PanelAlt = Hex("#1B1F28"),
        PanelAltTop = Hex("#232833"),
        Line = Hex("#262B35"),
        Text = Hex("#F2F4F8"),
        Muted = Hex("#8E97A8"),
        Amber = Hex("#F5B853"),
        Success = Hex("#4ADE80"),
        Hover = Hex("#16FFFFFF"),
        PadShell = Hex("#161A21"),
        PadShellTop = Hex("#262B35"),
        PadPlate = Hex("#0D1015"),
        PadEdge = Hex("#2F3542"),
        PadKey = Hex("#13161C"),
        PadKeyTop = Hex("#252A34"),
        PadKeyLine = Hex("#3A4250"),
        PadInk = Hex("#D7DDE8"),
        PlateKey = Hex("#0F1217"),
        PlateKeyTop = Hex("#20252E"),
        PlateKeyLine = Hex("#353C49"),
        PlateInk = Hex("#C3CAD6"),
        TouchSurface = Hex("#1B2028"),
        Well = Hex("#090B0F"),
    };

    private static readonly Palette LightPalette = new()
    {
        Canvas = Hex("#E9EDF3"),
        CanvasTop = Hex("#F6F8FB"),
        Rail = Hex("#E7EBF2"),
        Panel = Hex("#F7F9FC"),
        PanelTop = Hex("#FFFFFF"),
        PanelAlt = Hex("#ECEFF5"),
        PanelAltTop = Hex("#F9FAFC"),
        Line = Hex("#D3D9E3"),
        Text = Hex("#12151B"),
        Muted = Hex("#5B6475"),
        Amber = Hex("#B26B0C"),
        Success = Hex("#16A34A"),
        Hover = Hex("#10000000"),
        PadShell = Hex("#E3E7EE"),
        PadShellTop = Hex("#FFFFFF"),
        PadPlate = Hex("#22262E"),
        PadEdge = Hex("#C4CBD7"),
        PadKey = Hex("#DFE3EA"),
        PadKeyTop = Hex("#FFFFFF"),
        PadKeyLine = Hex("#B3BBC8"),
        PadInk = Hex("#3A414D"),
        PlateKey = Hex("#1A1E25"),
        PlateKeyTop = Hex("#3A404B"),
        PlateKeyLine = Hex("#4B525E"),
        PlateInk = Hex("#E2E6EC"),
        TouchSurface = Hex("#F9FAFC"),
        Well = Hex("#16191F"),
    };

    public static (string Name, Color Color)[] Accents { get; } =
    [
        ("Ice", Hex("#7EB6FF")),
        ("Cyan", Hex("#22D3EE")),
        ("Mint", Hex("#34D399")),
        ("Lime", Hex("#A3E635")),
        ("Amber", Hex("#FBBF24")),
        ("Coral", Hex("#FB7185")),
        ("Magenta", Hex("#F472D0")),
        ("Violet", Hex("#A78BFA")),
    ];

    public static event Action? Changed;

    public static bool IsDark { get; private set; } = true;
    public static Color Accent { get; private set; } = Hex("#7EB6FF");
    public static Palette Current => IsDark ? DarkPalette : LightPalette;
    public static ElementTheme ElementTheme => IsDark ? ElementTheme.Dark : ElementTheme.Light;
    public static Color Ink => Luminance(Accent) > 0.45 ? Hex("#0A0C10") : Colors.White;
    public static Color AccentText => IsDark ? Accent : Mix(Accent, Colors.Black, 0.38);

    public static void Apply(bool dark, Color accent)
    {
        IsDark = dark;
        Accent = Color.FromArgb(255, accent.R, accent.G, accent.B);
        var p = Current;
        var r = Application.Current.Resources;

        Solid(r, "CanvasBrush", p.Canvas);
        Solid(r, "RailBrush", p.Rail);
        Solid(r, "PanelBrush", p.Panel);
        Solid(r, "PanelAltBrush", p.PanelAlt);
        Solid(r, "LineBrush", p.Line);
        Solid(r, "TextBrush", p.Text);
        Solid(r, "MutedBrush", p.Muted);
        Solid(r, "AmberBrush", p.Amber);
        Solid(r, "AmberHaloBrush", Alpha(p.Amber, 0x66));
        Solid(r, "SuccessBrush", p.Success);
        Solid(r, "SuccessHaloBrush", Alpha(p.Success, 0x55));
        Solid(r, "HoverBrush", p.Hover);
        Solid(r, "PadPlateBrush", p.PadPlate);
        Solid(r, "PadEdgeBrush", p.PadEdge);
        Solid(r, "PadKeyLineBrush", p.PadKeyLine);
        Solid(r, "PadInkBrush", p.PadInk);
        Solid(r, "PlateKeyLineBrush", p.PlateKeyLine);
        Solid(r, "PlateInkBrush", p.PlateInk);
        Solid(r, "TouchSurfaceBrush", p.TouchSurface);
        Solid(r, "WellBrush", p.Well);

        Solid(r, "AccentBrush", Accent);
        Solid(r, "AccentHoverBrush", dark ? Mix(Accent, Colors.White, 0.18) : Mix(Accent, Colors.Black, 0.1));
        Solid(r, "AccentPressedBrush", Mix(Accent, Colors.Black, 0.18));
        Solid(r, "AccentInkBrush", Ink);
        Solid(r, "AccentTextBrush", AccentText);
        Solid(r, "AccentSoftBrush", Alpha(Accent, dark ? (byte)0x2E : (byte)0x2A));
        Solid(r, "AccentHaloBrush", Alpha(Accent, dark ? (byte)0x6A : (byte)0x5A));

        Stops(r, "CanvasGradientBrush", p.CanvasTop, p.Canvas);
        Stops(r, "PanelGradientBrush", p.PanelTop, p.Panel);
        Stops(r, "RailGradientBrush", p.Rail, p.Canvas);
        Stops(r, "ButtonFillBrush", p.PanelAltTop, p.PanelAlt);
        Stops(r, "PadShellBrush", p.PadShellTop, p.PadShell);
        Stops(r, "PadKeyBrush", p.PadKeyTop, p.PadKey);
        Stops(r, "PlateKeyBrush", p.PlateKeyTop, p.PlateKey);
        Stops(r, "HeaderGradientBrush", Alpha(Accent, dark ? (byte)0x2C : (byte)0x26), Alpha(Accent, 0));
        Stops(r, "AccentGradientBrush", Mix(Accent, Colors.White, 0.16), Mix(Accent, Colors.Black, 0.08));
        Stops(r, "SelectedRailBrush", Alpha(Accent, dark ? (byte)0x30 : (byte)0x2A), Alpha(Accent, 0));
        Stops(r, "GlowLineBrush", Alpha(Accent, 0), Alpha(Accent, 0xD0), Alpha(Accent, 0));

        SetSystemAccent(r);
        Changed?.Invoke();
    }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static Color Hex(string hex)
    {
        var text = hex.Trim().TrimStart('#');
        if (text.Length == 6)
            text = "FF" + text;
        if (text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            return Color.FromArgb(255, 126, 182, 255);
        return Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color Alpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    public static bool Same(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

    private static double Luminance(Color c) => ((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)) / 255.0;

    private static void Solid(ResourceDictionary r, string key, Color color)
    {
        if (r.TryGetValue(key, out var value) && value is SolidColorBrush brush)
            brush.Color = color;
    }

    private static void Stops(ResourceDictionary r, string key, params Color[] colors)
    {
        if (!r.TryGetValue(key, out var value) || value is not GradientBrush brush)
            return;
        for (int i = 0; i < Math.Min(colors.Length, brush.GradientStops.Count); i++)
            brush.GradientStops[i].Color = colors[i];
    }

    private static void SetSystemAccent(ResourceDictionary r)
    {
        r["SystemAccentColor"] = Accent;
        r["SystemAccentColorLight1"] = Mix(Accent, Colors.White, 0.15);
        r["SystemAccentColorLight2"] = Mix(Accent, Colors.White, 0.3);
        r["SystemAccentColorLight3"] = Mix(Accent, Colors.White, 0.45);
        r["SystemAccentColorDark1"] = Mix(Accent, Colors.Black, 0.15);
        r["SystemAccentColorDark2"] = Mix(Accent, Colors.Black, 0.3);
        r["SystemAccentColorDark3"] = Mix(Accent, Colors.Black, 0.45);
    }
}
