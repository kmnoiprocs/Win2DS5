using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

public class KeyboardTests
{
    [Fact]
    public void Layout_IsA65PercentKeyboard()
    {
        var keyboard = new KeyboardModel();
        Assert.Equal(5, keyboard.Current.Count);
        foreach (var row in keyboard.Current)
            Assert.Equal(16, row.Sum(cell => cell.Span), 3);
        Assert.Equal("Q", Label(keyboard));
        Assert.Contains(keyboard.Current[0], cell => cell.Label == "Esc");
        Assert.Contains(keyboard.Current[0], cell => cell.Label == "Back");
        Assert.Contains(keyboard.Current[3], cell => cell.Label == "\u2191");
        Assert.Contains(keyboard.Current[4], cell => cell.Label == "Space");
        Assert.Contains(keyboard.Current[4], cell => cell.Label == "\u2190");
        Assert.Contains(keyboard.Current[4], cell => cell.Label == "\u2192");
    }

    [Fact]
    public void Navigation_FollowsKeyCenters()
    {
        var keyboard = new KeyboardModel();
        keyboard.MoveSelection(0, 1);
        Assert.Equal("A", Label(keyboard));
        keyboard.MoveSelection(0, -1);
        Assert.Equal("Q", Label(keyboard));
        keyboard.MoveSelection(1, 0);
        Assert.Equal("W", Label(keyboard));
        keyboard.MoveSelection(0, -1);
        Assert.Equal("2", Label(keyboard));
    }

    [Fact]
    public void Shift_TypesTheSymbolAboveTheNumber()
    {
        var keyboard = new KeyboardModel();
        Tap(keyboard, "Shift");
        var number = keyboard.Current[0].First(cell => cell.Label == "1");
        keyboard.Describe(number, out var label, out var super, out _, out var emphasize);
        Assert.Equal("1", label);
        Assert.Equal("!", super);
        Assert.True(emphasize);
        Assert.Equal(new[] { KeyCatalog.LShift.ScanCode, KeyCatalog.Digit('1').ScanCode }, Downs(Tap(keyboard, "1")));
    }

    [Fact]
    public void Fn_TurnsTheNumberRowIntoFunctionKeys()
    {
        var keyboard = new KeyboardModel();
        Assert.Empty(Tap(keyboard, "Fn"));
        var number = keyboard.Current[0].First(cell => cell.Label == "1");
        keyboard.Describe(number, out var label, out var super, out _, out _);
        Assert.Equal("F1", label);
        Assert.Equal("", super);
        Assert.Equal(new[] { KeyCatalog.Find("F1").ScanCode }, Downs(Tap(keyboard, "1")));
    }

    [Fact]
    public void CapsLock_ShiftsLettersUntilItIsToggledOff()
    {
        var keyboard = new KeyboardModel();
        Tap(keyboard, "Caps");
        Assert.Equal(new[] { KeyCatalog.LShift.ScanCode, KeyCatalog.Letter('A').ScanCode }, Downs(Tap(keyboard, "A")));
        Tap(keyboard, "Shift");
        Assert.Equal(new[] { KeyCatalog.Letter('A').ScanCode }, Downs(Tap(keyboard, "A")));
    }

    [Fact]
    public void StickyCtrl_SendsTheNextKeyAsAShortcut()
    {
        var keyboard = new KeyboardModel();
        Tap(keyboard, "Ctrl");
        Assert.Equal(new[] { KeyCatalog.LCtrl.ScanCode, KeyCatalog.Letter('C').ScanCode }, Downs(Tap(keyboard, "C")));
        Assert.Empty(keyboard.Suggestions);
    }

    [Fact]
    public void Corrections_PreferTheCommonWordAndStillKeepTheSpelling()
    {
        var keyboard = new KeyboardModel();
        Tap(keyboard, "T");
        Tap(keyboard, "E");
        Tap(keyboard, "H");
        Assert.Equal("the", keyboard.Suggestions[0]);
        Assert.Contains("teh", keyboard.Suggestions);
        Assert.Equal(new[] { KeyCatalog.Space.ScanCode }, Downs(Tap(keyboard, "Space")));
        Assert.Equal("teh", keyboard.Memory.Suggest("teh", "", 4)[0]);
    }

    [Fact]
    public void Suggestions_UseThePreviousWord()
    {
        var keyboard = new KeyboardModel();
        Assert.True(keyboard.Memory.KnownWords > 2000);
        Assert.Equal("the", keyboard.Memory.Suggest("", "of", 4)[0]);
        Assert.Equal("the", keyboard.Memory.Suggest("th", "of", 4)[0]);
        keyboard.Memory.Learn("zzplay", "zqxword");
        foreach (var letter in new[] { "Z", "Z", "P", "L", "A", "Y" })
            Tap(keyboard, letter);
        Tap(keyboard, "Space");
        Assert.Equal("zqxword", keyboard.Suggestions[0]);
    }

    [Fact]
    public void Accept_ReplacesThePrefixAndCirclePutsItBack()
    {
        var keyboard = new KeyboardModel();
        Tap(keyboard, "T");
        Tap(keyboard, "E");
        Tap(keyboard, "H");
        var commands = new List<OutputCommand>();
        keyboard.AcceptSuggestion(commands);
        Assert.Equal(
            new[]
            {
                KeyCatalog.Backspace.ScanCode,
                KeyCatalog.Backspace.ScanCode,
                KeyCatalog.Backspace.ScanCode,
                KeyCatalog.Letter('T').ScanCode,
                KeyCatalog.Letter('H').ScanCode,
                KeyCatalog.Letter('E').ScanCode,
                KeyCatalog.Space.ScanCode,
            },
            Downs(commands));
        commands.Clear();
        keyboard.Backspace(commands);
        Assert.Equal(
            new[]
            {
                KeyCatalog.Backspace.ScanCode,
                KeyCatalog.Backspace.ScanCode,
                KeyCatalog.Backspace.ScanCode,
                KeyCatalog.Backspace.ScanCode,
                KeyCatalog.Letter('T').ScanCode,
                KeyCatalog.Letter('E').ScanCode,
                KeyCatalog.Letter('H').ScanCode,
            },
            Downs(commands));
        Assert.Equal("the", keyboard.Suggestions[0]);
    }

    [Fact]
    public void Engine_CorrectsTranspositionsAndCompletesPrefixes()
    {
        var memory = new WordMemory(new Dictionary<string, int>
        {
            ["the"] = 5000,
            ["ten"] = 200,
            ["tea"] = 100,
            ["receive"] = 800,
            ["cat"] = 300,
        });
        Assert.Equal("the", memory.Suggest("teh", "", 4)[0]);
        Assert.Equal("receive", memory.Suggest("recieve", "", 4)[0]);
        Assert.Equal("cat", memory.Suggest("ca", "", 4)[0]);
    }

    [Fact]
    public void ShoulderButtons_ChangeAndInsertSuggestionsWithoutMoving()
    {
        var session = new MappingSession(ProfilePresets.MouseHand()) { DesktopEnabled = true };
        session.Keyboard.IsOpen = true;
        var clock = TimeSpan.Zero;
        foreach (var label in new[] { "T", "E", "H" })
        {
            Assert.True(session.Keyboard.Focus(label));
            clock += TimeSpan.FromMilliseconds(30);
            session.Step(Pad.Hold(ControlId.Cross), clock);
            clock += TimeSpan.FromMilliseconds(30);
            session.Step(Pad.Hold(), clock);
        }

        Assert.Equal("the", session.Keyboard.Suggestions[0]);
        session.Keyboard.CycleSuggestion(0);
        int row = session.Keyboard.Row;
        int column = session.Keyboard.Column;
        clock += TimeSpan.FromMilliseconds(30);
        session.Step(Pad.Hold(ControlId.L1), clock);
        Assert.Equal(row, session.Keyboard.Row);
        Assert.Equal(column, session.Keyboard.Column);
        Assert.NotEqual(0, session.Keyboard.SuggestionIndex);
        clock += TimeSpan.FromMilliseconds(30);
        session.Step(Pad.Hold(), clock);
        clock += TimeSpan.FromMilliseconds(30);
        session.Step(Pad.Hold(ControlId.L1), clock);
        while (session.Keyboard.SuggestionIndex != 0)
        {
            clock += TimeSpan.FromMilliseconds(30);
            session.Step(Pad.Hold(), clock);
            clock += TimeSpan.FromMilliseconds(30);
            session.Step(Pad.Hold(ControlId.L1), clock);
        }

        clock += TimeSpan.FromMilliseconds(30);
        session.Step(Pad.Hold(), clock);
        clock += TimeSpan.FromMilliseconds(30);
        var inserted = session.Step(Pad.Hold(ControlId.R1), clock);
        Assert.Equal(row, session.Keyboard.Row);
        Assert.Equal(column, session.Keyboard.Column);
        Assert.Equal(KeyCatalog.Letter('T').ScanCode, inserted.Commands.First(command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.Letter('T').ScanCode).A);
        Assert.Contains(inserted.Commands, command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.Space.ScanCode);
    }

    [Fact]
    public void OpensBesideThePointerOnThatMonitor()
    {
        var primary = new PixelRect(0, 0, 1920, 1080);
        var below = KeyboardPlacement.NearPointer(new PixelPoint(400, 300), primary, 800, 400);
        Assert.Equal(0, below.X);
        Assert.Equal(318, below.Y);

        var above = KeyboardPlacement.NearPointer(new PixelPoint(960, 1000), primary, 800, 400);
        Assert.Equal(560, above.X);
        Assert.Equal(582, above.Y);

        var leftMonitor = new PixelRect(-1920, 0, 1920, 1080);
        var there = KeyboardPlacement.NearPointer(new PixelPoint(-200, 400), leftMonitor, 1000, 500);
        Assert.InRange(there.X, -1920, -1000);
        Assert.InRange(there.Y, 0, 580);
        Assert.True(there.X + 1000 <= 0);
    }

    [Fact]
    public void RightStick_CanCrossOntoAMonitorToTheLeft()
    {
        var keyboard = new KeyboardModel { Left = 40, Top = 200 };
        keyboard.SetTravel(-2560, 0, 1920, 1080, 900, 420);
        for (int i = 0; i < 30; i++)
            keyboard.Adjust(-1, 0, false, false, 0.05);
        Assert.True(keyboard.Left < -400);
        Assert.InRange(keyboard.Left, -2560, 1920 - 900);
    }

    private static string Label(KeyboardModel keyboard) => keyboard.Current[keyboard.Row][keyboard.Column].Label;

    private static List<OutputCommand> Tap(KeyboardModel keyboard, string label)
    {
        Assert.True(keyboard.Focus(label));
        var commands = new List<OutputCommand>();
        keyboard.TypeSelected(commands);
        return commands;
    }

    private static ushort[] Downs(List<OutputCommand> commands) =>
        commands.Where(command => command.Kind == OutputKind.KeyDown).Select(command => (ushort)command.A).ToArray();
}
