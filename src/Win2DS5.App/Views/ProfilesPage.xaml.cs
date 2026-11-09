using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Win2DS5.Core;

namespace Win2DS5.App.Views;

public sealed partial class ProfilesPage : UserControl
{
    private bool _loading = true;

    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public event Action? ProfileChanged;

    private void Reload()
    {
        _loading = true;
        ProfileList.Items.Clear();
        foreach (var profile in App.Runtime.Document.Profiles)
            ProfileList.Items.Add(profile.Name);
        var active = App.Runtime.Document.Active;
        ProfileList.SelectedIndex = App.Runtime.Document.Profiles.FindIndex(profile => profile.Id == active.Id);
        NameBox.Text = active.Name;
        ProcessBox.Text = string.Join(", ", active.ProcessNames);
        _loading = false;
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileList.SelectedIndex < 0)
            return;
        var chosen = App.Runtime.Document.Profiles[ProfileList.SelectedIndex];
        App.Runtime.Update(() => App.Runtime.Document.ActiveId = chosen.Id, true);
        App.Runtime.Save();
        NameBox.Text = chosen.Name;
        ProcessBox.Text = string.Join(", ", chosen.ProcessNames);
        ProfileChanged?.Invoke();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        App.Runtime.Update(() =>
        {
            var profile = App.Runtime.Document.Active;
            profile.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? profile.Name : NameBox.Text.Trim();
            profile.ProcessNames = ProcessBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        });
        App.Runtime.Save();
        Reload();
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        var copy = Clone(App.Runtime.Document.Active);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name += " copy";
        App.Runtime.Update(() =>
        {
            App.Runtime.Document.Profiles.Add(copy);
            App.Runtime.Document.ActiveId = copy.Id;
        }, true);
        App.Runtime.Save();
        Reload();
        ProfileChanged?.Invoke();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (App.Runtime.Document.Profiles.Count < 2)
            return;
        App.Runtime.Update(() =>
        {
            var document = App.Runtime.Document;
            document.Profiles.RemoveAll(profile => profile.Id == document.ActiveId);
            document.ActiveId = document.Profiles[0].Id;
        }, true);
        App.Runtime.Save();
        Reload();
        ProfileChanged?.Invoke();
    }

    private void OnMouseHand(object sender, RoutedEventArgs e) => AddPreset(ProfilePresets.MouseHand());

    private void OnTriggerShift(object sender, RoutedEventArgs e) => AddPreset(ProfilePresets.TriggerShift());

    private void AddPreset(Profile profile)
    {
        App.Runtime.Update(() =>
        {
            App.Runtime.Document.Profiles.Add(profile);
            App.Runtime.Document.ActiveId = profile.Id;
        }, true);
        App.Runtime.Save();
        Reload();
        ProfileChanged?.Invoke();
    }

    private static Profile Clone(Profile source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source, ProfileStore.Options);
        return System.Text.Json.JsonSerializer.Deserialize<Profile>(json, ProfileStore.Options) ?? ProfilePresets.MouseHand();
    }
}
