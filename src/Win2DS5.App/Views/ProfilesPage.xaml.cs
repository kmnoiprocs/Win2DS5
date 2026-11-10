using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Win2DS5.App.Controls;
using Win2DS5.Core;

namespace Win2DS5.App.Views;

public sealed partial class ProfilesPage : UserControl
{
    private bool _loading = true;

    public ProfilesPage()
    {
        InitializeComponent();
        PresetBox.Items.Add("DS4");
        PresetBox.Items.Add("XBOX");
        PresetBox.Items.Add("Touchpad");
        PresetBox.Items.Add("Sticks");
        PresetBox.SelectedIndex = 0;
        Loaded += (_, _) => Reload();
    }

    public event Action? ProfileChanged;

    private void Reload()
    {
        _loading = true;
        int keep = ProfileList.SelectedIndex;
        ProfileList.Items.Clear();
        var profiles = App.Runtime.Document.Profiles;
        string current = App.Runtime.Editing.Id;
        bool named = !string.IsNullOrEmpty(App.Runtime.ActiveInputKey);
        foreach (var profile in profiles)
        {
            string label = profile.Name;
            if (named && profile.Id == current)
                label += "  ·  this controller";
            ProfileList.Items.Add(label);
        }

        ProfileList.SelectedIndex = keep >= 0 && keep < profiles.Count
            ? keep
            : profiles.FindIndex(profile => profile.Id == App.Runtime.Document.ActiveId);
        ShowSelected();
        ShowLayers();
        _loading = false;
    }

    private void ShowLayers()
    {
        LayerList.Items.Clear();
        var document = App.Runtime.Document;
        var bases = new List<Layer>();
        foreach (var profile in document.Profiles)
        {
            if (profile.Layers.Count == 0)
                continue;
            var layer = document.LayerLibrary.FirstOrDefault(item => item.Id == profile.BaseLayer.Id) ?? profile.BaseLayer;
            if (bases.All(item => item.Id != layer.Id))
                bases.Add(layer);
        }

        var ordered = bases.Concat(document.LayerLibrary.Where(layer => bases.All(item => item.Id != layer.Id)));
        foreach (var layer in ordered)
        {
            var users = document.Profiles.Where(profile => profile.Layers.Any(item => item.Id == layer.Id)).Select(profile => profile.Name).ToList();
            var homes = document.Profiles.Where(profile => profile.Layers.Count > 0 && profile.BaseLayer.Id == layer.Id).Select(profile => profile.Name).ToList();
            string text = layer.Name;
            if (homes.Count > 0)
                text += "  ·  base of " + string.Join(", ", homes);
            else if (users.Count > 0)
                text += "  ·  " + string.Join(", ", users);
            else
                text += "  ·  not used";
            LayerList.Items.Add(text);
        }

        if (LayerList.Items.Count == 0)
            LayerList.Items.Add("No saved layers yet");
    }

    private Profile? Selected()
    {
        int index = ProfileList.SelectedIndex;
        var profiles = App.Runtime.Document.Profiles;
        return index >= 0 && index < profiles.Count ? profiles[index] : null;
    }

    private void ShowSelected()
    {
        var profile = Selected();
        if (profile == null)
            return;
        NameBox.Text = profile.Name;
        ProcessBox.Text = string.Join(", ", profile.ProcessNames);
        BaseLayerBox.Items.Clear();
        foreach (var layer in profile.Layers)
            BaseLayerBox.Items.Add(layer.Name);
        int baseIndex = profile.Layers.FindIndex(layer => layer.Id == profile.BaseLayer.Id);
        BaseLayerBox.SelectedIndex = baseIndex >= 0 ? baseIndex : 0;
        bool onController = !string.IsNullOrEmpty(App.Runtime.ActiveInputKey) && App.Runtime.Editing.Id == profile.Id;
        UsingLabel.Text = onController
            ? $"Assigned to {App.Runtime.InputLabel}."
            : string.IsNullOrEmpty(App.Runtime.ActiveInputKey)
                ? "No controller is selected. This profile is the one opened on Controls."
                : $"Not assigned to {App.Runtime.InputLabel}. Use it on this controller to switch.";
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        _loading = true;
        ShowSelected();
        _loading = false;
        var profile = Selected();
        if (profile == null || !string.IsNullOrEmpty(App.Runtime.ActiveInputKey))
            return;
        App.Runtime.Update(() => App.Runtime.Document.ActiveId = profile.Id);
        App.Runtime.Save();
        ProfileChanged?.Invoke();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var profile = Selected();
        if (profile == null)
            return;
        App.Runtime.Update(() =>
        {
            profile.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? profile.Name : NameBox.Text.Trim();
            profile.ProcessNames = ProcessBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (BaseLayerBox.SelectedIndex >= 0 && BaseLayerBox.SelectedIndex < profile.Layers.Count)
                ChooseBase(profile, profile.Layers[BaseLayerBox.SelectedIndex]);
        });
        App.Runtime.Save();
        Reload();
    }

    private void OnUse(object sender, RoutedEventArgs e)
    {
        var profile = Selected();
        if (profile == null)
            return;
        App.Runtime.AssignToActive(profile.Id);
        Reload();
        ProfileChanged?.Invoke();
    }

    private async void OnNew(object sender, RoutedEventArgs e)
    {
        if (XamlRoot == null)
        {
            Add(BlankProfile(null), "New profile");
            return;
        }

        var library = App.Runtime.Document.LayerLibrary;
        var choices = new QuietComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 280 };
        choices.Items.Add("New blank base layer");
        foreach (var layer in library)
            choices.Items.Add(layer.Name);
        choices.SelectedIndex = 0;
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            Text = "Choose the saved layer this profile starts from.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(choices);
        var dialog = new ContentDialog
        {
            Title = "New profile",
            Content = body,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        Layer? chosen = choices.SelectedIndex > 0 && choices.SelectedIndex - 1 < library.Count
            ? library[choices.SelectedIndex - 1]
            : null;
        var profile = BlankProfile(chosen);
        profile.Name = UniqueName("New profile");
        AddExisting(profile);
    }

    private Profile BlankProfile(Layer? shared)
    {
        var profile = ProfilePresets.Blank();
        profile.Inputs.Clear();
        if (shared != null)
        {
            profile.Layers = [shared];
            profile.BaseLayerId = shared.Id;
            return profile;
        }

        string name = BlankLayerName(UniqueName("New profile"));
        var layer = new Layer { Id = Guid.NewGuid().ToString("N"), Name = name };
        App.Runtime.Document.LayerLibrary.Add(layer);
        profile.Layers = [layer];
        profile.BaseLayerId = layer.Id;
        return profile;
    }

    private static string BlankLayerName(string profileName)
    {
        var names = App.Runtime.Document.LayerLibrary.Select(layer => layer.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("Base"))
            return "Base";
        if (!names.Contains(profileName))
            return profileName;
        for (int i = 2; ; i++)
        {
            string candidate = profileName + " " + i;
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        var profile = Selected();
        if (profile == null)
            return;
        var copy = Clone(profile);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = UniqueName(profile.Name + " copy");
        copy.Inputs.Clear();
        AddExisting(copy);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var profile = Selected();
        if (profile == null || App.Runtime.Document.Profiles.Count < 2)
            return;
        App.Runtime.Update(() =>
        {
            var document = App.Runtime.Document;
            string fallback = document.Profiles.First(item => item.Id != profile.Id).Id;
            document.Profiles.RemoveAll(item => item.Id == profile.Id);
            foreach (var controller in document.Controllers.Where(item => item.ProfileId == profile.Id))
                controller.ProfileId = fallback;
            if (document.ActiveId == profile.Id)
                document.ActiveId = fallback;
        }, true);
        App.Runtime.Save();
        Reload();
        ProfileChanged?.Invoke();
    }

    private void OnCreatePreset(object sender, RoutedEventArgs e)
    {
        var created = PresetBox.SelectedIndex switch
        {
            1 => ProfilePresets.Xbox(),
            2 => ProfilePresets.Touchpad(),
            3 => ProfilePresets.Sticks(),
            _ => ProfilePresets.Ds4(),
        };
        created.Name = UniqueName(created.Name);
        AdoptPresetLayers(created);
        AddExisting(created);
    }

    private void Add(Profile profile, string name)
    {
        profile.Name = UniqueName(name);
        AddExisting(profile);
    }

    private void AddExisting(Profile profile)
    {
        App.Runtime.Update(() => App.Runtime.Document.Profiles.Add(profile));
        App.Runtime.Save();
        Reload();
        ProfileList.SelectedIndex = App.Runtime.Document.Profiles.Count - 1;
    }

    private static void ChooseBase(Profile profile, Layer layer)
    {
        profile.Layers.RemoveAll(item => item.Id == layer.Id);
        profile.Layers.Insert(0, layer);
        profile.BaseLayerId = layer.Id;
    }

    private static void AdoptPresetLayers(Profile profile)
    {
        var library = App.Runtime.Document.LayerLibrary;
        var moved = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < profile.Layers.Count; i++)
        {
            var layer = profile.Layers[i];
            var existing = library.FirstOrDefault(item => item.Id == layer.Id);
            if (existing == null)
            {
                library.Add(layer);
                continue;
            }

            if (layer.Id == ProfilePresets.ProfileSelectId)
            {
                profile.Layers[i] = existing;
                continue;
            }

            string fresh = Guid.NewGuid().ToString("N");
            moved[layer.Id] = fresh;
            layer.Id = fresh;
            library.Add(layer);
        }

        foreach (var pair in moved)
        {
            if (profile.BaseLayerId == pair.Key)
                profile.BaseLayerId = pair.Value;
            foreach (var layer in profile.Layers)
            {
                foreach (var binding in layer.Bindings.Values)
                    Point(binding, pair.Key, pair.Value);
            }
        }

        if (profile.GetLayer(profile.BaseLayerId) == null && profile.Layers.Count > 0)
            profile.BaseLayerId = profile.Layers[0].Id;
    }

    private static void Point(Binding? binding, string oldId, string newId)
    {
        if (binding == null)
            return;
        if (binding.LayerId == oldId)
            binding.LayerId = newId;
        Point(binding.Tap, oldId, newId);
    }

    private static string UniqueName(string name)
    {
        var names = App.Runtime.Document.Profiles.Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name))
            return name;
        for (int i = 2; ; i++)
        {
            string candidate = $"{name} {i}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static Profile Clone(Profile source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source, ProfileStore.Options);
        return System.Text.Json.JsonSerializer.Deserialize<Profile>(json, ProfileStore.Options) ?? ProfilePresets.Blank();
    }
}
