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
        var profile = ProfilePresets.MouseHand();
        return new ProfileDocument
        {
            ActiveId = profile.Id,
            Profiles = [profile],
        };
    }

    public static void Repair(ProfileDocument document)
    {
        document.Profiles ??= [];
        document.Settings ??= new AppSettings();
        if (document.Profiles.Count == 0)
            document.Profiles.Add(ProfilePresets.MouseHand());

        foreach (var profile in document.Profiles)
        {
            profile.Layers ??= [];
            profile.ProcessNames ??= [];
            profile.ToggleChord ??= [nameof(ControlId.Mute), nameof(ControlId.Create)];
            profile.LeftStick ??= new StickSettings();
            profile.RightStick ??= new StickSettings();
            profile.Touchpad ??= new TouchpadSettings();
            profile.Touchpad.Gestures ??= new Dictionary<string, Binding>();
            profile.Gyro ??= new GyroSettings();
            if (profile.GetLayer("base") == null)
                profile.Layers.Insert(0, new Layer { Id = "base", Name = "Base" });
            foreach (var layer in profile.Layers)
            {
                layer.Bindings ??= new Dictionary<string, Binding>();
                foreach (var binding in layer.Bindings.Values)
                    PreserveCustomDelay(binding);
            }

            foreach (var binding in profile.Touchpad.Gestures.Values)
                PreserveCustomDelay(binding);
            if (string.IsNullOrWhiteSpace(profile.Id))
                profile.Id = Guid.NewGuid().ToString("N");
        }

        if (document.Profiles.All(profile => profile.Id != document.ActiveId))
            document.ActiveId = document.Profiles[0].Id;
        if (document.Settings.LongPressMs is < 100 or > 1000)
            document.Settings.LongPressMs = 350;
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
