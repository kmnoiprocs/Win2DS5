namespace Win2DS5.Core;

public sealed class KeyboardModel
{
    public const string ControlsHint = "L1 next word    ·    R1 insert it    ·    □ space    ·    ○ delete    ·    △ enter";

    private readonly IReadOnlyList<IReadOnlyList<KeyCell>> _rows;
    private readonly List<string> _shown = [];
    private readonly List<Committed> _history = [];
    private readonly int _homeRow;
    private readonly int _homeColumn;
    private string _prefix = "";
    private string _previous = "";
    private Stamp? _undo;
    private int _index;
    private bool _open;
    private int _travelLeft = -32000;
    private int _travelTop = -32000;
    private int _travelRight = 32000;
    private int _travelBottom = 32000;
    private double _spanWidth;
    private double _spanHeight;

    public KeyboardModel()
    {
        _rows = KeyboardLayout.Create();
        (_homeRow, _homeColumn) = Find("Q");
        Memory = new WordMemory();
        ResetTyping();
    }

    public WordMemory Memory { get; }
    public double Opacity { get; set; } = 0.94;
    public double Left { get; set; } = 160;
    public double Top { get; set; } = 560;
    public double Width { get; set; } = 1120;
    public double Height { get; set; } = 520;
    public int Row { get; private set; }
    public int Column { get; private set; }
    public bool ShiftOnce { get; private set; }
    public bool CapsLock { get; private set; }
    public bool FnLock { get; private set; }
    public bool CtrlLock { get; private set; }
    public bool AltLock { get; private set; }
    public bool WinLock { get; private set; }
    public int LayoutGeneration => 1;
    public int Presses { get; private set; }
    public IReadOnlyList<IReadOnlyList<KeyCell>> Current => _rows;
    public IReadOnlyList<string> Suggestions => _shown;
    public int SuggestionIndex => _index;

    public bool IsOpen
    {
        get => _open;
        set
        {
            if (_open == value)
                return;
            _open = value;
            ResetTyping();
        }
    }

    public void AttachMemory(string path) => Memory.UseStore(path);

    public void MoveSelection(int dColumn, int dRow)
    {
        if (dRow != 0)
            MoveVertical(dRow);
        if (dColumn != 0)
            MoveHorizontal(dColumn);
    }

    public void CycleSuggestion(int delta)
    {
        if (_shown.Count == 0 || delta == 0)
            return;
        int count = _shown.Count;
        _index = (_index + delta % count + count) % count;
        if (Row < 0)
            Column = _index;
    }

    public void TypeSelected(List<OutputCommand> commands)
    {
        if (Row < 0)
        {
            AcceptSuggestion(commands);
            return;
        }

        Type(_rows[Row][Column], commands);
    }

    public void AcceptSuggestion(List<OutputCommand> commands)
    {
        if (_shown.Count == 0)
            return;
        _index = Math.Clamp(_index, 0, _shown.Count - 1);
        var word = _shown[_index];
        if (word.Length == 0)
            return;
        if (string.Equals(word, _prefix, StringComparison.Ordinal))
        {
            TypeSpace(commands);
            return;
        }

        foreach (var letter in word)
        {
            if (!TryStroke(letter, out _, out _))
                return;
        }

        var stamp = new Stamp
        {
            Prefix = _prefix,
            Previous = _previous,
            History = _history.Select(item => item.Copy()).ToList(),
            Delete = word.Length + 1,
            Retype = _prefix,
            LearnedAfter = _previous,
            LearnedWord = word,
        };
        Presses++;
        for (int i = 0; i < _prefix.Length; i++)
            EmitStroke(commands, KeyCatalog.Backspace, false);
        foreach (var letter in word)
            EmitChar(commands, letter);
        EmitStroke(commands, KeyCatalog.Space, false);
        bool learned = word.Any(char.IsLetter) && Memory.Learn(_previous, word);
        stamp.Learned = learned;
        _undo = stamp;
        if (word.Any(char.IsLetter))
        {
            _history.Add(new Committed { Text = word, Previous = _previous, Learned = learned });
            if (_history.Count > 40)
                _history.RemoveAt(0);
            _previous = word.ToLowerInvariant();
        }

        _prefix = "";
        ShiftOnce = false;
        Refresh();
    }

    public void TypeSpace(List<OutputCommand> commands)
    {
        _undo = null;
        Presses++;
        Commit();
        EmitStroke(commands, KeyCatalog.Space, false);
        Refresh();
    }

    public void TypeEnter(List<OutputCommand> commands)
    {
        _undo = null;
        Presses++;
        Commit();
        EmitStroke(commands, KeyCatalog.Enter, false);
        Refresh();
    }

    public void Backspace(List<OutputCommand> commands)
    {
        if (_undo != null)
        {
            Undo(commands);
            return;
        }

        Presses++;
        EmitStroke(commands, KeyCatalog.Backspace, false);
        if (_prefix.Length > 0)
            _prefix = _prefix[..^1];
        else if (_history.Count > 0)
        {
            var last = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            if (last.Learned)
                Memory.Unlearn(last.Previous, last.Text);
            _prefix = last.Text;
            _previous = last.Previous;
        }

        Refresh();
    }

    public void TypeAt(double nx, double ny, List<OutputCommand> commands)
    {
        nx = Math.Clamp(nx, 0, 0.999);
        ny = Math.Clamp(ny, 0, 0.999);
        const double band = 0.2;
        if (ny < band)
        {
            if (_shown.Count == 0)
                return;
            _index = Math.Clamp((int)(nx * _shown.Count), 0, _shown.Count - 1);
            Row = -1;
            Column = _index;
            AcceptSuggestion(commands);
            return;
        }

        double y = (ny - band) / (1 - band);
        int row = Math.Clamp((int)(y * _rows.Count), 0, _rows.Count - 1);
        var cells = _rows[row];
        double total = cells.Sum(cell => cell.Span);
        double cursor = nx * total;
        double walked = 0;
        int column = cells.Count - 1;
        for (int i = 0; i < cells.Count; i++)
        {
            walked += cells[i].Span;
            if (cursor <= walked)
            {
                column = i;
                break;
            }
        }

        Row = row;
        Column = column;
        Type(cells[column], commands);
    }

    public bool Focus(string label)
    {
        var found = Find(label);
        if (found.Row < 0)
            return false;
        Row = found.Row;
        Column = found.Column;
        return true;
    }

    public void Describe(KeyCell cell, out string label, out string super, out bool latched, out bool emphasizeSuper)
    {
        latched = cell.Role switch
        {
            KeyRole.Shift => ShiftOnce,
            KeyRole.Caps => CapsLock,
            KeyRole.Fn => FnLock,
            KeyRole.Ctrl => CtrlLock,
            KeyRole.Alt => AltLock,
            KeyRole.Win => WinLock,
            _ => false,
        };
        if (FnLock && cell.FnLabel.Length > 0)
        {
            label = cell.FnLabel;
            super = "";
            emphasizeSuper = false;
            return;
        }

        label = cell.IsLetter ? cell.Label.ToUpperInvariant() : cell.Label;
        super = cell.Super;
        emphasizeSuper = ShiftOnce && super.Length > 0;
    }

    public void Adjust(double rx, double ry, bool resize, bool opacity, double dt)
    {
        if (resize)
        {
            Width = Math.Clamp(Width + (rx * 720 * dt), 760, 1800);
            Height = Math.Clamp(Height + (ry * 480 * dt), 320, 960);
            return;
        }

        if (opacity)
        {
            Opacity = Math.Clamp(Opacity - (ry * 0.9 * dt), 0.28, 1);
            return;
        }

        double spanWidth = _spanWidth > 0 ? _spanWidth : Width;
        double spanHeight = _spanHeight > 0 ? _spanHeight : Height;
        Left = ClampAxis(Left + (rx * 1100 * dt), _travelLeft, _travelRight, spanWidth);
        Top = ClampAxis(Top + (ry * 800 * dt), _travelTop, _travelBottom, spanHeight);
    }

    public void SetTravel(int left, int top, int right, int bottom, double spanWidth, double spanHeight)
    {
        _travelLeft = Math.Min(left, right);
        _travelTop = Math.Min(top, bottom);
        _travelRight = Math.Max(left, right);
        _travelBottom = Math.Max(top, bottom);
        _spanWidth = Math.Max(1, spanWidth);
        _spanHeight = Math.Max(1, spanHeight);
    }

    private static double ClampAxis(double value, int origin, int extent, double span)
    {
        double limit = origin + Math.Max(0, (extent - origin) - span);
        return Math.Clamp(value, origin, limit);
    }

    public void ApplySettings(AppSettings settings)
    {
        Opacity = settings.KeyboardOpacity;
        Left = settings.KeyboardLeft;
        Top = settings.KeyboardTop;
        Width = settings.KeyboardWidth;
        Height = settings.KeyboardHeight;
    }

    public void WriteSettings(AppSettings settings)
    {
        settings.KeyboardOpacity = Opacity;
        settings.KeyboardLeft = Left;
        settings.KeyboardTop = Top;
        settings.KeyboardWidth = Width;
        settings.KeyboardHeight = Height;
        settings.KeyboardPlaced = true;
    }

    private void Type(KeyCell cell, List<OutputCommand> commands)
    {
        if (cell.Role != KeyRole.Character)
        {
            _undo = null;
            Presses++;
            switch (cell.Role)
            {
                case KeyRole.Shift:
                    ShiftOnce = !ShiftOnce;
                    break;
                case KeyRole.Caps:
                    CapsLock = !CapsLock;
                    break;
                case KeyRole.Fn:
                    FnLock = !FnLock;
                    break;
                case KeyRole.Ctrl:
                    CtrlLock = !CtrlLock;
                    break;
                case KeyRole.Alt:
                    AltLock = !AltLock;
                    break;
                case KeyRole.Win:
                    if (WinLock)
                    {
                        WinLock = false;
                        EmitStroke(commands, KeyCatalog.LWin, false);
                    }
                    else
                    {
                        WinLock = true;
                    }

                    break;
            }

            return;
        }

        if (CtrlLock || AltLock || WinLock)
        {
            _undo = null;
            Presses++;
            bool wipe = CtrlLock && !AltLock && !WinLock && Is(cell.Stroke, KeyCatalog.Backspace);
            EmitChord(commands, cell);
            if (wipe)
                _prefix = "";
            Refresh();
            return;
        }

        if (Is(cell.Stroke, KeyCatalog.Space))
        {
            TypeSpace(commands);
            return;
        }

        if (Is(cell.Stroke, KeyCatalog.Enter))
        {
            TypeEnter(commands);
            return;
        }

        if (Is(cell.Stroke, KeyCatalog.Backspace))
        {
            Backspace(commands);
            return;
        }

        _undo = null;
        Presses++;
        if (FnLock && cell.FnStroke != null)
        {
            EmitStroke(commands, cell.FnStroke, ShiftOnce && cell.FnFollowsShift);
            if (cell.FnFollowsShift)
                ShiftOnce = false;
            return;
        }

        var produced = Produce(cell);
        if (produced is char letter)
        {
            if (IsWordChar(letter))
            {
                EmitChar(commands, letter);
                _prefix += letter;
                if (cell.IsLetter || cell.Shifted != null)
                    ShiftOnce = false;
                Refresh();
                return;
            }

            Commit();
            if (letter is '.' or '!' or '?')
                _previous = "";
            EmitChar(commands, letter);
            ShiftOnce = false;
            Refresh();
            return;
        }

        if (cell.Stroke == null)
            return;
        EmitStroke(commands, cell.Stroke, ShiftOnce);
        ShiftOnce = false;
    }

    private void Undo(List<OutputCommand> commands)
    {
        var stamp = _undo;
        _undo = null;
        if (stamp == null)
            return;
        Presses++;
        for (int i = 0; i < stamp.Delete; i++)
            EmitStroke(commands, KeyCatalog.Backspace, false);
        foreach (var letter in stamp.Retype)
            EmitChar(commands, letter);
        if (stamp.Learned)
            Memory.Unlearn(stamp.LearnedAfter, stamp.LearnedWord);
        _prefix = stamp.Prefix;
        _previous = stamp.Previous;
        _history.Clear();
        _history.AddRange(stamp.History);
        ShiftOnce = false;
        Refresh();
    }

    private void Commit()
    {
        if (_prefix.Length == 0)
            return;
        var token = _prefix;
        _prefix = "";
        if (!token.Any(char.IsLetter))
            return;
        bool learned = Memory.Learn(_previous, token);
        _history.Add(new Committed { Text = token, Previous = _previous, Learned = learned });
        if (_history.Count > 40)
            _history.RemoveAt(0);
        _previous = token.ToLowerInvariant();
    }

    private void Refresh()
    {
        var picked = Memory.Suggest(_prefix, _previous, 4);
        _shown.Clear();
        foreach (var word in picked)
            _shown.Add(ApplyCase(word));
        if (_prefix.Length >= 2 && !_shown.Exists(item => item.Equals(_prefix, StringComparison.OrdinalIgnoreCase)))
        {
            if (_shown.Count >= 4)
                _shown[3] = _prefix;
            else
                _shown.Add(_prefix);
        }

        _index = 0;
        if (Row < 0)
        {
            if (_shown.Count == 0)
            {
                Row = _homeRow;
                Column = _homeColumn;
            }
            else
            {
                Column = 0;
            }
        }
    }

    private string ApplyCase(string lower)
    {
        if (lower.Length == 0)
            return lower;
        if (IsAllCaps(_prefix))
            return lower.ToUpperInvariant();
        bool capital = (_prefix.Length > 0 && char.IsUpper(_prefix[0])) || (_prefix.Length == 0 && (ShiftOnce || CapsLock));
        if (!capital)
            return lower;
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static bool IsAllCaps(string text)
    {
        int letters = 0;
        foreach (var letter in text)
        {
            if (!char.IsLetter(letter))
                continue;
            if (!char.IsUpper(letter))
                return false;
            letters++;
        }

        return letters >= 2;
    }

    private char? Produce(KeyCell cell)
    {
        if (cell.IsLetter)
        {
            char lower = cell.Unshifted ?? char.ToLowerInvariant(cell.Label[0]);
            return CapsLock ^ ShiftOnce ? char.ToUpperInvariant(lower) : lower;
        }

        if (ShiftOnce && cell.Shifted is char shifted)
            return shifted;
        return cell.Unshifted;
    }

    private void EmitChord(List<OutputCommand> commands, KeyCell cell)
    {
        if (cell.Stroke == null)
        {
            CtrlLock = false;
            AltLock = false;
            WinLock = false;
            ShiftOnce = false;
            return;
        }

        if (CtrlLock)
            Down(commands, KeyCatalog.LCtrl);
        if (AltLock)
            Down(commands, KeyCatalog.LAlt);
        if (WinLock)
            Down(commands, KeyCatalog.LWin);
        if (ShiftOnce)
            Down(commands, KeyCatalog.LShift);
        Down(commands, cell.Stroke);
        Up(commands, cell.Stroke);
        if (ShiftOnce)
            Up(commands, KeyCatalog.LShift);
        if (WinLock)
            Up(commands, KeyCatalog.LWin);
        if (AltLock)
            Up(commands, KeyCatalog.LAlt);
        if (CtrlLock)
            Up(commands, KeyCatalog.LCtrl);
        CtrlLock = false;
        AltLock = false;
        WinLock = false;
        ShiftOnce = false;
    }

    private void MoveHorizontal(int delta)
    {
        if (Row < 0)
        {
            CycleSuggestion(delta);
            return;
        }

        int count = _rows[Row].Count;
        Column = (Column + delta % count + count) % count;
    }

    private void MoveVertical(int delta)
    {
        if (delta < 0)
        {
            if (Row < 0)
                return;
            if (Row == 0)
            {
                EnterBar();
                return;
            }

            int target = Row - 1;
            Column = Closest(target, Center(Row, Column));
            Row = target;
            return;
        }

        if (Row < 0)
        {
            LeaveBar();
            return;
        }

        if (Row >= _rows.Count - 1)
            return;
        int next = Row + 1;
        Column = Closest(next, Center(Row, Column));
        Row = next;
    }

    private void EnterBar()
    {
        if (_shown.Count == 0)
            return;
        double width = _rows[0].Sum(cell => cell.Span);
        double center = Center(Row, Column);
        _index = Math.Clamp((int)(center / width * _shown.Count), 0, _shown.Count - 1);
        Row = -1;
        Column = _index;
    }

    private void LeaveBar()
    {
        double width = _rows[0].Sum(cell => cell.Span);
        double center = (_index + 0.5) / Math.Max(1, _shown.Count) * width;
        Row = 0;
        Column = Closest(0, center);
    }

    private int Closest(int row, double center)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        double x = 0;
        var cells = _rows[row];
        for (int i = 0; i < cells.Count; i++)
        {
            double middle = x + (cells[i].Span / 2);
            double distance = Math.Abs(middle - center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }

            x += cells[i].Span;
        }

        return best;
    }

    private double Center(int row, int column)
    {
        double x = 0;
        var cells = _rows[row];
        for (int i = 0; i < column; i++)
            x += cells[i].Span;
        return x + (cells[column].Span / 2);
    }

    private (int Row, int Column) Find(string label)
    {
        for (int row = 0; row < _rows.Count; row++)
        {
            for (int column = 0; column < _rows[row].Count; column++)
            {
                if (_rows[row][column].Label.Equals(label, StringComparison.OrdinalIgnoreCase))
                    return (row, column);
            }
        }

        return (-1, -1);
    }

    private void ResetTyping()
    {
        Row = _homeRow;
        Column = _homeColumn;
        ShiftOnce = false;
        CapsLock = false;
        FnLock = false;
        CtrlLock = false;
        AltLock = false;
        WinLock = false;
        _prefix = "";
        _previous = "";
        _history.Clear();
        _undo = null;
        _shown.Clear();
        _index = 0;
    }

    private static bool IsWordChar(char letter) => char.IsLetterOrDigit(letter) || letter is '\'' or '-';

    private static bool Is(KeyStroke? stroke, KeyStroke other) =>
        stroke != null && stroke.ScanCode == other.ScanCode && stroke.Extended == other.Extended;

    private static void EmitChar(List<OutputCommand> commands, char letter)
    {
        if (TryStroke(letter, out var stroke, out var shift))
            EmitStroke(commands, stroke, shift);
    }

    private static void EmitStroke(List<OutputCommand> commands, KeyStroke stroke, bool shift)
    {
        if (shift)
            Down(commands, KeyCatalog.LShift);
        Down(commands, stroke);
        Up(commands, stroke);
        if (shift)
            Up(commands, KeyCatalog.LShift);
    }

    private static void Down(List<OutputCommand> commands, KeyStroke stroke) => commands.Add(OutputCommand.KeyDown(stroke));

    private static void Up(List<OutputCommand> commands, KeyStroke stroke) => commands.Add(OutputCommand.KeyUp(stroke));

    private static bool TryStroke(char letter, out KeyStroke stroke, out bool shift)
    {
        shift = false;
        if (char.IsLetter(letter))
        {
            stroke = KeyCatalog.Letter(letter);
            shift = char.IsUpper(letter);
            return true;
        }

        if (char.IsDigit(letter))
        {
            stroke = KeyCatalog.Digit(letter);
            return true;
        }

        switch (letter)
        {
            case ' ': stroke = KeyCatalog.Space; return true;
            case '!': stroke = KeyCatalog.Digit('1'); shift = true; return true;
            case '@': stroke = KeyCatalog.Digit('2'); shift = true; return true;
            case '#': stroke = KeyCatalog.Digit('3'); shift = true; return true;
            case '$': stroke = KeyCatalog.Digit('4'); shift = true; return true;
            case '%': stroke = KeyCatalog.Digit('5'); shift = true; return true;
            case '^': stroke = KeyCatalog.Digit('6'); shift = true; return true;
            case '&': stroke = KeyCatalog.Digit('7'); shift = true; return true;
            case '*': stroke = KeyCatalog.Digit('8'); shift = true; return true;
            case '(': stroke = KeyCatalog.Digit('9'); shift = true; return true;
            case ')': stroke = KeyCatalog.Digit('0'); shift = true; return true;
            case '-': stroke = KeyCatalog.Minus; return true;
            case '_': stroke = KeyCatalog.Minus; shift = true; return true;
            case '=': stroke = KeyCatalog.EqualsKey; return true;
            case '+': stroke = KeyCatalog.EqualsKey; shift = true; return true;
            case '[': stroke = KeyCatalog.LBracket; return true;
            case '{': stroke = KeyCatalog.LBracket; shift = true; return true;
            case ']': stroke = KeyCatalog.RBracket; return true;
            case '}': stroke = KeyCatalog.RBracket; shift = true; return true;
            case '\\': stroke = KeyCatalog.Backslash; return true;
            case '|': stroke = KeyCatalog.Backslash; shift = true; return true;
            case ';': stroke = KeyCatalog.Semicolon; return true;
            case ':': stroke = KeyCatalog.Semicolon; shift = true; return true;
            case '\'': stroke = KeyCatalog.Apostrophe; return true;
            case '"': stroke = KeyCatalog.Apostrophe; shift = true; return true;
            case ',': stroke = KeyCatalog.Comma; return true;
            case '<': stroke = KeyCatalog.Comma; shift = true; return true;
            case '.': stroke = KeyCatalog.Period; return true;
            case '>': stroke = KeyCatalog.Period; shift = true; return true;
            case '/': stroke = KeyCatalog.Slash; return true;
            case '?': stroke = KeyCatalog.Slash; shift = true; return true;
            case '`': stroke = KeyCatalog.Grave; return true;
            case '~': stroke = KeyCatalog.Grave; shift = true; return true;
            default:
                stroke = KeyCatalog.Space;
                return false;
        }
    }

    private sealed class Committed
    {
        public string Text { get; init; } = "";
        public string Previous { get; init; } = "";
        public bool Learned { get; init; }
        public Committed Copy() => new() { Text = Text, Previous = Previous, Learned = Learned };
    }

    private sealed class Stamp
    {
        public string Prefix { get; init; } = "";
        public string Previous { get; init; } = "";
        public List<Committed> History { get; init; } = [];
        public int Delete { get; init; }
        public string Retype { get; init; } = "";
        public bool Learned { get; set; }
        public string LearnedWord { get; init; } = "";
        public string LearnedAfter { get; init; } = "";
    }
}
