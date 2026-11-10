using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Win2DS5.App.Controls;
using Win2DS5.Core;
using Layout = Win2DS5.Core.Layout;

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
        ShowLayouts();
        _loading = false;
    }

    private void ShowLayouts()
    {
        LayoutList.Items.Clear();
        var document = App.Runtime.Document;
        var bases = new List<Layout>();
        foreach (var profile in document.Profiles)
        {
            if (profile.Layouts.Count == 0)
                continue;
            var layout = document.LayoutLibrary.FirstOrDefault(item => item.Id == profile.BaseLayout.Id) ?? profile.BaseLayout;
            if (bases.All(item => item.Id != layout.Id))
                bases.Add(layout);
        }

        var ordered = bases.Concat(document.LayoutLibrary.Where(layout => bases.All(item => item.Id != layout.Id)));
        foreach (var layout in ordered)
        {
            var users = document.Profiles.Where(profile => profile.Layouts.Any(item => item.Id == layout.Id)).Select(profile => profile.Name).ToList();
            var homes = document.Profiles.Where(profile => profile.Layouts.Count > 0 && profile.BaseLayout.Id == layout.Id).Select(profile => profile.Name).ToList();
            string text = layout.Name;
            if (homes.Count > 0)
                text += "  ·  base of " + string.Join(", ", homes);
            else if (users.Count > 0)
                text += "  ·  " + string.Join(", ", users);
            else
                text += "  ·  not used";
            LayoutList.Items.Add(text);
        }

        if (LayoutList.Items.Count == 0)
            LayoutList.Items.Add("No saved layouts yet");
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
        BaseLayoutBox.Items.Clear();
        foreach (var layout in profile.Layouts)
            BaseLayoutBox.Items.Add(layout.Name);
        int baseIndex = profile.Layouts.FindIndex(layout => layout.Id == profile.BaseLayout.Id);
        BaseLayoutBox.SelectedIndex = baseIndex >= 0 ? baseIndex : 0;
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
            if (BaseLayoutBox.SelectedIndex >= 0 && BaseLayoutBox.SelectedIndex < profile.Layouts.Count)
                ChooseBase(profile, profile.Layouts[BaseLayoutBox.SelectedIndex]);
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

        var library = App.Runtime.Document.LayoutLibrary;
        var choices = new QuietComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 280 };
        choices.Items.Add("New blank base layout");
        foreach (var layout in library)
            choices.Items.Add(layout.Name);
        choices.SelectedIndex = 0;
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            Text = "Choose the saved layout this profile starts from.",
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

        Layout? chosen = choices.SelectedIndex > 0 && choices.SelectedIndex - 1 < library.Count
            ? library[choices.SelectedIndex - 1]
            : null;
        var profile = BlankProfile(chosen);
        profile.Name = UniqueName("New profile");
        AddExisting(profile);
    }

    private Profile BlankProfile(Layout? shared)
    {
        var profile = ProfilePresets.Blank();
        profile.Inputs.Clear();
        if (shared != null)
        {
            profile.Layouts = [shared];
            profile.BaseLayoutId = shared.Id;
            return profile;
        }

        string name = BlankLayoutName(UniqueName("New profile"));
        var layout = new Layout { Id = Guid.NewGuid().ToString("N"), Name = name };
        App.Runtime.Document.LayoutLibrary.Add(layout);
        profile.Layouts = [layout];
        profile.BaseLayoutId = layout.Id;
        return profile;
    }

    private static string BlankLayoutName(string profileName)
    {
        var names = App.Runtime.Document.LayoutLibrary.Select(layout => layout.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        AdoptPresetLayouts(created);
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

    private static void ChooseBase(Profile profile, Layout layout)
    {
        profile.Layouts.RemoveAll(item => item.Id == layout.Id);
        profile.Layouts.Insert(0, layout);
        profile.BaseLayoutId = layout.Id;
    }

    private static void AdoptPresetLayouts(Profile profile)
    {
        var library = App.Runtime.Document.LayoutLibrary;
        var moved = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < profile.Layouts.Count; i++)
        {
            var layout = profile.Layouts[i];
            var existing = library.FirstOrDefault(item => item.Id == layout.Id);
            if (existing == null)
            {
                library.Add(layout);
                continue;
            }

            if (layout.Id == ProfilePresets.ProfileSelectId)
            {
                profile.Layouts[i] = existing;
                continue;
            }

            string fresh = Guid.NewGuid().ToString("N");
            moved[layout.Id] = fresh;
            layout.Id = fresh;
            library.Add(layout);
        }

        foreach (var pair in moved)
        {
            if (profile.BaseLayoutId == pair.Key)
                profile.BaseLayoutId = pair.Value;
            foreach (var layout in profile.Layouts)
            {
                foreach (var binding in layout.Bindings.Values)
                    Point(binding, pair.Key, pair.Value);
            }
        }

        if (profile.GetLayout(profile.BaseLayoutId) == null && profile.Layouts.Count > 0)
            profile.BaseLayoutId = profile.Layouts[0].Id;
    }

    private static void Point(Binding? binding, string oldId, string newId)
    {
        if (binding == null)
            return;
        if (binding.LayoutId == oldId)
            binding.LayoutId = newId;
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
