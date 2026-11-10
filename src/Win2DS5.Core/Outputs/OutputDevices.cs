namespace Win2DS5.Core;

public enum OutputDeviceKind
{
    Mouse,
    Keyboard,
    DualShock4,
    Xbox,
}

public sealed class OutputDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public OutputDeviceKind Kind { get; set; }
    public string Name { get; set; } = "";
    public int Slot { get; set; }
}

public static class OutputCatalog
{
    public const int PadLimit = 8;

    public static int Limit(OutputDeviceKind kind) => kind switch
    {
        OutputDeviceKind.Mouse => 1,
        OutputDeviceKind.Keyboard => 1,
        OutputDeviceKind.DualShock4 => 8,
        OutputDeviceKind.Xbox => 4,
        _ => 0,
    };

    public static bool IsPad(OutputDeviceKind kind) =>
        kind is OutputDeviceKind.DualShock4 or OutputDeviceKind.Xbox;

    public static int Count(IReadOnlyList<OutputDevice> outputs, OutputDeviceKind kind) =>
        outputs.Count(output => output.Kind == kind);

    public static int PadCount(IReadOnlyList<OutputDevice> outputs) =>
        outputs.Count(output => IsPad(output.Kind));

    public static bool CanAdd(IReadOnlyList<OutputDevice> outputs, OutputDeviceKind kind)
    {
        if (Count(outputs, kind) >= Limit(kind))
            return false;
        return !IsPad(kind) || PadCount(outputs) < PadLimit;
    }

    public static bool TryAdd(List<OutputDevice> outputs, OutputDeviceKind kind)
    {
        if (!CanAdd(outputs, kind))
            return false;
        int slot = kind is OutputDeviceKind.DualShock4 or OutputDeviceKind.Xbox
            ? NextSlot(outputs, kind)
            : 0;
        outputs.Add(new OutputDevice
        {
            Kind = kind,
            Slot = slot,
            Name = Display(kind, slot),
        });
        return true;
    }

    public static string Display(OutputDeviceKind kind, int slot) => kind switch
    {
        OutputDeviceKind.Mouse => "Mouse",
        OutputDeviceKind.Keyboard => "Keyboard",
        OutputDeviceKind.DualShock4 => $"DS4 {slot}",
        OutputDeviceKind.Xbox => $"XBOX {slot}",
        _ => "Output",
    };

    public static string KindLabel(OutputDeviceKind kind) => kind switch
    {
        OutputDeviceKind.Mouse => "Mouse",
        OutputDeviceKind.Keyboard => "Keyboard",
        OutputDeviceKind.DualShock4 => "DualShock 4",
        OutputDeviceKind.Xbox => "XBOX controller",
        _ => "Output",
    };

    public static OutputDevice? Find(IReadOnlyList<OutputDevice> outputs, string? id) =>
        string.IsNullOrEmpty(id) ? null : outputs.FirstOrDefault(output => output.Id == id);

    public static IReadOnlyList<OutputDevice> PadsInSlotOrder(IReadOnlyList<OutputDevice> outputs) =>
        outputs.Where(output => IsPad(output.Kind))
            .OrderBy(output => output.Kind)
            .ThenBy(output => output.Slot)
            .ThenBy(output => output.Id, StringComparer.Ordinal)
            .ToList();

    public static string? PadTarget(OutputCommand command, IReadOnlyList<OutputDevice> outputs)
    {
        if (command.Kind is not (OutputKind.PadDown or OutputKind.PadUp or OutputKind.LeftStick or OutputKind.RightStick))
            return null;
        if (string.IsNullOrEmpty(command.OutputId))
            return null;
        var output = Find(outputs, command.OutputId);
        return output != null && IsPad(output.Kind) ? output.Id : null;
    }

    public static void Remember(Binding? binding, IReadOnlyList<OutputDevice> outputs)
    {
        if (binding == null)
            return;
        var output = Find(outputs, binding.OutputId);
        if (output != null)
        {
            binding.OutputKnown = true;
            binding.OutputKind = output.Kind;
            binding.OutputSlot = output.Slot;
        }

        Remember(binding.Tap, outputs);
    }

    public static void Remember(StickSettings? stick, IReadOnlyList<OutputDevice> outputs)
    {
        if (stick == null)
            return;
        var output = Find(outputs, stick.OutputId);
        if (output == null)
            return;
        stick.OutputKnown = true;
        stick.OutputKind = output.Kind;
        stick.OutputSlot = output.Slot;
    }

    public static string Infer(Binding? binding, IReadOnlyList<OutputDevice> outputs) =>
        !string.IsNullOrEmpty(binding?.OutputId) && Find(outputs, binding.OutputId) != null
            ? binding.OutputId
            : "";

    private static int NextSlot(IReadOnlyList<OutputDevice> outputs, OutputDeviceKind kind)
    {
        var used = outputs.Where(output => output.Kind == kind).Select(output => output.Slot).ToHashSet();
        for (int slot = 1; slot <= Limit(kind); slot++)
        {
            if (!used.Contains(slot))
                return slot;
        }

        return used.Count + 1;
    }
}

public sealed class InputLayout
{
    public string DeviceKey { get; set; } = "";
    public List<Layer> Layers { get; set; } = [];
    public StickSettings LeftStick { get; set; } = new();
    public StickSettings RightStick { get; set; } = new();
    public TouchpadSettings Touchpad { get; set; } = new();
    public GyroSettings Gyro { get; set; } = new();

    public Layer? GetLayer(string id) => Layers.FirstOrDefault(layer => layer.Id == id);

    public Layer BaseLayer => GetLayer("base") ?? Layers[0];

    public static InputLayout FromProfile(Profile profile, string deviceKey)
    {
        var seed = new InputLayout
        {
            Layers = profile.Layers,
            LeftStick = profile.LeftStick,
            RightStick = profile.RightStick,
            Touchpad = profile.Touchpad,
            Gyro = profile.Gyro,
        };
        var copy = System.Text.Json.JsonSerializer.Deserialize<InputLayout>(
            System.Text.Json.JsonSerializer.Serialize(seed, ProfileStore.Options),
            ProfileStore.Options) ?? new InputLayout();
        copy.DeviceKey = deviceKey;
        copy.Layers ??= [];
        copy.LeftStick ??= new StickSettings();
        copy.RightStick ??= new StickSettings();
        copy.Touchpad ??= new TouchpadSettings();
        copy.Touchpad.Gestures ??= new Dictionary<string, Binding>();
        copy.Gyro ??= new GyroSettings();
        if (copy.Layers.Count == 0)
            copy.Layers.Add(new Layer { Id = "base", Name = "Base" });
        return copy;
    }
}
