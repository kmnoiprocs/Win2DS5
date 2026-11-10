namespace Win2DS5.Core;

public enum StickMode
{
    Off,
    Mouse,
    Scroll,
    Gamepad,
}

public enum TouchpadMode
{
    Off,
    Trackpad,
    Stick,
}

public enum TouchZone
{
    Center,
    Left,
    Right,
    Top,
}

public enum GestureKind
{
    Tap,
    LongPress,
    Click,
}

public sealed class StickSettings
{
    public StickMode Mode { get; set; } = StickMode.Off;
    public double Deadzone { get; set; } = 0.14;
    public double Sensitivity { get; set; } = 1400;
    public double Curve { get; set; } = 1.35;
    public double HorizontalScale { get; set; } = 1;
    public double VerticalScale { get; set; } = 1;
    public string OutputId { get; set; } = "";
    public bool OutputKnown { get; set; }
    public OutputDeviceKind OutputKind { get; set; }
    public int OutputSlot { get; set; }
}

public sealed class TouchpadSettings
{
    public TouchpadMode Mode { get; set; } = TouchpadMode.Trackpad;
    public double Sensitivity { get; set; } = 1.35;
    public double StickSensitivity { get; set; } = 1100;
    public double StickDeadzone { get; set; } = 0.12;
    public double Curve { get; set; } = 1.25;
    public int LongPressMs { get; set; } = 400;
    public int DragSlop { get; set; } = 36;
    public bool TwoFingerScroll { get; set; } = true;
    public bool EdgeScroll { get; set; } = true;
    public double EdgeFraction { get; set; } = 0.08;
    public double ScrollScale { get; set; } = 1.5;
    public double EdgeScrollScale { get; set; } = 1.15;
    public double TopFraction { get; set; } = 0.22;
    public double LeftFraction { get; set; } = 0.33;
    public double RightFraction { get; set; } = 0.33;
    public Dictionary<string, Binding> Gestures { get; set; } = new();

    public static string GestureKey(TouchZone zone, GestureKind kind) => $"{zone}.{kind}";

    public Binding? GetGesture(TouchZone zone, GestureKind kind) =>
        Gestures.TryGetValue(GestureKey(zone, kind), out var binding) ? binding : null;

    public void SetGesture(TouchZone zone, GestureKind kind, Binding? binding)
    {
        var key = GestureKey(zone, kind);
        if (binding == null || binding.Kind == BindingKind.None)
            Gestures.Remove(key);
        else
            Gestures[key] = binding;
    }
}

public sealed class GyroSettings
{
    public bool Enabled { get; set; }
    public ControlId Activator { get; set; } = ControlId.L3;
    public double Sensitivity { get; set; } = 14;
    public double Deadzone { get; set; } = 1.2;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
}

public sealed class Layer
{
    public string Id { get; set; } = "base";
    public string Name { get; set; } = "Base";
    public string Light { get; set; } = "";
    public bool AffectsBindings { get; set; } = true;
    public double PointerScale { get; set; } = 1;
    public Dictionary<string, Binding> Bindings { get; set; } = new();

    public Binding? Get(ControlId id) =>
        Bindings.TryGetValue(id.ToString(), out var binding) ? binding : null;

    public void Set(ControlId id, Binding? binding)
    {
        var key = id.ToString();
        if (binding == null || (binding.Kind == BindingKind.None && !KeepsNone(binding)))
            Bindings.Remove(key);
        else
            Bindings[key] = binding;
    }

    public Layer Clone()
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<Layer>(
            System.Text.Json.JsonSerializer.Serialize(this, ProfileStore.Options),
            ProfileStore.Options) ?? new Layer();
        copy.Bindings ??= new Dictionary<string, Binding>();
        return copy;
    }

    public void ReplaceBindings(Layer source)
    {
        var copy = source.Clone();
        Bindings.Clear();
        foreach (var pair in copy.Bindings)
            Bindings[pair.Key] = pair.Value;
    }

    private static bool KeepsNone(Binding binding) => binding.HasCustomSteps();
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Desktop";
    public string ShellColor { get; set; } = "";
    public string ShellImage { get; set; } = "";
    public List<string> ProcessNames { get; set; } = [];
    public List<Layer> Layers { get; set; } = [];
    public string BaseLayerId { get; set; } = "";
    public StickSettings LeftStick { get; set; } = new();
    public StickSettings RightStick { get; set; } = new();
    public TouchpadSettings Touchpad { get; set; } = new();
    public GyroSettings Gyro { get; set; } = new();
    public List<string> ToggleChord { get; set; } = [nameof(ControlId.Mute), nameof(ControlId.Create)];
    public List<InputLayout> Inputs { get; set; } = [];

    public InputLayout LayoutFor(string? deviceKey)
    {
        deviceKey ??= "";
        var found = Inputs.FirstOrDefault(layout => layout.DeviceKey == deviceKey);
        if (found != null)
            return found;
        if (deviceKey.Length > 0)
        {
            var pending = Inputs.FirstOrDefault(layout => layout.DeviceKey.Length == 0);
            if (pending != null)
            {
                pending.DeviceKey = deviceKey;
                return pending;
            }
        }

        var created = InputLayout.FromProfile(this, deviceKey);
        Inputs.Add(created);
        return created;
    }

    public Layer? GetLayer(string id) => Layers.FirstOrDefault(layer => layer.Id == id);

    public Layer BaseLayer =>
        (!string.IsNullOrEmpty(BaseLayerId) ? GetLayer(BaseLayerId) : null)
        ?? GetLayer("base")
        ?? Layers[0];
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool DesktopEnabled { get; set; } = true;
    public double KeyboardOpacity { get; set; } = 0.94;
    public double KeyboardLeft { get; set; } = 160;
    public double KeyboardTop { get; set; } = 620;
    public double KeyboardWidth { get; set; } = 980;
    public double KeyboardHeight { get; set; } = 340;
    public bool KeyboardPlaced { get; set; }
    public string Theme { get; set; } = "Dark";
    public string Accent { get; set; } = "#7EB6FF";
    public int LongPressMs { get; set; } = 350;
    public bool CheckForUpdates { get; set; } = true;
    public string SelectedInputKey { get; set; } = "";
}

public sealed class ControllerAssignment
{
    public string DeviceKey { get; set; } = "";
    public string ProfileId { get; set; } = "";
}

public sealed class ProfileDocument
{
    public string ActiveId { get; set; } = "";
    public List<Profile> Profiles { get; set; } = [];
    public List<OutputDevice> Outputs { get; set; } = [];
    public List<Layer> LayerLibrary { get; set; } = [];
    public List<ControllerAssignment> Controllers { get; set; } = [];
    public AppSettings Settings { get; set; } = new();

    public Profile Active =>
        Profiles.FirstOrDefault(profile => profile.Id == ActiveId) ?? Profiles[0];

    public Profile ProfileFor(string? deviceKey)
    {
        if (!string.IsNullOrEmpty(deviceKey))
        {
            var assigned = Controllers.FirstOrDefault(item => item.DeviceKey == deviceKey);
            var profile = Profiles.FirstOrDefault(item => item.Id == assigned?.ProfileId);
            if (profile != null)
                return profile;
        }

        return Active;
    }

    public void Assign(string deviceKey, string profileId)
    {
        if (string.IsNullOrEmpty(deviceKey) || Profiles.All(profile => profile.Id != profileId))
            return;
        var existing = Controllers.FirstOrDefault(item => item.DeviceKey == deviceKey);
        if (existing == null)
            Controllers.Add(new ControllerAssignment { DeviceKey = deviceKey, ProfileId = profileId });
        else
            existing.ProfileId = profileId;
    }
}
