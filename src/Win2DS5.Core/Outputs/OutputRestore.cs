namespace Win2DS5.Core;

public sealed record MissingOutput(string OldId, OutputDeviceKind Kind, int Slot, string Label);

public static class OutputRestore
{
    public static List<MissingOutput> Find(Profile profile, IReadOnlyList<OutputDevice> outputs)
    {
        var found = new Dictionary<string, MissingOutput>(StringComparer.Ordinal);
        foreach (var layout in profile.Layouts ?? [])
        {
            foreach (var binding in (layout.Bindings ?? []).Values)
                Collect(binding, outputs, found);
        }

        if (profile.Touchpad?.Gestures != null)
        {
            foreach (var binding in profile.Touchpad.Gestures.Values)
                Collect(binding, outputs, found);
        }
        CollectStick(profile.LeftStick, outputs, found);
        CollectStick(profile.RightStick, outputs, found);
        return found.Values.ToList();
    }

    public static List<string> Restore(ProfileDocument document, Profile profile, IReadOnlyList<MissingOutput> missing)
    {
        var failed = new List<string>();
        foreach (var need in missing)
        {
            var match = document.Outputs.FirstOrDefault(output => output.Kind == need.Kind && (need.Slot == 0 || output.Slot == need.Slot));
            if (match == null && need.Kind is OutputDeviceKind.Mouse or OutputDeviceKind.Keyboard)
                match = document.Outputs.FirstOrDefault(output => output.Kind == need.Kind);
            if (match == null)
            {
                if (!OutputCatalog.TryAdd(document.Outputs, need.Kind))
                {
                    failed.Add(need.Label);
                    continue;
                }

                match = document.Outputs[^1];
            }

            Retarget(profile, need.OldId, match);
        }

        return failed;
    }

    private static void Collect(Binding? binding, IReadOnlyList<OutputDevice> outputs, Dictionary<string, MissingOutput> found)
    {
        if (binding == null)
            return;
        Note(binding.OutputId, binding.OutputKnown, binding.OutputKind, binding.OutputSlot, binding.Kind, outputs, found);
        Collect(binding.Tap, outputs, found);
    }

    private static void CollectStick(StickSettings? stick, IReadOnlyList<OutputDevice> outputs, Dictionary<string, MissingOutput> found)
    {
        if (stick == null || stick.Mode == StickMode.Off)
            return;
        var kind = stick.Mode == StickMode.Gamepad ? BindingKind.Pad : stick.Mode == StickMode.Scroll ? BindingKind.Scroll : BindingKind.Mouse;
        Note(stick.OutputId, stick.OutputKnown, stick.OutputKind, stick.OutputSlot, kind, outputs, found);
    }

    private static void Note(string id, bool known, OutputDeviceKind remembered, int slot, BindingKind bindingKind, IReadOnlyList<OutputDevice> outputs, Dictionary<string, MissingOutput> found)
    {
        if (string.IsNullOrEmpty(id) || outputs.Any(output => output.Id == id) || found.ContainsKey(id))
            return;
        OutputDeviceKind kind = known ? remembered : bindingKind switch
        {
            BindingKind.Key or BindingKind.ToggleKeyboard => OutputDeviceKind.Keyboard,
            BindingKind.Pad => OutputDeviceKind.Xbox,
            _ => OutputDeviceKind.Mouse,
        };
        if (!known)
            slot = kind is OutputDeviceKind.Mouse or OutputDeviceKind.Keyboard ? 0 : 1;
        string label = kind is OutputDeviceKind.Mouse or OutputDeviceKind.Keyboard
            ? OutputCatalog.KindLabel(kind)
            : $"{OutputCatalog.KindLabel(kind)} · Slot {slot}";
        found[id] = new MissingOutput(id, kind, slot, label);
    }

    private static void Retarget(Profile profile, string oldId, OutputDevice output)
    {
        foreach (var layout in profile.Layouts ?? [])
        {
            foreach (var binding in (layout.Bindings ?? []).Values)
                Retarget(binding, oldId, output);
        }

        if (profile.Touchpad?.Gestures != null)
        {
            foreach (var binding in profile.Touchpad.Gestures.Values)
                Retarget(binding, oldId, output);
        }
        Retarget(profile.LeftStick, oldId, output);
        Retarget(profile.RightStick, oldId, output);
    }

    private static void Retarget(Binding? binding, string oldId, OutputDevice output)
    {
        if (binding == null)
            return;
        if (binding.OutputId == oldId)
        {
            binding.OutputId = output.Id;
            binding.OutputKnown = true;
            binding.OutputKind = output.Kind;
            binding.OutputSlot = output.Slot;
        }

        Retarget(binding.Tap, oldId, output);
    }

    private static void Retarget(StickSettings? stick, string oldId, OutputDevice output)
    {
        if (stick == null || stick.OutputId != oldId)
            return;
        stick.OutputId = output.Id;
        stick.OutputKnown = true;
        stick.OutputKind = output.Kind;
        stick.OutputSlot = output.Slot;
    }
}
