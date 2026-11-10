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
    /// Replaces saved profiles and layouts with the shipping set. Outputs and appearance settings stay.
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
        document.LayoutLibrary ??= [];
        document.Controllers ??= [];
        document.Settings ??= new AppSettings();
        if (document.Profiles.Count == 0)
            document.Profiles.Add(ProfilePresets.MouseHand());

        foreach (var profile in document.Profiles)
        {
            profile.Inputs ??= [];
            profile.Layouts ??= [];
            profile.ProcessNames ??= [];
            profile.ToggleChord ??= [nameof(ControlId.Mute), nameof(ControlId.Create)];
            profile.LeftStick ??= new StickSettings();
            profile.RightStick ??= new StickSettings();
            profile.Touchpad ??= new TouchpadSettings();
            profile.Touchpad.Gestures ??= new Dictionary<string, Binding>();
            profile.Gyro ??= new GyroSettings();
            if (profile.Layouts.Count == 0)
                profile.Layouts.Add(new Layout { Id = "base", Name = "Base" });
            foreach (var layout in profile.Layouts)
            {
                layout.Bindings ??= new Dictionary<string, Binding>();
                foreach (var binding in layout.Bindings.Values)
                    PreserveCustomDelay(binding);
            }

            foreach (var binding in profile.Touchpad.Gestures.Values)
                PreserveCustomDelay(binding);
            foreach (var layout in profile.Inputs)
            {
                layout.Layouts ??= [];
                layout.LeftStick ??= new StickSettings();
                layout.RightStick ??= new StickSettings();
                layout.Touchpad ??= new TouchpadSettings();
                layout.Touchpad.Gestures ??= new Dictionary<string, Binding>();
                layout.Gyro ??= new GyroSettings();
                foreach (var saved in layout.Layouts)
                {
                    saved.Bindings ??= new Dictionary<string, Binding>();
                    foreach (var binding in saved.Bindings.Values)
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
        ShareLayouts(document);
        foreach (var profile in document.Profiles)
        {
            if (profile.Layouts.Count == 0)
                continue;
            if (string.IsNullOrEmpty(profile.BaseLayoutId) || profile.GetLayout(profile.BaseLayoutId) == null)
                profile.BaseLayoutId = (profile.GetLayout("base") ?? profile.Layouts[0]).Id;
            NameShippingLayout(profile.BaseLayout);
            foreach (var layout in profile.Layouts)
                NameShippingLayout(layout);
        }

        foreach (var layout in document.LayoutLibrary)
            NameShippingLayout(layout);
        EnsureOutputs(document);
    }

    private static void NameShippingLayout(Layout layout)
    {
        string? name = layout.Id switch
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
        if (name != null && layout.Name is "Base" or "Shift" or "Precision")
            layout.Name = name;
    }

    private static void ShareLayouts(ProfileDocument document)
    {
        var preexisting = document.LayoutLibrary.Select(layout => layout.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var profile in document.Profiles)
        {
            for (int i = 0; i < profile.Layouts.Count; i++)
            {
                var layout = profile.Layouts[i];
                if (string.IsNullOrWhiteSpace(layout.Id))
                    layout.Id = Guid.NewGuid().ToString("N");
                var shared = document.LayoutLibrary.FirstOrDefault(item => item.Id == layout.Id);
                if (shared == null)
                {
                    document.LayoutLibrary.Add(layout);
                    continue;
                }

                if (ReferenceEquals(shared, layout))
                    continue;
                if (preexisting.Contains(layout.Id))
                {
                    profile.Layouts[i] = shared;
                    continue;
                }

                string oldId = layout.Id;
                layout.Id = Guid.NewGuid().ToString("N");
                if (profile.BaseLayoutId == oldId)
                    profile.BaseLayoutId = layout.Id;
                RewriteLayoutId(profile, oldId, layout.Id);
                document.LayoutLibrary.Add(layout);
            }
        }
    }

    private static void RewriteLayoutId(Profile profile, string oldId, string newId)
    {
        foreach (var layout in profile.Layouts)
        {
            foreach (var binding in layout.Bindings.Values)
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
        if (binding.LayoutId == oldId)
            binding.LayoutId = newId;
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
            StampProfile(profile.Layouts, profile.Touchpad, profile.LeftStick, profile.RightStick, mouse, keyboard, pad, document.Outputs);
            foreach (var layout in profile.Inputs)
                StampProfile(layout.Layouts, layout.Touchpad, layout.LeftStick, layout.RightStick, mouse, keyboard, pad, document.Outputs);
        }
    }

    private static void StampProfile(List<Layout> layouts, TouchpadSettings touch, StickSettings left, StickSettings right, string mouse, string keyboard, string pad, List<OutputDevice> outputs)
    {
        foreach (var layout in layouts)
        {
            foreach (var binding in layout.Bindings.Values)
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
        return profile.Layouts.SelectMany(layout => layout.Bindings.Values).Any(binding => binding.Kind == BindingKind.Pad)
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
