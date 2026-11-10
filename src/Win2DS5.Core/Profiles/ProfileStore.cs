using System.Text.Json;
using System.Text.Json.Serialization;

namespace Win2DS5.Core;

public static class ProfileStore
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Win2DS5",
        "profiles.json");

    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ProfileDocument LoadOrCreate(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
        {
            var created = CreateDefault();
            Save(path, created);
            return created;
        }

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<ProfileDocument>(json, Options) ?? CreateDefault();
            Repair(document);
            return document;
        }
        catch (JsonException)
        {
            var fallback = CreateDefault();
            Save(path + ".broken", new ProfileDocument());
            return fallback;
        }
    }

    public static void Save(string path, ProfileDocument document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options));
    }

    public static ProfileDocument CreateDefault()
    {
        var document = ProfilePresets.Shipping();
        EnsureKind(document, OutputDeviceKind.Mouse);
        EnsureKind(document, OutputDeviceKind.Keyboard);
        EnsureKind(document, OutputDeviceKind.DualShock4);
        EnsureKind(document, OutputDeviceKind.Xbox);
        Repair(document);
        return document;
    }

    /// <summary>
    /// Replaces saved profiles and layers with the shipping set. Outputs and appearance settings stay.
    /// </summary>
    public static void InstallShippingDefaults(string? path = null)
    {
        path ??= DefaultPath;
        ProfileDocument? previous = null;
        if (File.Exists(path))
        {
            try
            {
                previous = JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(path), Options);
            }
            catch (JsonException)
            {
                previous = null;
            }
        }

        var document = ProfilePresets.Shipping();
        if (previous != null)
        {
            document.Outputs = previous.Outputs ?? [];
            document.Settings = previous.Settings ?? new AppSettings();
        }

        EnsureKind(document, OutputDeviceKind.Mouse);
        EnsureKind(document, OutputDeviceKind.Keyboard);
        EnsureKind(document, OutputDeviceKind.DualShock4);
        EnsureKind(document, OutputDeviceKind.Xbox);
        Repair(document);
        Save(path, document);
    }

    private static void EnsureKind(ProfileDocument document, OutputDeviceKind kind)
    {
        document.Outputs ??= [];
        if (document.Outputs.Any(output => output.Kind == kind))
            return;
        OutputCatalog.TryAdd(document.Outputs, kind);
    }

    public static void Repair(ProfileDocument document)
    {
        document.Profiles ??= [];
        document.Outputs ??= [];
        document.LayerLibrary ??= [];
        document.Controllers ??= [];
        document.Settings ??= new AppSettings();
        if (document.Profiles.Count == 0)
            document.Profiles.Add(ProfilePresets.MouseHand());

        foreach (var profile in document.Profiles)
        {
            profile.Inputs ??= [];
            profile.Layers ??= [];
            profile.ProcessNames ??= [];
            profile.ToggleChord ??= [nameof(ControlId.Mute), nameof(ControlId.Create)];
            profile.LeftStick ??= new StickSettings();
            profile.RightStick ??= new StickSettings();
            profile.Touchpad ??= new TouchpadSettings();
            profile.Touchpad.Gestures ??= new Dictionary<string, Binding>();
            profile.Gyro ??= new GyroSettings();
            if (profile.Layers.Count == 0)
                profile.Layers.Add(new Layer { Id = "base", Name = "Base" });
            foreach (var layer in profile.Layers)
            {
                layer.Bindings ??= new Dictionary<string, Binding>();
                foreach (var binding in layer.Bindings.Values)
                    PreserveCustomDelay(binding);
            }

            foreach (var binding in profile.Touchpad.Gestures.Values)
                PreserveCustomDelay(binding);
            foreach (var layout in profile.Inputs)
            {
                layout.Layers ??= [];
                layout.LeftStick ??= new StickSettings();
                layout.RightStick ??= new StickSettings();
                layout.Touchpad ??= new TouchpadSettings();
                layout.Touchpad.Gestures ??= new Dictionary<string, Binding>();
                layout.Gyro ??= new GyroSettings();
                foreach (var layer in layout.Layers)
                {
                    layer.Bindings ??= new Dictionary<string, Binding>();
                    foreach (var binding in layer.Bindings.Values)
                        PreserveCustomDelay(binding);
                }
            }
            if (string.IsNullOrWhiteSpace(profile.Id))
                profile.Id = Guid.NewGuid().ToString("N");
        }

        if (document.Profiles.All(profile => profile.Id != document.ActiveId))
            document.ActiveId = document.Profiles[0].Id;
        document.Controllers.RemoveAll(item => document.Profiles.All(profile => profile.Id != item.ProfileId));
        if (document.Settings.LongPressMs is < 100 or > 1000)
            document.Settings.LongPressMs = 350;
        ShareLayers(document);
        foreach (var profile in document.Profiles)
        {
            if (profile.Layers.Count == 0)
                continue;
            if (string.IsNullOrEmpty(profile.BaseLayerId) || profile.GetLayer(profile.BaseLayerId) == null)
                profile.BaseLayerId = (profile.GetLayer("base") ?? profile.Layers[0]).Id;
            NameShippingLayer(profile.BaseLayer);
            foreach (var layer in profile.Layers)
                NameShippingLayer(layer);
        }

        foreach (var layer in document.LayerLibrary)
            NameShippingLayer(layer);
        EnsureOutputs(document);
    }

    private static void NameShippingLayer(Layer layer)
    {
        string? name = layer.Id switch
        {
            "base-ds4" => "DS4",
            "base-xbox" => "XBOX",
            "base-touch" => "Touchpad",
            "base-sticks" => "Sticks",
            "shift-touch" => "Touchpad shift",
            "precision-touch" => "Touchpad precision",
            "shift-sticks" => "Sticks shift",
            "precision-sticks" => "Sticks precision",
            _ => null,
        };
        if (name != null && layer.Name is "Base" or "Shift" or "Precision")
            layer.Name = name;
    }

    private static void ShareLayers(ProfileDocument document)
    {
        var preexisting = document.LayerLibrary.Select(layer => layer.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var profile in document.Profiles)
        {
            for (int i = 0; i < profile.Layers.Count; i++)
            {
                var layer = profile.Layers[i];
                if (string.IsNullOrWhiteSpace(layer.Id))
                    layer.Id = Guid.NewGuid().ToString("N");
                var shared = document.LayerLibrary.FirstOrDefault(item => item.Id == layer.Id);
                if (shared == null)
                {
                    document.LayerLibrary.Add(layer);
                    continue;
                }

                if (ReferenceEquals(shared, layer))
                    continue;
                if (preexisting.Contains(layer.Id))
                {
                    profile.Layers[i] = shared;
                    continue;
                }

                string oldId = layer.Id;
                layer.Id = Guid.NewGuid().ToString("N");
                if (profile.BaseLayerId == oldId)
                    profile.BaseLayerId = layer.Id;
                RewriteLayerId(profile, oldId, layer.Id);
                document.LayerLibrary.Add(layer);
            }
        }
    }

    private static void RewriteLayerId(Profile profile, string oldId, string newId)
    {
        foreach (var layer in profile.Layers)
        {
            foreach (var binding in layer.Bindings.Values)
                Rewrite(binding, oldId, newId);
        }

        if (profile.Touchpad?.Gestures != null)
        {
            foreach (var binding in profile.Touchpad.Gestures.Values)
                Rewrite(binding, oldId, newId);
        }
    }

    private static void Rewrite(Binding? binding, string oldId, string newId)
    {
        if (binding == null)
            return;
        if (binding.LayerId == oldId)
            binding.LayerId = newId;
        Rewrite(binding.Tap, oldId, newId);
    }

    private static void EnsureOutputs(ProfileDocument document)
    {
        bool seeded = document.Outputs.Count == 0;
        if (seeded)
        {
            OutputCatalog.TryAdd(document.Outputs, OutputDeviceKind.Mouse);
            OutputCatalog.TryAdd(document.Outputs, OutputDeviceKind.Keyboard);
            if (document.Profiles.Any(UsesPad))
                OutputCatalog.TryAdd(document.Outputs, OutputDeviceKind.Xbox);
        }

        var mouse = document.Outputs.FirstOrDefault(output => output.Kind == OutputDeviceKind.Mouse)?.Id ?? "";
        var keyboard = document.Outputs.FirstOrDefault(output => output.Kind == OutputDeviceKind.Keyboard)?.Id ?? "";
        var pad = seeded
            ? document.Outputs.FirstOrDefault(output => output.Kind is OutputDeviceKind.Xbox or OutputDeviceKind.DualShock4)?.Id ?? ""
            : "";
        foreach (var output in document.Outputs)
            output.Name = OutputCatalog.Display(output.Kind, output.Slot);
        foreach (var profile in document.Profiles)
        {
            StampProfile(profile.Layers, profile.Touchpad, profile.LeftStick, profile.RightStick, mouse, keyboard, pad, document.Outputs);
            foreach (var layout in profile.Inputs)
                StampProfile(layout.Layers, layout.Touchpad, layout.LeftStick, layout.RightStick, mouse, keyboard, pad, document.Outputs);
        }
    }

    private static void StampProfile(List<Layer> layers, TouchpadSettings touch, StickSettings left, StickSettings right, string mouse, string keyboard, string pad, List<OutputDevice> outputs)
    {
        foreach (var layer in layers)
        {
            foreach (var binding in layer.Bindings.Values)
                Stamp(binding, mouse, keyboard, pad, outputs);
        }

        foreach (var binding in touch.Gestures.Values)
            Stamp(binding, mouse, keyboard, pad, outputs);
        if (string.IsNullOrEmpty(left.OutputId))
            left.OutputId = AimedOutput(left.OutputKnown, left.OutputKind, left.OutputSlot, left.Mode == StickMode.Gamepad ? pad : mouse, outputs);
        if (string.IsNullOrEmpty(right.OutputId))
            right.OutputId = AimedOutput(right.OutputKnown, right.OutputKind, right.OutputSlot, right.Mode == StickMode.Gamepad ? pad : mouse, outputs);
        OutputCatalog.Remember(left, outputs);
        OutputCatalog.Remember(right, outputs);
    }

    private static void Stamp(Binding? binding, string mouse, string keyboard, string pad, List<OutputDevice> outputs)
    {
        if (binding == null)
            return;
        if (string.IsNullOrEmpty(binding.OutputId))
        {
            string fallback = binding.Kind switch
            {
                BindingKind.Key or BindingKind.ToggleKeyboard => keyboard,
                BindingKind.Mouse or BindingKind.Scroll => mouse,
                BindingKind.Pad => pad,
                _ => "",
            };
            binding.OutputId = AimedOutput(binding.OutputKnown, binding.OutputKind, binding.OutputSlot, fallback, outputs);
        }

        Stamp(binding.Tap, mouse, keyboard, pad, outputs);
        OutputCatalog.Remember(binding, outputs);
    }

    private static string AimedOutput(bool known, OutputDeviceKind kind, int slot, string fallback, List<OutputDevice> outputs)
    {
        if (!known)
            return fallback;
        var exact = outputs.FirstOrDefault(output => output.Kind == kind && output.Slot == slot);
        if (exact != null)
            return exact.Id;
        return outputs.FirstOrDefault(output => output.Kind == kind)?.Id ?? "";
    }

    private static bool UsesPad(Profile profile)
    {
        if (profile.LeftStick.Mode == StickMode.Gamepad || profile.RightStick.Mode == StickMode.Gamepad)
            return true;
        return profile.Layers.SelectMany(layer => layer.Bindings.Values).Any(binding => binding.Kind == BindingKind.Pad)
            || profile.Touchpad.Gestures.Values.Any(binding => binding.Kind == BindingKind.Pad);
    }

    private static void PreserveCustomDelay(Binding? binding)
    {
        if (binding == null)
            return;
        binding.PressSteps ??= [];
        binding.ReleaseSteps ??= [];
        if (binding.Tap == null || binding.OverrideDelay)
            return;
        if (binding.TapMs is > 0 and not 350)
            binding.OverrideDelay = true;
    }
}
