namespace Win2DS5.Core;

public static class KeyCatalog
{
    public static IReadOnlyList<KeyStroke> All { get; }
    public static KeyStroke Esc { get; }
    public static KeyStroke Tab { get; }
    public static KeyStroke Enter { get; }
    public static KeyStroke Space { get; }
    public static KeyStroke Backspace { get; }
    public static KeyStroke Delete { get; }
    public static KeyStroke LShift { get; }
    public static KeyStroke RShift { get; }
    public static KeyStroke LCtrl { get; }
    public static KeyStroke RCtrl { get; }
    public static KeyStroke LAlt { get; }
    public static KeyStroke RAlt { get; }
    public static KeyStroke LWin { get; }
    public static KeyStroke RWin { get; }
    public static KeyStroke Up { get; }
    public static KeyStroke Down { get; }
    public static KeyStroke Left { get; }
    public static KeyStroke Right { get; }
    public static KeyStroke Home { get; }
    public static KeyStroke End { get; }
    public static KeyStroke PageUp { get; }
    public static KeyStroke PageDown { get; }
    public static KeyStroke Comma { get; }
    public static KeyStroke Period { get; }
    public static KeyStroke Slash { get; }
    public static KeyStroke Minus { get; }
    public static KeyStroke EqualsKey { get; }
    public static KeyStroke LBracket { get; }
    public static KeyStroke RBracket { get; }
    public static KeyStroke Backslash { get; }
    public static KeyStroke Semicolon { get; }
    public static KeyStroke Apostrophe { get; }
    public static KeyStroke Grave { get; }

    static KeyCatalog()
    {
        All = Build();
        Esc = Find("Esc");
        Tab = Find("Tab");
        Enter = Find("Enter");
        Space = Find("Space");
        Backspace = Find("Backspace");
        Delete = Find("Delete");
        LShift = Find("Left Shift");
        RShift = Find("Right Shift");
        LCtrl = Find("Left Ctrl");
        RCtrl = Find("Right Ctrl");
        LAlt = Find("Left Alt");
        RAlt = Find("Right Alt");
        LWin = Find("Left Win");
        RWin = Find("Right Win");
        Up = Find("Up");
        Down = Find("Down");
        Left = Find("Left");
        Right = Find("Right");
        Home = Find("Home");
        End = Find("End");
        PageUp = Find("Page Up");
        PageDown = Find("Page Down");
        Comma = Find("Comma");
        Period = Find("Period");
        Slash = Find("Slash");
        Minus = Find("Minus");
        EqualsKey = Find("Equals");
        LBracket = Find("Left Bracket");
        RBracket = Find("Right Bracket");
        Backslash = Find("Backslash");
        Semicolon = Find("Semicolon");
        Apostrophe = Find("Apostrophe");
        Grave = Find("Grave");
    }

    public static KeyStroke Find(string name) =>
        All.First(key => key.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static KeyStroke Letter(char letter) => Find(char.ToUpperInvariant(letter).ToString());

    public static KeyStroke Digit(char digit) => Find(digit.ToString());

    public static KeyStroke? Match(ushort scanCode, bool extended) =>
        All.FirstOrDefault(key => key.ScanCode == scanCode && key.Extended == extended);

    private static KeyStroke Key(string name, ushort scan, bool extended = false) =>
        new() { Name = name, ScanCode = scan, Extended = extended };

    private static List<KeyStroke> Build()
    {
        var keys = new List<KeyStroke>
        {
            Key("Esc", 0x01),
            Key("1", 0x02), Key("2", 0x03), Key("3", 0x04), Key("4", 0x05), Key("5", 0x06),
            Key("6", 0x07), Key("7", 0x08), Key("8", 0x09), Key("9", 0x0A), Key("0", 0x0B),
            Key("Minus", 0x0C), Key("Equals", 0x0D), Key("Backspace", 0x0E), Key("Tab", 0x0F),
            Key("Q", 0x10), Key("W", 0x11), Key("E", 0x12), Key("R", 0x13), Key("T", 0x14),
            Key("Y", 0x15), Key("U", 0x16), Key("I", 0x17), Key("O", 0x18), Key("P", 0x19),
            Key("Left Bracket", 0x1A), Key("Right Bracket", 0x1B), Key("Enter", 0x1C),
            Key("Left Ctrl", 0x1D),
            Key("A", 0x1E), Key("S", 0x1F), Key("D", 0x20), Key("F", 0x21), Key("G", 0x22),
            Key("H", 0x23), Key("J", 0x24), Key("K", 0x25), Key("L", 0x26),
            Key("Semicolon", 0x27), Key("Apostrophe", 0x28), Key("Grave", 0x29),
            Key("Left Shift", 0x2A), Key("Backslash", 0x2B),
            Key("Z", 0x2C), Key("X", 0x2D), Key("C", 0x2E), Key("V", 0x2F), Key("B", 0x30),
            Key("N", 0x31), Key("M", 0x32), Key("Comma", 0x33), Key("Period", 0x34), Key("Slash", 0x35),
            Key("Right Shift", 0x36),
            Key("Left Alt", 0x38), Key("Space", 0x39), Key("Caps Lock", 0x3A),
            Key("F1", 0x3B), Key("F2", 0x3C), Key("F3", 0x3D), Key("F4", 0x3E), Key("F5", 0x3F),
            Key("F6", 0x40), Key("F7", 0x41), Key("F8", 0x42), Key("F9", 0x43), Key("F10", 0x44),
            Key("F11", 0x57), Key("F12", 0x58),
            Key("Right Ctrl", 0x1D, true),
            Key("Right Alt", 0x38, true),
            Key("Left Win", 0x5B, true),
            Key("Right Win", 0x5C, true),
            Key("Menu", 0x5D, true),
            Key("Insert", 0x52, true),
            Key("Delete", 0x53, true),
            Key("Home", 0x47, true),
            Key("End", 0x4F, true),
            Key("Page Up", 0x49, true),
            Key("Page Down", 0x51, true),
            Key("Left", 0x4B, true),
            Key("Right", 0x4D, true),
            Key("Up", 0x48, true),
            Key("Down", 0x50, true),
            Key("Print Screen", 0x37, true),
            Key("Scroll Lock", 0x46),
            Key("Pause", 0x45),
            Key("Num Lock", 0x45),
            Key("Numpad Divide", 0x35, true),
            Key("Numpad Multiply", 0x37),
            Key("Numpad Minus", 0x4A),
            Key("Numpad Plus", 0x4E),
            Key("Numpad Enter", 0x1C, true),
            Key("Numpad Decimal", 0x53),
            Key("Numpad 0", 0x52), Key("Numpad 1", 0x4F), Key("Numpad 2", 0x50), Key("Numpad 3", 0x51),
            Key("Numpad 4", 0x4B), Key("Numpad 5", 0x4C), Key("Numpad 6", 0x4D),
            Key("Numpad 7", 0x47), Key("Numpad 8", 0x48), Key("Numpad 9", 0x49),
            Key("Media Play", 0x22, true),
            Key("Media Next", 0x19, true),
            Key("Media Previous", 0x10, true),
            Key("Volume Mute", 0x20, true),
            Key("Volume Down", 0x2E, true),
            Key("Volume Up", 0x30, true),
            Key("Fn", 0),
        };
        return keys;
    }
}
