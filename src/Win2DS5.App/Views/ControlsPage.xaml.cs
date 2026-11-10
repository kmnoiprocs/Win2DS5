using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Win2DS5.App.Controls;
using Win2DS5.App.Runtime;
using Win2DS5.App.Theming;
using Win2DS5.Core;
using Win2DS5.DualSense;
using Windows.System;

namespace Win2DS5.App.Views;

public sealed partial class ControlsPage : UserControl
{
    private enum BindingSection { Sticks, Touchpad, Triggers, Buttons }

    private readonly DesktopRuntime _runtime = App.Runtime;
    private string _profileId = "";
    private string _layerId = "base";
    private string _tabSignature = "";
    private ControlId _control = ControlId.Cross;
    private string _outputSignature = "";
    private readonly Dictionary<string, Layer> _drafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _appliedDrafts = new(StringComparer.Ordinal);
    private BindingSection _section = BindingSection.Buttons;
    private bool _loading = true;
    private bool _renaming;
    private bool _cancelRename;
    private bool _renamingLayer;
    private bool _cancelLayerRename;
    private bool _colorGuard;
    private bool _shellDirty;
    private bool _layerDirty;
    private string _liveLayerId = "";
    private string _swatchProfile = "\0";
    private string _swatchLayer = "\0";

    private readonly ComboBox _leftMode = new QuietComboBox { CornerRadius = new CornerRadius(9) };
    private readonly ComboBox _rightMode = new QuietComboBox { CornerRadius = new CornerRadius(9) };
    private readonly ComboBox _leftOutput = new QuietComboBox { CornerRadius = new CornerRadius(9) };
    private readonly ComboBox _rightOutput = new QuietComboBox { CornerRadius = new CornerRadius(9) };
    private readonly Slider _leftSpeed = new() { Minimum = 200, Maximum = 4000 };
    private readonly Slider _leftDead = new() { Minimum = 0, Maximum = 0.4, StepFrequency = 0.01 };
    private readonly Slider _leftCurve = new() { Minimum = 0.6, Maximum = 3, StepFrequency = 0.05 };
    private readonly Slider _rightSpeed = new() { Minimum = 200, Maximum = 4000 };
    private readonly Slider _rightDead = new() { Minimum = 0, Maximum = 0.4, StepFrequency = 0.01 };
    private readonly Slider _rightCurve = new() { Minimum = 0.6, Maximum = 3, StepFrequency = 0.05 };
    private readonly TextBlock _leftWarn = Warn();
    private readonly TextBlock _rightWarn = Warn();
    private readonly ComboBox _touchMode = new QuietComboBox { CornerRadius = new CornerRadius(9) };
    private readonly Slider _touchSpeed = new() { Minimum = 0.4, Maximum = 3, StepFrequency = 0.05 };
    private readonly Slider _touchHold = new() { Minimum = 200, Maximum = 800, StepFrequency = 10 };
    private readonly CheckBox _twoFinger = new() { Content = "Two-finger scroll" };
    private readonly CheckBox _edgeScroll = new() { Content = "Edge scroll" };
    private readonly ComboBox _zoneBox = new QuietComboBox { CornerRadius = new CornerRadius(9) };
    private readonly ComboBox _gestureBox = new QuietComboBox { CornerRadius = new CornerRadius(9) };
    private readonly TextBlock _touchSpeedLabel = new();
    private readonly TextBlock _touchSpeedValue = Value();
    private readonly TextBlock _touchHoldValue = Value();

    public ControlsPage()
    {
        InitializeComponent();
        BuildStickSection();
        BuildTouchSection();
        Diagram.ControlChosen += OnControlChosen;
        Diagram.HoverChanged += OnHover;
        Editor.BindingChanged += OnBindingChanged;
        Editor.ShowChrome(false);
        Motion.Fade(HoverCard, 140);
        Motion.Fade(HintText, 140);
        HoverCard.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(180) };
        HoverCard.Translation = new Vector3(0, 6, 0);
        Loaded += (_, _) =>
        {
            Editor.Attach(this);
            ButtonEditor.Changed += OnBindingChanged;
            ButtonEditor.BrowseFile = () => PickPath(folder: false);
            ButtonEditor.BrowseFolder = () => PickPath(folder: true);
            ButtonEditor.AdvancedChanged += advanced =>
            {
                if (_section == BindingSection.Buttons)
                    ApplyButtonSplit(advanced);
            };
            ReloadMaps();
        };
    }

    public void Apply(RuntimeSnapshot snapshot)
    {
        RefreshControllerTabs();
        var profile = _runtime.Editing;
        if (profile.Id != _profileId || ProfileLabel.Text != profile.Name)
            ReloadMaps();
        var signature = OutputSignature();
        if (signature != _outputSignature)
        {
            _outputSignature = signature;
            if (_section == BindingSection.Buttons)
                ButtonEditor.RefreshOutputs(_runtime.Document.Outputs);
            else if (_section == BindingSection.Sticks)
                LoadSticks();
            else if (_section == BindingSection.Touchpad)
                LoadTouch();
        }
        _liveLayerId = snapshot.ActiveLayerId;
        Diagram.Apply(snapshot);
        SyncAppearance();
    }

    public void ReloadMaps()
    {
        string profileId = _runtime.Editing.Id;
        if (profileId != _profileId)
        {
            _drafts.Clear();
            _appliedDrafts.Clear();
        }
        _profileId = profileId;
        if (_runtime.Editing.GetLayer(_layerId) == null)
            _layerId = _runtime.Editing.BaseLayer.Id;
        if (!_renaming)
            ProfileLabel.Text = _runtime.Editing.Name;
        ControllerTitle.Text = ControllerName();
        RefreshLayers();
        ShowSection();
        SyncBindingActions();
    }

    private void RefreshControllerTabs()
    {
        var inputs = DualSenseConnection.List();
        string selected = _runtime.ActiveInputKey;
        if (inputs.Count > 0 && inputs.All(item => item.Path != selected))
        {
            _runtime.SelectInput(inputs[0].Path);
            selected = inputs[0].Path;
        }

        string signature = selected + "|" + string.Join("|", inputs.Select(item => item.Path + item.Name));
        if (signature == _tabSignature)
            return;
        _tabSignature = signature;
        ControllerTabs.Children.Clear();
        if (inputs.Count == 0)
        {
            ControllerTabs.Children.Add(Tab("No controller", true, () => { }));
            ControllerTitle.Text = "No controller";
            return;
        }

        foreach (var input in inputs)
        {
            bool on = input.Path == selected;
            string path = input.Path;
            ControllerTabs.Children.Add(Tab(input.Name, on, () =>
            {
                if (path == _runtime.ActiveInputKey)
                    return;
                _runtime.SelectInput(path);
                _tabSignature = "";
                ReloadMaps();
            }));
        }

        ControllerTitle.Text = ControllerName();
    }

    private void OnProfileMenu(object sender, RoutedEventArgs e)
    {
        if (_renaming)
            return;
        var flyout = new MenuFlyout();
        string current = _runtime.Editing.Id;
        foreach (var profile in _runtime.Document.Profiles)
        {
            string id = profile.Id;
            var item = new MenuFlyoutItem
            {
                Text = profile.Name,
                Icon = new FontIcon { Glyph = id == current ? "\uE73E" : "\uE770" },
            };
            item.Click += (_, _) =>
            {
                if (id == _runtime.Editing.Id)
                    return;
                _runtime.AssignToActive(id);
                ReloadMaps();
            };
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = "New profile", Icon = new FontIcon { Glyph = "\uE710" } };
        create.Click += (_, _) => CreateProfile();
        flyout.Items.Add(create);
        flyout.ShowAt(ProfileButton);
    }

    private void CreateProfile()
    {
        var profile = ProfilePresets.Blank();
        profile.Name = UniqueName("New profile");
        _runtime.Update(() => _runtime.Document.Profiles.Add(profile));
        _runtime.AssignToActive(profile.Id);
        _runtime.Save();
        ReloadMaps();
    }

    private void OnSaveProfile(object sender, RoutedEventArgs e)
    {
        if (_renaming)
            CommitRename();
        _runtime.Save();
        _runtime.Notify("Saved " + _runtime.Editing.Name);
    }

    private void OnRenameProfile(object sender, RoutedEventArgs e)
    {
        _renaming = true;
        _cancelRename = false;
        ProfileNameBox.Text = _runtime.Editing.Name;
        ProfileNameBox.Visibility = Visibility.Visible;
        ProfileButton.Visibility = Visibility.Collapsed;
        ProfileNameBox.Focus(FocusState.Programmatic);
        ProfileNameBox.SelectAll();
    }

    private void OnProfileNameKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            _cancelRename = true;
            _renaming = false;
            ProfileNameBox.Visibility = Visibility.Collapsed;
            ProfileButton.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }

    private void OnProfileNameLost(object sender, RoutedEventArgs e)
    {
        if (_cancelRename)
        {
            _cancelRename = false;
            return;
        }

        CommitRename();
    }

    private void CommitRename()
    {
        if (!_renaming)
            return;
        _renaming = false;
        string name = ProfileNameBox.Text.Trim();
        if (name.Length > 0 && name != _runtime.Editing.Name)
        {
            _runtime.Update(() => _runtime.Editing.Name = name);
            _runtime.Save();
        }

        ProfileLabel.Text = _runtime.Editing.Name;
        ProfileNameBox.Visibility = Visibility.Collapsed;
        ProfileButton.Visibility = Visibility.Visible;
    }

    private async void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        var profile = _runtime.Editing;
        if (_runtime.Document.Profiles.Count < 2)
        {
            var blocked = new ContentDialog
            {
                Title = "Delete profile",
                Content = "Keep at least one profile.",
                CloseButtonText = "Close",
                XamlRoot = XamlRoot,
            };
            await blocked.ShowAsync();
            return;
        }

        string fallbackName = _runtime.Document.Profiles.First(item => item.Id != profile.Id).Name;
        var dialog = new ContentDialog
        {
            Title = "Delete profile",
            Content = $"Delete {profile.Name}? Controllers using it will switch to {fallbackName}.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        ShellFiles.Delete(profile.ShellImage);
        _runtime.Update(() =>
        {
            var document = _runtime.Document;
            string fallback = document.Profiles.First(item => item.Id != profile.Id).Id;
            document.Profiles.RemoveAll(item => item.Id == profile.Id);
            foreach (var controller in document.Controllers.Where(item => item.ProfileId == profile.Id))
                controller.ProfileId = fallback;
            if (document.ActiveId == profile.Id)
                document.ActiveId = fallback;
        }, true);
        _runtime.Save();
        ReloadMaps();
    }

    private void OnControlChosen(ControlId id)
    {
        _control = id;
        _section = id switch
        {
            ControlId.L2 or ControlId.R2 or ControlId.L2Full or ControlId.R2Full => BindingSection.Triggers,
            ControlId.L3 or ControlId.R3 => BindingSection.Sticks,
            ControlId.TouchpadClick => BindingSection.Touchpad,
            _ => BindingSection.Buttons,
        };
        ShowSection();
    }

    private void ShowSection()
    {
        BindingTitle.Text = SelectedLayer().Name;
        RefreshSectionTabs();
        bool sticks = _section == BindingSection.Sticks;
        bool touch = _section == BindingSection.Touchpad;
        StickSection.Visibility = sticks ? Visibility.Visible : Visibility.Collapsed;
        TouchSection.Visibility = touch ? Visibility.Visible : Visibility.Collapsed;
        SectionScroll.Visibility = sticks || touch ? Visibility.Visible : Visibility.Collapsed;
        bool buttons = _section == BindingSection.Buttons;
        Editor.Visibility = buttons ? Visibility.Collapsed : Visibility.Visible;
        ButtonEditorScroll.Visibility = buttons ? Visibility.Visible : Visibility.Collapsed;
        ButtonListScroll.Visibility = buttons ? Visibility.Visible : Visibility.Collapsed;
        ControlChips.Visibility = buttons ? Visibility.Collapsed : Visibility.Visible;
        ExtraRow.Height = buttons ? new GridLength(1, GridUnitType.Star) : sticks || touch ? GridLength.Auto : new GridLength(0);
        EditorRow.Height = buttons ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        if (buttons)
            ApplyButtonSplit(ButtonEditor.IsAdvanced);
        SectionScroll.MaxHeight = sticks ? 300 : touch ? 240 : double.PositiveInfinity;
        var choices = ControlsIn(_section).ToList();
        if (choices.Count > 0 && !choices.Contains(_control))
            _control = choices[0];
        RefreshControlChips();
        if (_section == BindingSection.Buttons)
            RefreshButtonList();
        if (sticks)
            LoadSticks();
        else if (touch)
            LoadTouch();
        LoadEditor();
        _outputSignature = OutputSignature();
        Diagram.Select(_control);
    }

    private void RefreshSectionTabs()
    {
        SectionTabs.Children.Clear();
        AddSection("Buttons", BindingSection.Buttons);
        AddSection("Analog sticks", BindingSection.Sticks);
        AddSection("Touchpad", BindingSection.Touchpad);
        AddSection("Triggers", BindingSection.Triggers);
    }

    private void AddSection(string label, BindingSection section)
    {
        SectionTabs.Children.Add(Tab(label, _section == section, () =>
        {
            if (_section == section)
                return;
            _section = section;
            ShowSection();
        }));
    }

    private void RefreshControlChips()
    {
        ControlChips.Children.Clear();
        foreach (var id in ControlsIn(_section))
        {
            ControlId pick = id;
            ControlChips.Children.Add(Pill(ControlNames.Display(pick), pick == _control, () =>
            {
                _control = pick;
                Diagram.Select(pick);
                RefreshControlChips();
                LoadEditor();
            }));
        }
    }

    private void OnHover(ControlId? id)
    {
        if (id is not { } control)
        {
            HoverCard.Opacity = 0;
            HoverCard.Translation = new Vector3(0, 6, 0);
            HintText.Opacity = 1;
            return;
        }

        var layer = EditedLayer();
        HoverName.Text = ControlNames.Display(control);
        HoverBinding.Text = Describe(layer.Get(control));
        HoverCard.Opacity = 1;
        HoverCard.Translation = Vector3.Zero;
        HintText.Opacity = 0;
    }

    private void OnLayerMenu(object sender, RoutedEventArgs e)
    {
        if (_renamingLayer)
            return;
        var profile = _runtime.Editing;
        var flyout = new MenuFlyout();
        foreach (var layer in profile.Layers)
        {
            string id = layer.Id;
            var item = new MenuFlyoutItem
            {
                Text = layer.Id == profile.BaseLayer.Id ? layer.Name + "  ·  base" : layer.Name,
                Icon = new FontIcon { Glyph = id == _layerId ? "\uE73E" : "\uE770" },
            };
            item.Click += (_, _) => SelectLayer(id);
            flyout.Items.Add(item);
        }

        var library = _runtime.Document.LayerLibrary.Where(layer => profile.Layers.All(item => item.Id != layer.Id)).ToList();
        if (library.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            var add = new MenuFlyoutSubItem { Text = "Add a saved layer" };
            foreach (var layer in library)
            {
                string id = layer.Id;
                var item = new MenuFlyoutItem { Text = layer.Name, Icon = new FontIcon { Glyph = "\uE710" } };
                item.Click += (_, _) =>
                {
                    var shared = _runtime.Document.LayerLibrary.FirstOrDefault(candidate => candidate.Id == id);
                    if (shared == null || profile.Layers.Any(existing => existing.Id == id))
                        return;
                    _runtime.Update(() => profile.Layers.Add(shared));
                    _layerId = id;
                    _runtime.Save();
                    ReloadMaps();
                };
                add.Items.Add(item);
            }

            flyout.Items.Add(add);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = "New layer", Icon = new FontIcon { Glyph = "\uE710" } };
        create.Click += async (_, _) => await CreateLayer();
        flyout.Items.Add(create);
        flyout.ShowAt(LayerButton);
    }

    private void SelectLayer(string id)
    {
        if (id == _layerId)
            return;
        _layerId = id;
        RefreshLayers();
        LoadEditor();
    }

    private async Task CreateLayer()
    {
        if (XamlRoot == null)
            return;
        var box = new TextBox { PlaceholderText = "Layer name" };
        var dialog = new ContentDialog
        {
            Title = "New layer",
            Content = box,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        string name = string.IsNullOrWhiteSpace(box.Text) ? "Layer" : box.Text.Trim();
        _runtime.Update(() =>
        {
            var profile = _runtime.Editing;
            var layer = new Layer { Id = Guid.NewGuid().ToString("N"), Name = name };
            foreach (var control in ControlNames.Bindable)
                layer.Set(control, Binding.Base());
            _runtime.Document.LayerLibrary.Add(layer);
            profile.Layers.Add(layer);
            _layerId = layer.Id;
        }, true);
        _runtime.Save();
        RefreshLayers();
        LoadEditor();
    }

    private void RefreshLayers()
    {
        LayerTabs.Children.Clear();
        var profile = _runtime.Editing;
        var selected = profile.GetLayer(_layerId) ?? profile.BaseLayer;
        _layerId = selected.Id;
        if (!_renamingLayer)
            LayerLabel.Text = selected.Name;
        BindingTitle.Text = selected.Name;
        foreach (var layer in profile.Layers)
        {
            string id = layer.Id;
            LayerTabs.Children.Add(Tab(layer.Name, id == _layerId, () => SelectLayer(id), id == profile.BaseLayer.Id));
        }

        SyncAppearance();

        if (ButtonListScroll.Visibility == Visibility.Visible)
            RefreshButtonList();
    }

    private void OnSaveLayer(object sender, RoutedEventArgs e)
    {
        if (_renamingLayer)
            CommitLayerRename();
        var layer = SelectedLayer();
        _runtime.Save();
        _runtime.Notify("Saved " + layer.Name);
    }

    private void OnRenameLayer(object sender, RoutedEventArgs e)
    {
        var layer = SelectedLayer();
        _renamingLayer = true;
        _cancelLayerRename = false;
        LayerNameBox.Text = layer.Name;
        LayerNameBox.Visibility = Visibility.Visible;
        LayerButton.Visibility = Visibility.Collapsed;
        LayerNameBox.Focus(FocusState.Programmatic);
        LayerNameBox.SelectAll();
    }

    private void OnLayerNameKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitLayerRename();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            _cancelLayerRename = true;
            _renamingLayer = false;
            LayerNameBox.Visibility = Visibility.Collapsed;
            LayerButton.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }

    private void OnLayerNameLost(object sender, RoutedEventArgs e)
    {
        if (_cancelLayerRename)
        {
            _cancelLayerRename = false;
            return;
        }

        CommitLayerRename();
    }

    private void CommitLayerRename()
    {
        if (!_renamingLayer)
            return;
        _renamingLayer = false;
        var layer = SelectedLayer();
        string name = LayerNameBox.Text.Trim();
        if (name.Length > 0 && name != layer.Name)
        {
            _runtime.Update(() => layer.Name = name);
            _runtime.Save();
        }

        LayerLabel.Text = layer.Name;
        LayerNameBox.Visibility = Visibility.Collapsed;
        LayerButton.Visibility = Visibility.Visible;
        RefreshLayers();
    }

    private async void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (XamlRoot == null)
            return;
        var layer = SelectedLayer();
        var profiles = _runtime.Document.Profiles.Where(profile => profile.Layers.Any(item => item.Id == layer.Id)).ToList();
        var blocked = profiles.Where(profile => profile.Layers.Count < 2).Select(profile => profile.Name).ToList();
        string used = profiles.Count == 0
            ? "No profile is using this layer."
            : "Used by:\n" + string.Join("\n", profiles.Select(profile => "• " + profile.Name));
        if (blocked.Count > 0)
        {
            var stopped = new ContentDialog
            {
                Title = "Delete layer",
                Content = $"{layer.Name} is the only layer on {string.Join(", ", blocked)}. Keep at least one layer on each profile.\n\n{used}",
                CloseButtonText = "Close",
                XamlRoot = XamlRoot,
            };
            await stopped.ShowAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete layer",
            Content = $"Delete {layer.Name}?\n\n{used}",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        _drafts.Remove(layer.Id);
        _appliedDrafts.Remove(layer.Id);
        _runtime.Update(() =>
        {
            _runtime.Document.LayerLibrary.RemoveAll(item => item.Id == layer.Id);
            foreach (var profile in _runtime.Document.Profiles)
            {
                bool home = profile.BaseLayerId == layer.Id;
                profile.Layers.RemoveAll(item => item.Id == layer.Id);
                if (home && profile.Layers.Count > 0)
                    profile.BaseLayerId = profile.Layers[0].Id;
            }
        }, true);
        _runtime.Save();
        ReloadMaps();
    }

    private void OnProfileColorOpened(object sender, object e)
    {
        _colorGuard = true;
        var profile = _runtime.Editing;
        ProfileColorPicker.Color = Theme.TryHex(profile.ShellColor, out var color) ? color : Theme.Hex("#262B35");
        _colorGuard = false;
    }

    private void OnProfileColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_colorGuard)
            return;
        var profile = _runtime.Editing;
        string hex = Theme.ToHex(args.NewColor);
        if (hex == profile.ShellColor && profile.ShellImage.Length == 0)
            return;
        _shellDirty = true;
        ShellFiles.Delete(profile.ShellImage);
        profile.ShellImage = "";
        profile.ShellColor = hex;
        Diagram.SetShell(hex, "");
        _swatchProfile = "\0";
        SyncAppearance();
    }

    private void OnProfileColorClosed(object sender, object e)
    {
        if (!_shellDirty)
            return;
        _shellDirty = false;
        _runtime.Save();
    }

    private void OnProfileColorDefault(object sender, RoutedEventArgs e)
    {
        var profile = _runtime.Editing;
        profile.ShellColor = "";
        _shellDirty = true;
        Diagram.SetShell("", ShellFiles.Existing(profile.ShellImage));
        _swatchProfile = "\0";
        SyncAppearance();
        ProfileColorButton.Flyout.Hide();
    }

    private async void OnProfileImage(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null)
            return;
        var profile = _runtime.Editing;
        Directory.CreateDirectory(ShellFiles.Folder);
        ShellFiles.Delete(profile.ShellImage);
        string name = profile.Id + Path.GetExtension(file.Path);
        if (Path.GetExtension(name).Length == 0)
            name += ".png";
        File.Copy(file.Path, Path.Combine(ShellFiles.Folder, name), true);
        profile.ShellImage = name;
        profile.ShellColor = "";
        _runtime.Save();
        Diagram.SetShell("", ShellFiles.Existing(name));
        _swatchProfile = "\0";
        SyncAppearance();
    }

    private void OnClearShell(object sender, RoutedEventArgs e)
    {
        var profile = _runtime.Editing;
        ShellFiles.Delete(profile.ShellImage);
        profile.ShellImage = "";
        profile.ShellColor = "";
        _shellDirty = false;
        _runtime.Save();
        Diagram.SetShell("", "");
        _swatchProfile = "\0";
        SyncAppearance();
    }

    private void OnLayerColorOpened(object sender, object e)
    {
        _colorGuard = true;
        var layer = SelectedLayer();
        LayerColorPicker.Color = Theme.TryHex(layer.Light, out var color) ? color : Theme.Accent;
        _colorGuard = false;
    }

    private void OnLayerColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_colorGuard)
            return;
        var layer = SelectedLayer();
        string hex = Theme.ToHex(args.NewColor);
        if (hex == layer.Light)
            return;
        _layerDirty = true;
        layer.Light = hex;
        if (layer.Id == _liveLayerId || _liveLayerId.Length == 0)
        {
            Diagram.SetLight(hex);
            _runtime.RefreshLights();
        }

        _swatchLayer = "\0";
        SyncAppearance();
    }

    private void OnLayerColorClosed(object sender, object e)
    {
        if (!_layerDirty)
            return;
        _layerDirty = false;
        _runtime.Save();
        _runtime.RefreshLights();
    }

    private void OnLayerColorDefault(object sender, RoutedEventArgs e)
    {
        var layer = SelectedLayer();
        layer.Light = "";
        _layerDirty = false;
        _runtime.Save();
        _runtime.RefreshLights();
        if (layer.Id == _liveLayerId || _liveLayerId.Length == 0)
            Diagram.SetLight("");
        _swatchLayer = "\0";
        SyncAppearance();
        LayerColorButton.Flyout.Hide();
    }

    private void SyncAppearance()
    {
        var profile = _runtime.Editing;
        var layer = SelectedLayer();
        string profileKey = profile.Id + "|" + profile.ShellColor + "|" + profile.ShellImage;
        if (profileKey != _swatchProfile)
        {
            _swatchProfile = profileKey;
            ProfileSwatch.Fill = Swatch(profile.ShellColor, profile.ShellImage);
            ProfileShellClear.IsEnabled = profile.ShellColor.Length > 0 || profile.ShellImage.Length > 0;
        }

        string layerKey = layer.Id + "|" + layer.Light;
        if (layerKey != _swatchLayer)
        {
            _swatchLayer = layerKey;
            LayerSwatch.Fill = Theme.TryHex(layer.Light, out var color)
                ? new SolidColorBrush(color)
                : Theme.Brush("AccentBrush");
        }
    }

    private static Brush Swatch(string color, string image)
    {
        string path = ShellFiles.Existing(image);
        if (path.Length > 0)
        {
            var bitmap = new BitmapImage();
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path);
            return new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
        }

        return Theme.TryHex(color, out var paint) ? new SolidColorBrush(paint) : Theme.Brush("PadShellBrush");
    }

    private Layer SelectedLayer() => _runtime.Editing.GetLayer(_layerId) ?? _runtime.Editing.BaseLayer;

    private void LoadEditor()
    {
        var profile = _runtime.Editing;
        if (_section == BindingSection.Touchpad && _control != ControlId.TouchpadClick)
        {
            if (_zoneBox.SelectedIndex < 0 || _gestureBox.SelectedIndex < 0)
                return;
            var zone = (TouchZone)_zoneBox.SelectedIndex;
            var kind = (GestureKind)_gestureBox.SelectedIndex;
            Editor.Show($"{zone} {kind}", "A drag never fires a zone. Tap and long-press do.", profile.Touchpad.GetGesture(zone, kind), profile.Layers, false, false, false, _runtime.Document.Outputs);
            return;
        }

        var layer = WorkingLayer();
        var binding = layer.Get(_control);
        if (_section == BindingSection.Buttons)
        {
            ButtonEditor.Load(binding, profile.Layers, _runtime.Document.Profiles, _runtime.Document.Outputs);
            SyncBindingActions();
            return;
        }
        var home = _drafts.TryGetValue(profile.BaseLayer.Id, out var baseDraft) ? baseDraft : profile.BaseLayer;
        bool locked = _layerId != profile.BaseLayer.Id && home.Get(_control)?.Kind is BindingKind.LayerHold or BindingKind.LayerToggle or BindingKind.ToggleKeyboard;
        string hint = locked
            ? $"On {profile.BaseLayer.Name}, this control changes layers or opens the keyboard, so it stays available in every layer."
            : "Pick the output this button should drive, then choose the action.";
        Editor.Show(ControlNames.Display(_control), hint, binding, profile.Layers, !locked, !locked, _layerId != profile.BaseLayer.Id && !locked, _runtime.Document.Outputs);
        SyncBindingActions();
    }

    private void OnBindingChanged(Binding? binding)
    {
        if (_section == BindingSection.Touchpad && _control != ControlId.TouchpadClick)
        {
            if (_zoneBox.SelectedIndex < 0 || _gestureBox.SelectedIndex < 0)
                return;
            var zone = (TouchZone)_zoneBox.SelectedIndex;
            var kind = (GestureKind)_gestureBox.SelectedIndex;
            _runtime.Update(() => _runtime.Editing.Touchpad.SetGesture(zone, kind, binding), true);
            _runtime.Save();
            return;
        }

        if (binding != null)
            ButtonCatalog.Aim(binding, _runtime.Document.Outputs);
        WorkingLayer().Set(_control, binding);
        SyncBindingActions();
        if (_section == BindingSection.Buttons)
            RefreshButtonList();
    }

    private void OnApplyBindings(object sender, RoutedEventArgs e)
    {
        if (!AnyDraftDirty())
            return;
        var profile = _runtime.Editing;
        var layout = InputLayout.FromProfile(profile, _runtime.ActiveInputKey);
        foreach (var pair in _drafts)
        {
            if (!DraftDirty(pair.Key))
                continue;
            layout.GetLayer(pair.Key)?.ReplaceBindings(pair.Value);
        }

        if (!_runtime.ApplyControllerMap(layout, profile.Id))
        {
            _runtime.Notify("Connect a controller to apply these mappings");
            return;
        }

        foreach (var pair in _drafts)
            _appliedDrafts[pair.Key] = SerializeBindings(pair.Value);
        _runtime.Notify("Applied on this controller");
        SyncBindingActions();
    }

    private void OnSaveBindings(object sender, RoutedEventArgs e)
    {
        if (!_drafts.ContainsKey(_layerId) || !DraftDirty(_layerId))
            return;
        var draft = _drafts[_layerId];
        var layer = SelectedLayer();
        _runtime.SaveLayerBindings(layer.Id, draft);
        _runtime.Save();
        _appliedDrafts[layer.Id] = SerializeBindings(draft);
        _runtime.Notify("Saved " + layer.Name);
        SyncBindingActions();
        if (_section == BindingSection.Buttons)
            RefreshButtonList();
    }

    private Layer WorkingLayer()
    {
        var shared = SelectedLayer();
        if (_drafts.TryGetValue(shared.Id, out var draft))
            return draft;
        draft = shared.Clone();
        _drafts[shared.Id] = draft;
        return draft;
    }

    private Layer EditedLayer()
    {
        var shared = SelectedLayer();
        return _drafts.TryGetValue(shared.Id, out var draft) ? draft : shared;
    }

    private bool DraftDirty(string layerId)
    {
        if (!_drafts.TryGetValue(layerId, out var draft))
            return false;
        var shared = _runtime.Editing.GetLayer(layerId);
        if (shared == null)
            return true;
        return SerializeBindings(draft) != SerializeBindings(shared);
    }

    private bool AnyDraftDirty() => _drafts.Keys.Any(DraftDirty);

    private static string SerializeBindings(Layer layer) =>
        System.Text.Json.JsonSerializer.Serialize(layer.Bindings, ProfileStore.Options);

    private bool NeedsApply()
    {
        foreach (var pair in _drafts)
        {
            string now = SerializeBindings(pair.Value);
            if (_appliedDrafts.TryGetValue(pair.Key, out var applied))
            {
                if (now != applied)
                    return true;
            }
            else if (DraftDirty(pair.Key))
            {
                return true;
            }
        }

        return false;
    }

    private void SyncBindingActions()
    {
        ApplyBindingsButton.IsEnabled = AnyDraftDirty();
        SaveBindingsButton.IsEnabled = DraftDirty(_layerId);
        BindingAlert.Visibility = NeedsApply() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildStickSection()
    {
        foreach (var mode in new[] { "Off", "Mouse", "Scroll", "Controller" })
        {
            _leftMode.Items.Add(mode);
            _rightMode.Items.Add(mode);
        }

        Wire(_leftMode, CommitSticks);
        Wire(_rightMode, CommitSticks);
        Wire(_leftOutput, CommitSticks);
        Wire(_rightOutput, CommitSticks);
        Wire(_leftSpeed, CommitSticks);
        Wire(_leftDead, CommitSticks);
        Wire(_leftCurve, CommitSticks);
        Wire(_rightSpeed, CommitSticks);
        Wire(_rightDead, CommitSticks);
        Wire(_rightCurve, CommitSticks);
        StickSection.Children.Add(StickBlock("Left stick", _leftMode, _leftOutput, _leftSpeed, _leftDead, _leftCurve, _leftWarn));
        StickSection.Children.Add(StickBlock("Right stick", _rightMode, _rightOutput, _rightSpeed, _rightDead, _rightCurve, _rightWarn));
    }

    private void BuildTouchSection()
    {
        foreach (var mode in new[] { "Off", "Trackpad", "Stick" })
            _touchMode.Items.Add(mode);
        foreach (var zone in new[] { "Center", "Left", "Right", "Top" })
            _zoneBox.Items.Add(zone);
        foreach (var gesture in new[] { "Tap", "Long press", "Click" })
            _gestureBox.Items.Add(gesture);
        Wire(_touchMode, CommitTouch);
        Wire(_touchSpeed, CommitTouch);
        Wire(_touchHold, CommitTouch);
        _twoFinger.Checked += (_, _) => CommitTouch();
        _twoFinger.Unchecked += (_, _) => CommitTouch();
        _edgeScroll.Checked += (_, _) => CommitTouch();
        _edgeScroll.Unchecked += (_, _) => CommitTouch();
        _zoneBox.SelectionChanged += (_, _) => SelectGesture();
        _gestureBox.SelectionChanged += (_, _) => SelectGesture();
        TouchSection.Children.Add(Field("Pointer mode", _touchMode));
        TouchSection.Children.Add(SliderField(_touchSpeedLabel, _touchSpeedValue, _touchSpeed));
        TouchSection.Children.Add(SliderField(new TextBlock { Text = "Long press" }, _touchHoldValue, _touchHold));
        TouchSection.Children.Add(_twoFinger);
        TouchSection.Children.Add(_edgeScroll);
        var zones = new Grid { ColumnSpacing = 8 };
        zones.ColumnDefinitions.Add(new ColumnDefinition());
        zones.ColumnDefinitions.Add(new ColumnDefinition());
        zones.Children.Add(Field("Zone", _zoneBox));
        var gestureField = Field("Gesture", _gestureBox);
        Grid.SetColumn(gestureField, 1);
        zones.Children.Add(gestureField);
        TouchSection.Children.Add(zones);
        TouchSection.Children.Add(new TextBlock
        {
            Text = "Zone size is edited on the Touchpad page.",
            Style = (Style)Application.Current.Resources["CaptionText"],
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private void LoadSticks()
    {
        _loading = true;
        var profile = _runtime.Editing;
        FillOutputs(_leftOutput, profile.LeftStick.OutputId);
        FillOutputs(_rightOutput, profile.RightStick.OutputId);
        _leftMode.SelectedIndex = (int)profile.LeftStick.Mode;
        _rightMode.SelectedIndex = (int)profile.RightStick.Mode;
        _leftSpeed.Value = profile.LeftStick.Sensitivity;
        _leftDead.Value = profile.LeftStick.Deadzone;
        _leftCurve.Value = profile.LeftStick.Curve;
        _rightSpeed.Value = profile.RightStick.Sensitivity;
        _rightDead.Value = profile.RightStick.Deadzone;
        _rightCurve.Value = profile.RightStick.Curve;
        ShowWarn(_leftWarn, profile.LeftStick);
        ShowWarn(_rightWarn, profile.RightStick);
        _loading = false;
    }

    private void CommitSticks()
    {
        if (_loading)
            return;
        _runtime.Update(() =>
        {
            var profile = _runtime.Editing;
            profile.LeftStick.Mode = (StickMode)Math.Max(0, _leftMode.SelectedIndex);
            profile.RightStick.Mode = (StickMode)Math.Max(0, _rightMode.SelectedIndex);
            profile.LeftStick.Sensitivity = _leftSpeed.Value;
            profile.LeftStick.Deadzone = _leftDead.Value;
            profile.LeftStick.Curve = _leftCurve.Value;
            profile.RightStick.Sensitivity = _rightSpeed.Value;
            profile.RightStick.Deadzone = _rightDead.Value;
            profile.RightStick.Curve = _rightCurve.Value;
            profile.LeftStick.OutputId = OutputAt(_leftOutput, profile.LeftStick.OutputId);
            profile.RightStick.OutputId = OutputAt(_rightOutput, profile.RightStick.OutputId);
            OutputCatalog.Remember(profile.LeftStick, _runtime.Document.Outputs);
            OutputCatalog.Remember(profile.RightStick, _runtime.Document.Outputs);
        });
        ShowWarn(_leftWarn, _runtime.Editing.LeftStick);
        ShowWarn(_rightWarn, _runtime.Editing.RightStick);
        _runtime.Save();
    }

    private void LoadTouch()
    {
        _loading = true;
        var touch = _runtime.Editing.Touchpad;
        _touchMode.SelectedIndex = (int)touch.Mode;
        _touchSpeed.Value = touch.Mode == TouchpadMode.Stick ? touch.StickSensitivity / 1000.0 : touch.Sensitivity;
        _touchHold.Value = touch.LongPressMs;
        _twoFinger.IsChecked = touch.TwoFingerScroll;
        _edgeScroll.IsChecked = touch.EdgeScroll;
        if (_zoneBox.SelectedIndex < 0)
            _zoneBox.SelectedIndex = 0;
        if (_gestureBox.SelectedIndex < 0)
            _gestureBox.SelectedIndex = 0;
        _touchSpeedLabel.Text = touch.Mode == TouchpadMode.Stick ? "Stick speed" : "Trackpad sensitivity";
        _touchSpeedValue.Text = touch.Mode == TouchpadMode.Stick ? $"{touch.StickSensitivity:0}" : $"{touch.Sensitivity:0.00}×";
        _touchHoldValue.Text = $"{touch.LongPressMs} ms";
        _loading = false;
    }

    private void SelectGesture()
    {
        if (_loading || _section != BindingSection.Touchpad)
            return;
        _control = ControlId.None;
        RefreshControlChips();
        LoadEditor();
    }

    private void CommitTouch()
    {
        if (_loading)
            return;
        _runtime.Update(() =>
        {
            var touch = _runtime.Editing.Touchpad;
            touch.Mode = (TouchpadMode)Math.Max(0, _touchMode.SelectedIndex);
            if (touch.Mode == TouchpadMode.Stick)
                touch.StickSensitivity = _touchSpeed.Value * 1000;
            else
                touch.Sensitivity = _touchSpeed.Value;
            touch.LongPressMs = (int)_touchHold.Value;
            touch.TwoFingerScroll = _twoFinger.IsChecked == true;
            touch.EdgeScroll = _edgeScroll.IsChecked == true;
        });
        var saved = _runtime.Editing.Touchpad;
        _touchSpeedLabel.Text = saved.Mode == TouchpadMode.Stick ? "Stick speed" : "Trackpad sensitivity";
        _touchSpeedValue.Text = saved.Mode == TouchpadMode.Stick ? $"{saved.StickSensitivity:0}" : $"{saved.Sensitivity:0.00}×";
        _touchHoldValue.Text = $"{saved.LongPressMs} ms";
        _runtime.Save();
    }

    private void ApplyButtonSplit(bool advanced)
    {
        ButtonEditorScroll.MaxHeight = advanced ? double.PositiveInfinity : 220;
        ButtonEditorScroll.VerticalAlignment = advanced ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        ExtraRow.Height = new GridLength(1, GridUnitType.Star);
        EditorRow.Height = advanced ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
    }

    private void RefreshButtonList()
    {
        ButtonList.Children.Clear();
        var layer = EditedLayer();
        foreach (var id in ControlsIn(BindingSection.Buttons))
        {
            ControlId pick = id;
            bool on = pick == _control;
            var row = new Grid { Height = 36, Padding = new Thickness(12, 0, 12, 0), ColumnSpacing = 12, CornerRadius = new CornerRadius(8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (on)
                row.Background = Theme.Brush("AccentGradientBrush");
            var name = new TextBlock
            {
                Text = ControlNames.Display(pick),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Theme.Brush(on ? "AccentInkBrush" : "TextBrush"),
            };
            var bound = new TextBlock
            {
                Text = Describe(layer.Get(pick)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Theme.Brush(on ? "AccentInkBrush" : "MutedBrush"),
            };
            Grid.SetColumn(bound, 1);
            row.Children.Add(name);
            row.Children.Add(bound);
            var button = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            button.Click += (_, _) =>
            {
                _control = pick;
                Diagram.Select(pick);
                RefreshButtonList();
                LoadEditor();
            };
            ButtonList.Children.Add(button);
            if (on)
            {
                button.Loaded += (_, _) => button.StartBringIntoView(new BringIntoViewOptions
                {
                    VerticalAlignmentRatio = 0.5,
                    AnimationDesired = false,
                });
            }
        }
    }

    private async Task<string?> PickPath(bool folder)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
        if (folder)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var picked = await picker.PickSingleFolderAsync();
            return picked?.Path;
        }

        var files = new Windows.Storage.Pickers.FileOpenPicker();
        files.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(files, hwnd);
        var file = await files.PickSingleFileAsync();
        return file?.Path;
    }

    private string OutputSignature() =>
        string.Join("\n", _runtime.Document.Outputs.Select(output => $"{output.Id}|{output.Kind}|{output.Slot}|{output.Name}"));

    private static ControlId[] ControlsIn(BindingSection section)
    {
        if (section == BindingSection.Triggers)
            return [ControlId.L2, ControlId.R2, ControlId.L2Full, ControlId.R2Full];
        if (section == BindingSection.Sticks)
            return [ControlId.L3, ControlId.R3];
        if (section == BindingSection.Touchpad)
            return [ControlId.TouchpadClick];
        if (section == BindingSection.Buttons)
            return ControlNames.Bindable.Where(id => !IsTrigger(id) && id is not (ControlId.L3 or ControlId.R3 or ControlId.TouchpadClick)).ToArray();
        return [];
    }

    private static bool IsTrigger(ControlId id) =>
        id is ControlId.L2 or ControlId.R2 or ControlId.L2Full or ControlId.R2Full;

    private void FillOutputs(ComboBox box, string selected)
    {
        box.Items.Clear();
        foreach (var output in _runtime.Document.Outputs)
            box.Items.Add(output.Name);
        box.SelectedIndex = _runtime.Document.Outputs.FindIndex(output => output.Id == selected);
    }

    private string OutputAt(ComboBox box, string current)
    {
        int index = box.SelectedIndex;
        if (index < 0 || index >= _runtime.Document.Outputs.Count)
            return current;
        return _runtime.Document.Outputs[index].Id;
    }

    private string Describe(Binding? binding)
    {
        var note = ConfigWarnings.ForBinding(_runtime.Document, _runtime.Editing, binding);
        if (!string.IsNullOrEmpty(note))
            return note;
        if (binding?.Advanced == true)
            return "Advanced";
        if (binding?.Kind == BindingKind.Profile)
        {
            string? name = _runtime.Document.Profiles.FirstOrDefault(profile => profile.Id == binding.ProfileId)?.Name;
            return string.IsNullOrEmpty(name) ? "Switch profile" : "Switch to " + name;
        }

        var choice = ButtonCatalog.Find(ButtonCatalog.Match(binding));
        return choice == null ? BindingText.Describe(binding) : choice.Label;
    }

    private string ControllerName()
    {
        string key = _runtime.ActiveInputKey;
        var match = DualSenseConnection.List().FirstOrDefault(item => item.Path == key);
        return string.IsNullOrEmpty(match.Path) ? "No controller" : match.Name;
    }

    private string UniqueName(string seed)
    {
        var names = _runtime.Document.Profiles.Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(seed))
            return seed;
        int n = 2;
        while (names.Contains(seed + " " + n))
            n++;
        return seed + " " + n;
    }

    private Button Tab(string text, bool on, Action click, bool baseLayer = false)
    {
        var name = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = Theme.Brush(on ? "AccentInkBrush" : "TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        object content = name;
        if (baseLayer)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(name);
            row.Children.Add(new TextBlock
            {
                Text = "Base",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 60,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush(on ? "AccentInkBrush" : "AccentTextBrush"),
            });
            content = row;
        }

        var button = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources["TabSegmentStyle"],
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 7, 14, 7),
        };
        if (on)
            button.Background = Theme.Brush("AccentGradientBrush");
        button.Click += (_, _) => click();
        return button;
    }

    private Button Pill(string text, bool on, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = Theme.Brush(on ? "AccentInkBrush" : "TextBrush"),
            },
            Style = (Style)Application.Current.Resources[on ? "PrimaryButtonStyle" : "QuietButtonStyle"],
            CornerRadius = new CornerRadius(17),
            Padding = new Thickness(14, 7, 14, 7),
        };
        button.Click += (_, _) => click();
        return button;
    }

    private static StackPanel StickBlock(string title, ComboBox mode, ComboBox output, Slider speed, Slider dead, Slider curve, TextBlock warning)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SectionText"] });
        panel.Children.Add(Field("Mode", mode));
        panel.Children.Add(Field("Output", output));
        panel.Children.Add(LabeledSlider("Speed", speed));
        panel.Children.Add(LabeledSlider("Deadzone", dead));
        panel.Children.Add(LabeledSlider("Curve", curve));
        panel.Children.Add(warning);
        return panel;
    }

    private static StackPanel Field(string label, FrameworkElement input)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Theme.Brush("MutedBrush") });
        panel.Children.Add(input);
        return panel;
    }

    private static StackPanel LabeledSlider(string label, Slider slider)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Theme.Brush("MutedBrush") });
        panel.Children.Add(slider);
        return panel;
    }

    private static StackPanel SliderField(TextBlock label, TextBlock value, Slider slider)
    {
        var header = new Grid();
        header.Children.Add(label);
        header.Children.Add(value);
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(header);
        panel.Children.Add(slider);
        return panel;
    }

    private static void Wire(ComboBox box, Action commit) => box.SelectionChanged += (_, _) => commit();

    private static void Wire(Slider slider, Action commit) => slider.ValueChanged += (_, _) => commit();

    private static TextBlock Value() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        FontSize = 12,
        Foreground = Theme.Brush("AccentTextBrush"),
    };

    private static TextBlock Warn() => new()
    {
        FontSize = 11,
        Foreground = Theme.Brush("AmberBrush"),
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };

    private static void ShowWarn(TextBlock block, StickSettings stick)
    {
        string? warning = ConfigWarnings.ForStick(App.Runtime.Document, stick);
        block.Text = warning ?? "";
        block.Visibility = string.IsNullOrEmpty(warning) ? Visibility.Collapsed : Visibility.Visible;
    }
}
