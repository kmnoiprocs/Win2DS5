namespace Win2DS5.Core;

public enum KeyRole
{
    Character,
    Shift,
    Caps,
    Fn,
    Ctrl,
    Alt,
    Win,
}

public sealed class KeyCell
{
    public string Label { get; init; } = "";
    public string Super { get; init; } = "";
    public string FnLabel { get; init; } = "";
    public KeyStroke? Stroke { get; init; }
    public KeyStroke? FnStroke { get; init; }
    public bool FnFollowsShift { get; init; }
    public bool IsLetter { get; init; }
    public KeyRole Role { get; init; }
    public double Span { get; init; } = 1;
    public char? Unshifted { get; init; }
    public char? Shifted { get; init; }
}

public static class KeyboardLayout
{
    public static IReadOnlyList<IReadOnlyList<KeyCell>> Create()
    {
        var f1 = KeyCatalog.Find("F1");
        var f2 = KeyCatalog.Find("F2");
        var f3 = KeyCatalog.Find("F3");
        var f4 = KeyCatalog.Find("F4");
        var f5 = KeyCatalog.Find("F5");
        var f6 = KeyCatalog.Find("F6");
        var f7 = KeyCatalog.Find("F7");
        var f8 = KeyCatalog.Find("F8");
        var f9 = KeyCatalog.Find("F9");
        var f10 = KeyCatalog.Find("F10");
        var f11 = KeyCatalog.Find("F11");
        var f12 = KeyCatalog.Find("F12");
        return
        [
            Row(
                Nav("Esc", KeyCatalog.Esc, fnLabel: "`", fn: KeyCatalog.Grave, fnShift: true),
                Digit('1', '!', f1), Digit('2', '@', f2), Digit('3', '#', f3), Digit('4', '$', f4),
                Digit('5', '%', f5), Digit('6', '^', f6), Digit('7', '&', f7), Digit('8', '*', f8),
                Digit('9', '(', f9), Digit('0', ')', f10),
                Symbol("-", "_", KeyCatalog.Minus, '-', '_', fnLabel: "F11", fn: f11),
                Symbol("=", "+", KeyCatalog.EqualsKey, '=', '+', fnLabel: "F12", fn: f12),
                Nav("Back", KeyCatalog.Backspace, 2),
                Nav("Del", KeyCatalog.Delete, fnLabel: "Ins", fn: KeyCatalog.Find("Insert"))),
            Row(
                Nav("Tab", KeyCatalog.Tab, 1.5),
                Letter('q'), Letter('w'), Letter('e'), Letter('r'), Letter('t'),
                Letter('y'), Letter('u'), Letter('i'), Letter('o'), Letter('p'),
                Symbol("[", "{", KeyCatalog.LBracket, '[', '{'),
                Symbol("]", "}", KeyCatalog.RBracket, ']', '}'),
                Symbol("\\", "|", KeyCatalog.Backslash, '\\', '|', 1.5),
                Nav("PgUp", KeyCatalog.PageUp)),
            Row(
                Modifier("Caps", KeyRole.Caps, 1.75),
                Letter('a'), Letter('s'), Letter('d'), Letter('f'), Letter('g'),
                Letter('h'), Letter('j'), Letter('k'), Letter('l'),
                Symbol(";", ":", KeyCatalog.Semicolon, ';', ':'),
                Symbol("'", "\"", KeyCatalog.Apostrophe, '\'', '"'),
                Nav("Enter", KeyCatalog.Enter, 2.25),
                Nav("PgDn", KeyCatalog.PageDown)),
            Row(
                Modifier("Shift", KeyRole.Shift, 2.25),
                Letter('z'), Letter('x'), Letter('c'), Letter('v'), Letter('b'), Letter('n'), Letter('m'),
                Symbol(",", "<", KeyCatalog.Comma, ',', '<'),
                Symbol(".", ">", KeyCatalog.Period, '.', '>'),
                Symbol("/", "?", KeyCatalog.Slash, '/', '?'),
                Modifier("Shift", KeyRole.Shift, 2.75),
                Nav("\u2191", KeyCatalog.Up)),
            Row(
                Modifier("Ctrl", KeyRole.Ctrl, 1.25),
                Modifier("Win", KeyRole.Win, 1.25),
                Modifier("Alt", KeyRole.Alt, 1.25),
                Nav("Space", KeyCatalog.Space, 6),
                Modifier("Alt", KeyRole.Alt, 1.25),
                Modifier("Fn", KeyRole.Fn, 1),
                Modifier("Ctrl", KeyRole.Ctrl, 1),
                Nav("\u2190", KeyCatalog.Left, fnLabel: "Home", fn: KeyCatalog.Home),
                Nav("\u2193", KeyCatalog.Down),
                Nav("\u2192", KeyCatalog.Right, fnLabel: "End", fn: KeyCatalog.End)),
        ];
    }

    private static IReadOnlyList<KeyCell> Row(params KeyCell[] cells) => cells;

    private static KeyCell Letter(char letter) => new()
    {
        Label = char.ToUpperInvariant(letter).ToString(),
        Stroke = KeyCatalog.Letter(letter),
        IsLetter = true,
        Unshifted = char.ToLowerInvariant(letter),
    };

    private static KeyCell Digit(char digit, char shifted, KeyStroke fn) =>
        Symbol(digit.ToString(), shifted.ToString(), KeyCatalog.Digit(digit), digit, shifted, fnLabel: fn.Name, fn: fn);

    private static KeyCell Symbol(string label, string super, KeyStroke stroke, char unshifted, char shifted, double span = 1, string fnLabel = "", KeyStroke? fn = null) => new()
    {
        Label = label,
        Super = super,
        Stroke = stroke,
        Unshifted = unshifted,
        Shifted = shifted,
        Span = span,
        FnLabel = fnLabel,
        FnStroke = fn,
    };

    private static KeyCell Nav(string label, KeyStroke stroke, double span = 1, string fnLabel = "", KeyStroke? fn = null, bool fnShift = false) => new()
    {
        Label = label,
        Stroke = stroke,
        Span = span,
        FnLabel = fnLabel,
        FnStroke = fn,
        FnFollowsShift = fnShift,
    };

    private static KeyCell Modifier(string label, KeyRole role, double span) => new()
    {
        Label = label,
        Role = role,
        Span = span,
    };
}
