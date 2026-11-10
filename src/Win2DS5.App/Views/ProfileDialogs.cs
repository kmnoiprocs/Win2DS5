using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Win2DS5.App.Controls;
using Win2DS5.Core;
using Layout = Win2DS5.Core.Layout;

namespace Win2DS5.App.Views;

public static class ProfileDialogs
{
    public static async Task<Profile?> CreateAsync(XamlRoot? root, ProfileDocument document)
    {
        Layout? chosen = null;
        if (root != null)
        {
            var library = document.LayoutLibrary;
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
                XamlRoot = root,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return null;
            if (choices.SelectedIndex > 0 && choices.SelectedIndex - 1 < library.Count)
                chosen = library[choices.SelectedIndex - 1];
        }

        return Build(document, chosen);
    }

    public static Profile Build(ProfileDocument document, Layout? shared)
    {
        var profile = ProfilePresets.Blank();
        profile.Inputs.Clear();
        profile.Name = UniqueName(document, "New profile");
        if (shared != null)
        {
            profile.Layouts = [shared];
            profile.BaseLayoutId = shared.Id;
            return profile;
        }

        var layout = new Layout { Id = Guid.NewGuid().ToString("N"), Name = BlankLayoutName(document, profile.Name) };
        document.LayoutLibrary.Add(layout);
        profile.Layouts = [layout];
        profile.BaseLayoutId = layout.Id;
        return profile;
    }

    private static string UniqueName(ProfileDocument document, string name)
    {
        var names = document.Profiles.Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name))
            return name;
        for (int i = 2; ; i++)
        {
            string candidate = $"{name} {i}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static string BlankLayoutName(ProfileDocument document, string profileName)
    {
        var names = document.LayoutLibrary.Select(layout => layout.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
}
