namespace Win2DS5.Core;

public sealed class ButtonChoice
{
    public ButtonChoice(string code, string label)
    {
        Code = code;
        Label = label;
    }

    public string Code { get; }
    public string Label { get; }
    public override string ToString() => Label;
}

public static class ButtonCatalog
{
    public static IReadOnlyList<(string Group, IReadOnlyList<ButtonChoice> Actions)> Tree { get; } = Build();

    public static ButtonChoice? Find(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        foreach (var group in Tree)
        {
            foreach (var action in group.Actions)
            {
                if (action.Code == code)
                    return action;
            }
        }

        return null;
    }

    public static string? Match(Binding? binding)
    {
        if (binding == null || binding.Kind == BindingKind.None)
            return null;
        return binding.Kind switch
        {
            BindingKind.Mouse => binding.Mouse switch
            {
                MouseButton.Left => "mouse.left",
                MouseButton.Right => "mouse.right",
                MouseButton.Middle => "mouse.middle",
                MouseButton.X1 => "mouse.back",
                MouseButton.X2 => "mouse.forward",
                _ => null,
            },
            BindingKind.Scroll => binding.ScrollY > 0 ? "mouse.up" : binding.ScrollY < 0 ? "mouse.down" : binding.ScrollX < 0 ? "mouse.scroll-left" : "mouse.scroll-right",
            BindingKind.MouseMove => "mouse.move",
            BindingKind.Key => MatchKey(binding),
            BindingKind.LayerHold or BindingKind.LayerToggle => "app.layer",
            BindingKind.ToggleKeyboard => "app.keyboard",
            BindingKind.Profile => "app.profile",
            BindingKind.Pause or BindingKind.Resume => "app.pause",
            BindingKind.System => binding.Action,
            BindingKind.StickNudge => binding.Action.Length == 0 ? null : binding.Action,
            BindingKind.Pad => binding.Action.Length == 0 ? InferPad(binding) : binding.Action,
            _ => null,
        };
    }

    public static void Aim(Binding binding, IReadOnlyList<OutputDevice> outputs)
    {
        if (binding.Kind is BindingKind.Key)
            AimKind(binding, outputs, OutputDeviceKind.Keyboard);
        else if (binding.Kind is BindingKind.Mouse or BindingKind.Scroll or BindingKind.MouseMove)
            AimKind(binding, outputs, OutputDeviceKind.Mouse);
        else if (binding.Kind is BindingKind.Pad or BindingKind.StickNudge)
        {
            var kind = binding.OutputKind == OutputDeviceKind.Xbox ? OutputDeviceKind.Xbox : OutputDeviceKind.DualShock4;
            var live = binding.Targets?.Where(id => outputs.Any(output => output.Id == id)).ToList() ?? [];
            if (live.Count > 0)
            {
                binding.Targets = live;
                binding.OutputId = live[0];
                var chosen = outputs.First(output => output.Id == binding.OutputId);
                binding.OutputKnown = true;
                binding.OutputKind = chosen.Kind;
                binding.OutputSlot = chosen.Slot;
                return;
            }

            AimKind(binding, outputs, kind);
            if (!string.IsNullOrEmpty(binding.OutputId))
                binding.Targets = [binding.OutputId];
        }
    }

    private static void AimKind(Binding binding, IReadOnlyList<OutputDevice> outputs, OutputDeviceKind kind)
    {
        if (!string.IsNullOrEmpty(binding.OutputId) && outputs.Any(output => output.Id == binding.OutputId))
            return;
        var match = outputs.FirstOrDefault(output => output.Kind == kind && output.Slot == binding.OutputSlot)
            ?? outputs.FirstOrDefault(output => output.Kind == kind);
        if (match == null)
            return;
        binding.OutputId = match.Id;
        binding.OutputKnown = true;
        binding.OutputKind = match.Kind;
        binding.OutputSlot = match.Slot;
    }

    public static bool Configurable(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return false;
        return code is "mouse.up" or "mouse.down" or "mouse.scroll-left" or "mouse.scroll-right" or "mouse.move"
            or "media.volume-custom" or "app.layer" or "app.profile" or "windows.open"
            || PadKind(code) != null;
    }

    public static OutputDeviceKind? PadKind(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        if (code.StartsWith("ds4.", StringComparison.Ordinal))
            return OutputDeviceKind.DualShock4;
        if (code.StartsWith("xbox.", StringComparison.Ordinal))
            return OutputDeviceKind.Xbox;
        return null;
    }

    public static bool IsEdgeAction(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return false;
        var sample = Create(code);
        return sample.Kind is BindingKind.Key or BindingKind.Mouse;
    }

    public static string StepLabel(MacroStep step)
    {
        var choice = Find(step.Code);
        string name = choice?.Label ?? step.Code;
        if (!IsEdgeAction(step.Code))
            return name;
        if (step.Both)
            return name + " · Both";
        return name + (step.Up ? " · Release" : " · Downpress");
    }

    private static string? InferPad(Binding binding)
    {
        string prefix = binding.OutputKind == OutputDeviceKind.Xbox ? "xbox" : "ds4";
        string suffix = binding.PadButton switch
        {
            ControlId.DPadLeft => "left",
            ControlId.DPadRight => "right",
            ControlId.DPadUp => "up",
            ControlId.DPadDown => "down",
            ControlId.Cross => prefix == "xbox" ? "a" : "cross",
            ControlId.Circle => prefix == "xbox" ? "b" : "circle",
            ControlId.Square => prefix == "xbox" ? "x" : "square",
            ControlId.Triangle => prefix == "xbox" ? "y" : "triangle",
            ControlId.L1 => prefix == "xbox" ? "lb" : "l1",
            ControlId.R1 => prefix == "xbox" ? "rb" : "r1",
            ControlId.L2 => prefix == "xbox" ? "lt" : "l2",
            ControlId.R2 => prefix == "xbox" ? "rt" : "r2",
            ControlId.L3 => prefix == "xbox" ? "ls" : "l3",
            ControlId.R3 => prefix == "xbox" ? "rs" : "r3",
            ControlId.Create => prefix == "xbox" ? "view" : "share",
            ControlId.Options => prefix == "xbox" ? "menu" : "options",
            ControlId.Ps => prefix == "xbox" ? "xbox" : "ps",
            ControlId.Mute => "mute",
            ControlId.TouchpadClick => prefix == "xbox" ? "xbox-share" : "touch-center",
            _ => "",
        };
        return suffix.Length == 0 ? null : prefix + "." + suffix;
    }

    public static Binding Create(string code)
    {
        var binding = code switch
        {
            "mouse.left" => Binding.MouseClick(MouseButton.Left),
            "mouse.right" => Binding.MouseClick(MouseButton.Right),
            "mouse.middle" => Binding.MouseClick(MouseButton.Middle),
            "mouse.back" => Binding.MouseClick(MouseButton.X1),
            "mouse.forward" => Binding.MouseClick(MouseButton.X2),
            "mouse.up" => Binding.Scroll(0, 120),
            "mouse.down" => Binding.Scroll(0, -120),
            "mouse.scroll-left" => Binding.Scroll(-120, 0),
            "mouse.scroll-right" => Binding.Scroll(120, 0),
            "mouse.move" => new Binding { Kind = BindingKind.MouseMove, ScrollX = 0, ScrollY = 0 },
            "app.layer" => Binding.Hold(""),
            "app.keyboard" => Binding.Keyboard(),
            "app.profile" => Binding.SwitchProfile(""),
            "app.pause" or "app.resume" => new Binding { Kind = BindingKind.Pause, Action = "Toggle Win2DS5" },
            "media.volume-custom" => new Binding { Kind = BindingKind.System, Action = code, ScrollY = 50 },
            "windows.open" => new Binding { Kind = BindingKind.System, Action = code },
            _ when code.StartsWith("key.", StringComparison.Ordinal) => Binding.Key(KeyCatalog.Find(code["key.".Length..])),
            _ when code.StartsWith("media.", StringComparison.Ordinal) => Binding.Key(KeyCatalog.Find(MediaName(code))),
            _ when code.StartsWith("windows.", StringComparison.Ordinal) => WindowsBinding(code),
            _ when code.StartsWith("ds4.", StringComparison.Ordinal) => Pad(code, OutputDeviceKind.DualShock4),
            _ when code.StartsWith("xbox.", StringComparison.Ordinal) => Pad(code, OutputDeviceKind.Xbox),
            _ => new Binding { Kind = BindingKind.None },
        };
        binding.Action = code.StartsWith("ds4.", StringComparison.Ordinal) || code.StartsWith("xbox.", StringComparison.Ordinal) || code.StartsWith("app.", StringComparison.Ordinal) || code.StartsWith("windows.", StringComparison.Ordinal) || code.StartsWith("media.", StringComparison.Ordinal)
            ? code
            : binding.Action;
        if (code.StartsWith("ds4.", StringComparison.Ordinal) || code.StartsWith("xbox.", StringComparison.Ordinal))
            binding.Action = code;
        return binding;
    }

    private static string MatchKey(Binding binding)
    {
        if (binding.Keys.Count == 1)
        {
            var key = binding.Keys[0];
            foreach (var group in Tree)
            {
                foreach (var action in group.Actions)
                {
                    if (!action.Code.StartsWith("key.", StringComparison.Ordinal) && !action.Code.StartsWith("media.", StringComparison.Ordinal))
                        continue;
                    var name = action.Code.StartsWith("key.", StringComparison.Ordinal) ? action.Code["key.".Length..] : MediaName(action.Code);
                    var found = KeyCatalog.All.FirstOrDefault(item => item.Name == name);
                    if (found != null && found.ScanCode == key.ScanCode && found.Extended == key.Extended && found.Name == key.Name)
                        return action.Code;
                }
            }
        }

        foreach (var action in Tree.First(group => group.Group == "Windows Controls").Actions)
        {
            if (action.Code == "windows.open")
                continue;
            var sample = Create(action.Code);
            if (sample.Kind == BindingKind.Key && SameKeys(sample.Keys, binding.Keys))
                return action.Code;
        }

        return binding.Keys.Count == 1 ? "key." + binding.Keys[0].Name : "";
    }

    private static bool SameKeys(List<KeyStroke> left, List<KeyStroke> right)
    {
        if (left.Count != right.Count)
            return false;
        for (int i = 0; i < left.Count; i++)
        {
            if (left[i].ScanCode != right[i].ScanCode || left[i].Extended != right[i].Extended)
                return false;
        }

        return true;
    }

    private static Binding WindowsBinding(string code) => code switch
    {
        "windows.desktop" => Chord(KeyCatalog.LWin, KeyCatalog.Letter('D')),
        "windows.task" => Chord(KeyCatalog.LCtrl, KeyCatalog.LShift, KeyCatalog.Esc),
        "windows.close" => Chord(KeyCatalog.LAlt, KeyCatalog.Find("F4")),
        "windows.cut" => Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('X')),
        "windows.copy" => Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('C')),
        "windows.paste" => Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('V')),
        "windows.settings" => Chord(KeyCatalog.LWin, KeyCatalog.Letter('I')),
        _ => new Binding { Kind = BindingKind.System, Action = code },
    };

    private static Binding Chord(params KeyStroke[] keys)
    {
        var binding = Binding.Key(keys);
        return binding;
    }

    private static Binding Pad(string code, OutputDeviceKind kind)
    {
        var binding = Binding.Pad(PadButton(code));
        binding.Action = code;
        binding.OutputKnown = true;
        binding.OutputKind = kind;
        binding.OutputSlot = 1;
        if (code.Contains(".analog-", StringComparison.Ordinal))
        {
            binding.Kind = BindingKind.StickNudge;
            binding.ScrollX = code.Contains(".left-analog-", StringComparison.Ordinal) ? 0 : 1;
            binding.ScrollY = code.EndsWith("-left", StringComparison.Ordinal) ? -1
                : code.EndsWith("-right", StringComparison.Ordinal) ? 1
                : code.EndsWith("-up", StringComparison.Ordinal) ? 1
                : -1;
        }

        return binding;
    }

    private static ControlId PadButton(string code)
    {
        var name = code[(code.IndexOf('.') + 1)..];
        return name switch
        {
            "left" => ControlId.DPadLeft,
            "right" => ControlId.DPadRight,
            "up" => ControlId.DPadUp,
            "down" => ControlId.DPadDown,
            "cross" or "a" => ControlId.Cross,
            "square" or "x" => ControlId.Square,
            "circle" or "b" => ControlId.Circle,
            "triangle" or "y" => ControlId.Triangle,
            "l1" or "lb" => ControlId.L1,
            "r1" or "rb" => ControlId.R1,
            "l2" or "lt" => ControlId.L2,
            "r2" or "rt" => ControlId.R2,
            "l3" or "ls" => ControlId.L3,
            "r3" or "rs" => ControlId.R3,
            "share" => ControlId.Create,
            "options" or "view" or "menu" => name == "view" ? ControlId.Create : ControlId.Options,
            "ps" or "xbox" => ControlId.Ps,
            "mute" => ControlId.Mute,
            "touch-center" or "touch-left" or "touch-right" or "touch-top" => ControlId.TouchpadClick,
            "xbox-share" => ControlId.TouchpadClick,
            _ => ControlId.None,
        };
    }

    private static string MediaName(string code) => code switch
    {
        "media.play" => "Media Play",
        "media.seek-forward" => "Media Next",
        "media.seek-back" => "Media Previous",
        "media.next" => "Media Next",
        "media.previous" => "Media Previous",
        "media.up" => "Volume Up",
        "media.down" => "Volume Down",
        "media.mute" => "Volume Mute",
        _ => "",
    };

    private static List<(string Group, IReadOnlyList<ButtonChoice> Actions)> Build()
    {
        ButtonChoice[] keys =
        [
            .. Digits(), .. Letters(),
            Key(","), Key("."), Key("/"), Key(";"), Key("'"), Key("["), Key("]"), Key("\\"), Key("-"), Key("="), Key("`"),
            Named("Space", "Space"), Named("Backspace", "Backspace"), Named("Enter", "Enter"), Named("Escape", "Esc"),
            Named("Tab", "Tab"), Named("Caps Lock", "Caps Lock"),
            Named("Left Shift", "Left Shift"), Named("Right Shift", "Right Shift"),
            Named("Left Ctrl", "Left Ctrl"), Named("Right Ctrl", "Right Ctrl"),
            Named("Left Alt", "Left Alt"), Named("Right Alt", "Right Alt"),
            Named("Fn (Function Key)", "Fn"),
            Named("Insert", "Insert"), Named("Home", "Home"), Named("End", "End"), Named("Delete", "Delete"),
            Named("Page Up", "Page Up"), Named("Page Down", "Page Down"),
            Named("Print Screen", "Print Screen"), Named("Scroll Lock", "Scroll Lock"), Named("Pause Break", "Pause"),
            Named("Number Lock", "Num Lock"),
            Named("Number Pad - /", "Numpad Divide"), Named("Number Pad - *", "Numpad Multiply"),
            Named("Number Pad - -", "Numpad Minus"), Named("Number Pad - +", "Numpad Plus"),
            Named("Number Pad - Enter", "Numpad Enter"), Named("Number Pad - Delete", "Numpad Decimal"),
            .. Enumerable.Range(0, 10).Select(i => Named($"Number Pad - {i}", $"Numpad {i}")),
            Named("Left Arrow", "Left"), Named("Right Arrow", "Right"), Named("Up Arrow", "Up"), Named("Down Arrow", "Down"),
        ];
        return
        [
            ("Mouse Controls", [
                new("mouse.left", "Left Click"), new("mouse.right", "Right Click"), new("mouse.middle", "Middle Click"),
                new("mouse.up", "Scroll Up"), new("mouse.down", "Scroll Down"), new("mouse.scroll-left", "Scroll Left"), new("mouse.scroll-right", "Scroll Right"),
                new("mouse.back", "Side Button - Back"), new("mouse.forward", "Side Button - Forward"), new("mouse.move", "Mouse Move"),
            ]),
            ("Keyboard Controls", keys),
            ("Media Controls", [
                new("media.play", "Play/Pause"), new("media.seek-forward", "Seek Forward"), new("media.seek-back", "Seek Backward"),
                new("media.next", "Next"), new("media.previous", "Previous"),
                new("media.up", "Volume Up"), new("media.down", "Volume Down"), new("media.volume-custom", "Volume Custom"), new("media.mute", "Mute"),
            ]),
            ("Win2DS5 Controls", [
                new("app.layer", "Layer Modifier Button"), new("app.keyboard", "On-Screen Keyboard"), new("app.profile", "Profiles"),
                new("app.pause", "Toggle Win2DS5"),
            ]),
            ("DS4 Controls", PadGroup("ds4")),
            ("XBOX Controls", XboxGroup()),
            ("Windows Controls", [
                new("windows.desktop", "Show/Hide Desktop"), new("windows.task", "Task Manager"), new("windows.open", "Open"),
                new("windows.close", "Close Window (Alt+F4)"), new("windows.cut", "Cut (Ctrl+X)"), new("windows.copy", "Copy (Ctrl+C)"),
                new("windows.paste", "Paste (Ctrl+V)"), new("windows.settings", "Settings"), new("windows.sleep", "Sleep"),
                new("windows.restart", "Restart"), new("windows.shutdown", "Shut Down"),
            ]),
        ];
    }

    private static ButtonChoice[] Digits() => Enumerable.Range(0, 10).Select(i => new ButtonChoice("key." + i, i.ToString())).ToArray();

    private static ButtonChoice[] Letters() => Enumerable.Range('A', 26).Select(c => new ButtonChoice("key." + (char)c, ((char)c).ToString())).ToArray();

    private static ButtonChoice Key(string face) => face switch
    {
        "," => new("key.Comma", ","),
        "." => new("key.Period", "."),
        "/" => new("key.Slash", "/"),
        ";" => new("key.Semicolon", ";"),
        "'" => new("key.Apostrophe", "'"),
        "[" => new("key.Left Bracket", "["),
        "]" => new("key.Right Bracket", "]"),
        "\\" => new("key.Backslash", "\\"),
        "-" => new("key.Minus", "-"),
        "=" => new("key.Equals", "="),
        "`" => new("key.Grave", "`"),
        _ => new("key." + face, face),
    };

    private static ButtonChoice Named(string label, string keyName) => new("key." + keyName, label);

    private static ButtonChoice[] PadGroup(string prefix) =>
    [
        new($"{prefix}.left", "D-Pad Left"), new($"{prefix}.right", "D-Pad Right"), new($"{prefix}.up", "D-Pad Up"), new($"{prefix}.down", "D-Pad Down"),
        new($"{prefix}.cross", "Cross"), new($"{prefix}.square", "Square"), new($"{prefix}.circle", "Circle"), new($"{prefix}.triangle", "Triangle"),
        new($"{prefix}.l1", "L1"), new($"{prefix}.r1", "R1"), new($"{prefix}.l2", "L2"), new($"{prefix}.r2", "R2"), new($"{prefix}.l3", "L3"), new($"{prefix}.r3", "R3"),
        new($"{prefix}.left-analog-left", "Left Analog Left"), new($"{prefix}.left-analog-right", "Left Analog Right"),
        new($"{prefix}.left-analog-up", "Left Analog Up"), new($"{prefix}.left-analog-down", "Left Analog Down"),
        new($"{prefix}.right-analog-left", "Right Analog Left"), new($"{prefix}.right-analog-right", "Right Analog Right"),
        new($"{prefix}.right-analog-up", "Right Analog Up"), new($"{prefix}.right-analog-down", "Right Analog Down"),
        new($"{prefix}.share", "Share"), new($"{prefix}.options", "Options"), new($"{prefix}.ps", "PS Button"), new($"{prefix}.mute", "Mute"),
        new($"{prefix}.touch-center", "Touchpad - Center Click"), new($"{prefix}.touch-left", "Touchpad - Left Click"),
        new($"{prefix}.touch-right", "Touchpad - Right Click"), new($"{prefix}.touch-top", "Touchpad - Top Click"),
    ];

    private static ButtonChoice[] XboxGroup() =>
    [
        new("xbox.left", "D-Pad Left"), new("xbox.right", "D-Pad Right"), new("xbox.up", "D-Pad Up"), new("xbox.down", "D-Pad Down"),
        new("xbox.a", "A"), new("xbox.b", "B"), new("xbox.x", "X"), new("xbox.y", "Y"),
        new("xbox.lb", "LB (Left Bumper)"), new("xbox.rb", "RB (Right Bumper)"), new("xbox.lt", "LT (Left Trigger)"), new("xbox.rt", "RT (Right Trigger)"),
        new("xbox.ls", "LS (Left Stick)"), new("xbox.rs", "RS (Right Stick)"),
        new("xbox.left-analog-left", "Left Analog Left"), new("xbox.left-analog-right", "Left Analog Right"),
        new("xbox.left-analog-up", "Left Analog Up"), new("xbox.left-analog-down", "Left Analog Down"),
        new("xbox.right-analog-left", "Right Analog Left"), new("xbox.right-analog-right", "Right Analog Right"),
        new("xbox.right-analog-up", "Right Analog Up"), new("xbox.right-analog-down", "Right Analog Down"),
        new("xbox.view", "View"), new("xbox.menu", "Menu"), new("xbox.xbox", "XBOX Button"), new("xbox.xbox-share", "Share"),
    ];
}
