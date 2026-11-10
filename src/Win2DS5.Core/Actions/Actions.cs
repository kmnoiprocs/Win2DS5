using System.Text.Json.Serialization;

namespace Win2DS5.Core;

public enum BindingKind
{
    None,
    Key,
    Mouse,
    Scroll,
    [JsonStringEnumMemberName("LayerHold")]
    LayoutHold,
    [JsonStringEnumMemberName("LayerToggle")]
    LayoutToggle,
    ToggleKeyboard,
    UseBase,
    Pad,
    Profile,
    MouseMove,
    StickNudge,
    Pause,
    Resume,
    System,
}

public enum MouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

public enum OutputKind
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,
    MouseMove,
    Scroll,
    PadDown,
    PadUp,
    LeftStick,
    RightStick,
}

public sealed class KeyStroke
{
    public string Name { get; set; } = "";
    public ushort ScanCode { get; set; }
    public bool Extended { get; set; }
}

public enum EdgeKind
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,
}

public sealed class EdgeStep
{
    public EdgeKind Kind { get; set; }
    public KeyStroke Key { get; set; } = new();
    public MouseButton Mouse { get; set; }
}

public sealed class MacroStep
{
    public string Code { get; set; } = "";
    public bool Up { get; set; }
    public bool Both { get; set; }
    public int ScrollX { get; set; }
    public int ScrollY { get; set; }
    [JsonPropertyName("layerId")]
    public string LayoutId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Targets { get; set; } = [];
    [JsonPropertyName("layerToggle")]
    public bool LayoutToggle { get; set; }
}

public sealed class Binding
{
    public BindingKind Kind { get; set; }
    public List<KeyStroke> Keys { get; set; } = [];
    public MouseButton Mouse { get; set; }
    public int ScrollX { get; set; }
    public int ScrollY { get; set; }
    [JsonPropertyName("layerId")]
    public string? LayoutId { get; set; }
    public string ProfileId { get; set; } = "";
    public ControlId PadButton { get; set; }
    public Binding? Tap { get; set; }
    public int TapMs { get; set; } = 350;
    public bool OverrideDelay { get; set; }
    public bool Advanced { get; set; }
    public List<EdgeStep> PressSteps { get; set; } = [];
    public List<EdgeStep> ReleaseSteps { get; set; } = [];
    public List<MacroStep> DownMacros { get; set; } = [];
    public List<MacroStep> UpMacros { get; set; } = [];
    public string OutputId { get; set; } = "";
    public bool OutputKnown { get; set; }
    public OutputDeviceKind OutputKind { get; set; }
    public int OutputSlot { get; set; }
    public string Action { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Targets { get; set; } = [];

    public static Binding Key(params KeyStroke[] keys) => new() { Kind = BindingKind.Key, Keys = keys.ToList() };
    public static Binding MouseClick(MouseButton button) => new() { Kind = BindingKind.Mouse, Mouse = button };
    public static Binding Scroll(int x, int y) => new() { Kind = BindingKind.Scroll, ScrollX = x, ScrollY = y };
    public static Binding Hold(string layoutId, Binding? tap = null, int tapMs = 220) =>
        new() { Kind = BindingKind.LayoutHold, LayoutId = layoutId, Tap = tap, TapMs = tapMs };
    public static Binding Toggle(string layoutId) => new() { Kind = BindingKind.LayoutToggle, LayoutId = layoutId };
    public static Binding Keyboard() => new() { Kind = BindingKind.ToggleKeyboard };
    public static Binding Base() => new() { Kind = BindingKind.UseBase };
    public static Binding Pad(ControlId button) => new() { Kind = BindingKind.Pad, PadButton = button };
    public static Binding SwitchProfile(string profileId) => new() { Kind = BindingKind.Profile, ProfileId = profileId };

    public bool HasCustomSteps() =>
        Advanced && ((PressSteps?.Count ?? 0) > 0 || (ReleaseSteps?.Count ?? 0) > 0 || (DownMacros?.Count ?? 0) > 0 || (UpMacros?.Count ?? 0) > 0);
}

public readonly record struct OutputCommand(OutputKind Kind, int A, int B, string OutputId = "")
{
    public static OutputCommand KeyDown(KeyStroke key) => new(OutputKind.KeyDown, key.ScanCode, key.Extended ? 1 : 0);
    public static OutputCommand KeyUp(KeyStroke key) => new(OutputKind.KeyUp, key.ScanCode, key.Extended ? 1 : 0);
    public static OutputCommand KeyUp(ushort scan, bool extended) => new(OutputKind.KeyUp, scan, extended ? 1 : 0);
    public static OutputCommand MouseDown(MouseButton button) => new(OutputKind.MouseDown, (int)button, 0);
    public static OutputCommand MouseUp(MouseButton button) => new(OutputKind.MouseUp, (int)button, 0);
    public static OutputCommand Move(int dx, int dy) => new(OutputKind.MouseMove, dx, dy);
    public static OutputCommand Wheel(int dx, int dy) => new(OutputKind.Scroll, dx, dy);
    public static OutputCommand PadDown(ControlId button) => new(OutputKind.PadDown, (int)button, 0);
    public static OutputCommand PadUp(ControlId button) => new(OutputKind.PadUp, (int)button, 0);
    public static OutputCommand LeftStick(int x, int y) => new(OutputKind.LeftStick, x, y);
    public static OutputCommand RightStick(int x, int y) => new(OutputKind.RightStick, x, y);
}

public static class BindingText
{
    public static string Describe(Binding? binding)
    {
        if (binding == null)
            return "Unbound";
        if (binding.Kind == BindingKind.None)
            return binding.HasCustomSteps() ? "Custom press and release" : "Unbound";
        var action = DescribeKind(binding);
        if (binding.Tap == null)
            return binding.HasCustomSteps() ? action + " · Custom press and release" : action;
        var tap = binding.Tap.Kind == BindingKind.None ? "Nothing" : DescribeKind(binding.Tap);
        return $"Tap {tap} · Hold {action}";
    }

    private static string DescribeKind(Binding binding) => binding.Kind switch
    {
        BindingKind.Key => binding.Keys.Count == 0 ? "Key" : string.Join(" + ", binding.Keys.Select(key => key.Name)),
        BindingKind.Mouse => binding.Mouse switch
        {
            MouseButton.Left => "Left click",
            MouseButton.Right => "Right click",
            MouseButton.Middle => "Middle click",
            MouseButton.X1 => "Mouse back",
            MouseButton.X2 => "Mouse forward",
            _ => "Mouse",
        },
        BindingKind.Scroll => DescribeScroll(binding),
        BindingKind.LayoutHold => string.IsNullOrEmpty(binding.LayoutId) ? "Hold layout" : $"Hold {binding.LayoutId}",
        BindingKind.LayoutToggle => string.IsNullOrEmpty(binding.LayoutId) ? "Toggle layout" : $"Toggle {binding.LayoutId}",
        BindingKind.ToggleKeyboard => "On-screen keyboard",
        BindingKind.UseBase => "Base binding",
        BindingKind.Pad => string.IsNullOrEmpty(binding.Action) ? ControlNames.PadLabel(binding.PadButton) : binding.Action,
        BindingKind.Profile => "Switch profile",
        BindingKind.MouseMove => "Mouse move",
        BindingKind.StickNudge => binding.Action.Length == 0 ? "Analog" : binding.Action,
        BindingKind.Pause or BindingKind.Resume => "Toggle Win2DS5",
        BindingKind.System => binding.Action.Length == 0 ? "Windows" : binding.Action,
        _ => "Unbound",
    };

    private static string DescribeScroll(Binding binding)
    {
        if (binding.ScrollY > 0) return "Scroll up";
        if (binding.ScrollY < 0) return "Scroll down";
        if (binding.ScrollX > 0) return "Scroll right";
        if (binding.ScrollX < 0) return "Scroll left";
        return "Scroll";
    }
}
