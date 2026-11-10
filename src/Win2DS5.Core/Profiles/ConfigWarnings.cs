namespace Win2DS5.Core;

public sealed record ConfigWarning(string Message, string? OutputId, string? ProfileId);

public static class ConfigWarnings
{
    public static IReadOnlyList<ConfigWarning> Collect(ProfileDocument document, IEnumerable<string> connectedDeviceKeys)
    {
        var warnings = new List<ConfigWarning>();
        document.Profiles ??= [];
        document.Outputs ??= [];
        foreach (var profile in document.Profiles)
            Inspect(document, profile, warnings);
        SharedPads(document, connectedDeviceKeys, warnings);
        return warnings;
    }

    public static string? ForBinding(ProfileDocument document, Profile profile, Binding? binding)
    {
        if (binding == null)
            return null;
        foreach (var item in SelfAndTap(binding))
        {
            foreach (var note in BindingNotes(document, profile, "button", item, toast: false))
                return Present(note);
        }

        return null;
    }

    public static string? ForStick(ProfileDocument document, StickSettings? stick)
    {
        if (stick == null || stick.Mode != StickMode.Gamepad)
            return null;
        if (string.IsNullOrEmpty(stick.OutputId))
            return "Choose an output.";
        return OutputCatalog.Find(document.Outputs, stick.OutputId) == null
            ? "Points at a removed output."
            : null;
    }

    public static IReadOnlyList<string> ProfileNamesUsing(ProfileDocument document, string outputId)
    {
        var names = new List<string>();
        foreach (var profile in document.Profiles ?? [])
        {
            if (Uses(profile, outputId))
                names.Add(profile.Name);
        }

        return names;
    }

    public static int ControllersUsing(ProfileDocument document, string profileId, IEnumerable<string> connectedDeviceKeys)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in connectedDeviceKeys)
        {
            if (!string.IsNullOrEmpty(key))
                keys.Add(key);
        }

        foreach (var assignment in document.Controllers ?? [])
        {
            if (!string.IsNullOrEmpty(assignment.DeviceKey))
                keys.Add(assignment.DeviceKey);
        }

        if (keys.Count == 0)
            return document.Active.Id == profileId ? 1 : 0;
        return keys.Count(key => document.ProfileFor(key).Id == profileId);
    }

    private static void Inspect(ProfileDocument document, Profile profile, List<ConfigWarning> warnings)
    {
        bool removed = false;
        bool missing = false;
        foreach (var layout in profile.Layouts ?? [])
        {
            foreach (var pair in layout.Bindings ?? [])
            {
                if (!Enum.TryParse<ControlId>(pair.Key, out var control))
                    continue;
                string place = ControlNames.Display(control);
                foreach (var binding in SelfAndTap(pair.Value))
                    CollectBinding(document, profile, place, binding, warnings, ref removed, ref missing);
            }
        }

        foreach (var pair in profile.Touchpad?.Gestures ?? [])
        {
            string place = pair.Key.Replace('.', ' ');
            foreach (var binding in SelfAndTap(pair.Value))
                CollectBinding(document, profile, place, binding, warnings, ref removed, ref missing);
        }

        InspectStick(document, profile.LeftStick, ref removed, ref missing);
        InspectStick(document, profile.RightStick, ref removed, ref missing);
        if (removed)
            warnings.Add(new ConfigWarning($"{profile.Name} points at a removed output.", null, profile.Id));
        if (missing)
            warnings.Add(new ConfigWarning($"{profile.Name} has a button or stick with no output.", null, profile.Id));

        foreach (var name in profile.ToggleChord ?? [])
        {
            if (!Enum.TryParse<ControlId>(name, out var control) || control == ControlId.None)
                continue;
            bool owned = (profile.Layouts ?? []).Any(layout => HasAction(layout.Get(control)));
            if (!owned)
                continue;
            warnings.Add(new ConfigWarning(
                $"{ControlNames.Display(control)} on {profile.Name} is part of the desktop chord and also has its own action.",
                null,
                profile.Id));
        }
    }

    private static void CollectBinding(ProfileDocument document, Profile profile, string place, Binding binding, List<ConfigWarning> warnings, ref bool removed, ref bool missing)
    {
        foreach (var note in BindingNotes(document, profile, place, binding, toast: true))
        {
            if (note == Removed)
                removed = true;
            else if (note == Missing)
                missing = true;
            else
                warnings.Add(new ConfigWarning($"{place} on {profile.Name} {note}", null, profile.Id));
        }
    }

    private const string Removed = "points at a removed output.";
    private const string Missing = "has no output.";

    private static IEnumerable<string> BindingNotes(ProfileDocument document, Profile profile, string place, Binding binding, bool toast)
    {
        _ = place;
        if (binding.Advanced)
        {
            foreach (var item in Balance(binding))
            {
                yield return item.Delta > 0
                    ? $"presses {item.Name} and never releases it."
                    : $"releases {item.Name} without pressing it.";
            }
        }

        if (binding.Kind == BindingKind.Key && !HasKey(binding))
            yield return "has a key action with no key.";
        if (binding.Kind == BindingKind.Profile && (document.Profiles ?? []).All(item => item.Id != binding.ProfileId))
            yield return "switches to a profile that was deleted.";
        if (binding.Kind is BindingKind.LayoutHold or BindingKind.LayoutToggle &&
            (profile.Layouts ?? []).All(layout => layout.Id != binding.LayoutId))
            yield return "uses a layout that does not exist.";
        if (!SendsToOutput(binding))
            yield break;
        if (string.IsNullOrEmpty(binding.OutputId))
            yield return toast ? Missing : "Choose an output.";
        else if (OutputCatalog.Find(document.Outputs, binding.OutputId) == null)
            yield return toast ? Removed : "Points at a removed output.";
    }

    private static void InspectStick(ProfileDocument document, StickSettings? stick, ref bool removed, ref bool missing)
    {
        if (stick == null || stick.Mode != StickMode.Gamepad)
            return;
        if (string.IsNullOrEmpty(stick.OutputId))
            missing = true;
        else if (OutputCatalog.Find(document.Outputs, stick.OutputId) == null)
            removed = true;
    }

    private static string Present(string note) =>
        note.Length == 0 || char.IsUpper(note[0]) ? note : char.ToUpperInvariant(note[0]) + note[1..];

    private static void SharedPads(ProfileDocument document, IEnumerable<string> connectedDeviceKeys, List<ConfigWarning> warnings)
    {
        var keys = connectedDeviceKeys.Where(key => !string.IsNullOrEmpty(key)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count < 2)
            return;
        var uses = new Dictionary<(string OutputId, string What), List<string>>();
        foreach (var key in keys)
        {
            var profile = document.ProfileFor(key);
            foreach (var use in PadUses(document, profile))
            {
                if (!uses.TryGetValue(use, out var names))
                    uses[use] = names = [];
                names.Add(profile.Name);
            }
        }

        foreach (var outputGroup in uses.Where(pair => pair.Value.Count >= 2).GroupBy(pair => pair.Key.OutputId))
        {
            var output = OutputCatalog.Find(document.Outputs, outputGroup.Key);
            if (output == null)
                continue;
            var whats = outputGroup.Select(pair => pair.Key.What).Distinct().ToList();
            var names = outputGroup.SelectMany(pair => pair.Value).Distinct().ToList();
            int controllers = outputGroup.Max(pair => pair.Value.Count);
            string who = names.Count == 1
                ? $"{controllers} controllers assigned to {names[0]}"
                : string.Join(" and ", names);
            string what = whats.Count <= 3
                ? string.Join(", ", whats)
                : $"{whats[0]}, {whats[1]}, and {whats.Count - 2} more";
            warnings.Add(new ConfigWarning($"{who} press {what} on {output.Name}.", output.Id, null));
        }
    }

    private static IEnumerable<(string OutputId, string What)> PadUses(ProfileDocument document, Profile profile)
    {
        foreach (var layout in profile.Layouts ?? [])
        {
            foreach (var binding in (layout.Bindings ?? []).Values.SelectMany(SelfAndTap))
            {
                if (binding.Kind != BindingKind.Pad || binding.PadButton == ControlId.None)
                    continue;
                var output = OutputCatalog.Find(document.Outputs, binding.OutputId);
                if (output != null && OutputCatalog.IsPad(output.Kind))
                    yield return (output.Id, ControlNames.PadLabel(binding.PadButton));
            }
        }

        foreach (var (stick, name) in new[] { (profile.LeftStick, "the left stick"), (profile.RightStick, "the right stick") })
        {
            if (stick?.Mode != StickMode.Gamepad)
                continue;
            var output = OutputCatalog.Find(document.Outputs, stick.OutputId);
            if (output != null && OutputCatalog.IsPad(output.Kind))
                yield return (output.Id, name);
        }
    }

    private static bool Uses(Profile profile, string outputId)
    {
        if (string.IsNullOrEmpty(outputId))
            return false;
        if (profile.LeftStick?.OutputId == outputId || profile.RightStick?.OutputId == outputId)
            return true;
        foreach (var layout in profile.Layouts ?? [])
        {
            foreach (var binding in (layout.Bindings ?? []).Values.SelectMany(SelfAndTap))
            {
                if (binding.OutputId == outputId)
                    return true;
            }
        }

        foreach (var binding in (profile.Touchpad?.Gestures ?? []).Values.SelectMany(SelfAndTap))
        {
            if (binding.OutputId == outputId)
                return true;
        }

        return false;
    }

    private static IEnumerable<Binding> SelfAndTap(Binding binding)
    {
        yield return binding;
        if (binding.Tap != null)
            yield return binding.Tap;
    }

    private static bool HasAction(Binding? binding)
    {
        if (binding == null)
            return false;
        if (binding.Kind != BindingKind.None)
            return true;
        return binding.HasCustomSteps();
    }

    private static bool SendsToOutput(Binding binding) =>
        binding.Kind is BindingKind.Key or BindingKind.Mouse or BindingKind.Scroll or BindingKind.MouseMove or BindingKind.Pad or BindingKind.StickNudge;

    private static bool HasKey(Binding binding) =>
        binding.Keys != null && binding.Keys.Any(key => key.ScanCode != 0);

    private static IEnumerable<(string Name, int Delta)> Balance(Binding binding)
    {
        var counts = new Dictionary<string, (string Name, int Delta)>();
        void Add(EdgeStep step)
        {
            string key;
            string name;
            int delta;
            switch (step.Kind)
            {
                case EdgeKind.KeyDown:
                case EdgeKind.KeyUp:
                    name = string.IsNullOrWhiteSpace(step.Key?.Name) ? "a key" : step.Key.Name;
                    key = "k:" + (step.Key?.ScanCode ?? 0) + ":" + (step.Key?.Extended == true);
                    delta = step.Kind == EdgeKind.KeyDown ? 1 : -1;
                    break;
                case EdgeKind.MouseDown:
                case EdgeKind.MouseUp:
                    name = step.Mouse switch
                    {
                        MouseButton.Left => "left click",
                        MouseButton.Right => "right click",
                        MouseButton.Middle => "middle click",
                        MouseButton.X1 => "mouse back",
                        MouseButton.X2 => "mouse forward",
                        _ => "a mouse button",
                    };
                    key = "m:" + step.Mouse;
                    delta = step.Kind == EdgeKind.MouseDown ? 1 : -1;
                    break;
                default:
                    return;
            }

            counts.TryGetValue(key, out var current);
            counts[key] = (name, current.Delta + delta);
        }

        foreach (var step in binding.PressSteps ?? [])
            Add(step);
        foreach (var step in binding.ReleaseSteps ?? [])
            Add(step);
        foreach (var item in counts.Values)
        {
            if (item.Delta != 0)
                yield return item;
        }
    }
}
