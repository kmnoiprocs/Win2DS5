using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Win2DS5.Core;

namespace Win2DS5.App.Controls;

public sealed class ActionExtras : StackPanel
{
    private readonly NumberBox _scroll = Number("Scroll amount", 1, 2000, 120);
    private readonly NumberBox _moveX = Number("X", -4000, 4000, 0);
    private readonly NumberBox _moveY = Number("Y", -4000, 4000, 0);
    private readonly StackPanel _move = new() { Spacing = 8 };
    private readonly ComboBox _layerMode = Box();
    private readonly ComboBox _layer = Box();
    private readonly ComboBox _profile = Box();
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, StepFrequency = 1 };
    private readonly NumberBox _volumeBox = Number("Volume", 0, 100, 50);
    private readonly StackPanel _volumeRow = new() { Spacing = 8 };
    private readonly TextBox _path = new() { PlaceholderText = "File or folder", CornerRadius = new CornerRadius(9) };
    private readonly StackPanel _open = new() { Spacing = 8 };
    private readonly StackPanel _pads = new() { Spacing = 4 };
    private readonly TextBlock _padNote = new() { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionText"] };
    private readonly List<(CheckBox Box, string Id)> _padBoxes = [];
    private readonly List<string> _chosenPads = [];
    private readonly List<string> _layerIds = [];
    private readonly List<string> _profileIds = [];
    private List<OutputDevice> _outputs = [];
    private bool _loading;
    private string _code = "";

    public ActionExtras()
    {
        Spacing = 8;
        Visibility = Visibility.Collapsed;
        _layerMode.Header = "Hold/Toggle";
        _layerMode.Items.Add("Hold Layer");
        _layerMode.Items.Add("Toggle Layer");
        _layer.Header = "Layer";
        _profile.Header = "Profile";
        _scroll.ValueChanged += (_, _) => Notify();
        _moveX.ValueChanged += (_, _) => Notify();
        _moveY.ValueChanged += (_, _) => Notify();
        _layerMode.SelectionChanged += (_, _) => Notify();
        _layer.SelectionChanged += (_, _) => Notify();
        _profile.SelectionChanged += (_, _) => Notify();
        _volume.ValueChanged += (_, e) =>
        {
            if (_loading)
                return;
            _loading = true;
            _volumeBox.Value = Math.Round(e.NewValue);
            _loading = false;
            Notify();
        };
        _volumeBox.ValueChanged += (_, e) =>
        {
            if (_loading)
                return;
            _loading = true;
            _volume.Value = e.NewValue;
            _loading = false;
            Notify();
        };
        _path.TextChanged += (_, _) => Notify();
        var file = new Button { Content = "Browse file", Style = (Style)Application.Current.Resources["QuietButtonStyle"] };
        var folder = new Button { Content = "Browse folder", Style = (Style)Application.Current.Resources["QuietButtonStyle"] };
        file.Click += async (_, _) =>
        {
            if (BrowseFile == null)
                return;
            var picked = await BrowseFile();
            if (!string.IsNullOrEmpty(picked))
                _path.Text = picked;
        };
        folder.Click += async (_, _) =>
        {
            if (BrowseFolder == null)
                return;
            var picked = await BrowseFolder();
            if (!string.IsNullOrEmpty(picked))
                _path.Text = picked;
        };
        var browse = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        browse.Children.Add(file);
        browse.Children.Add(folder);
        _open.Children.Add(new TextBlock { Text = "Open a file or folder", Style = (Style)Application.Current.Resources["CaptionText"] });
        _open.Children.Add(_path);
        _open.Children.Add(browse);
        _move.Children.Add(new TextBlock { Text = "Move the pointer from where it is now. Positive Y moves up", Style = (Style)Application.Current.Resources["CaptionText"], TextWrapping = TextWrapping.Wrap });
        _move.Children.Add(_moveX);
        _move.Children.Add(_moveY);
        _volumeRow.Children.Add(new TextBlock { Text = "Volume", Style = (Style)Application.Current.Resources["CaptionText"] });
        _volumeRow.Children.Add(_volume);
        _volumeRow.Children.Add(_volumeBox);
        Children.Add(_scroll);
        Children.Add(_move);
        Children.Add(_layerMode);
        Children.Add(_layer);
        Children.Add(_profile);
        Children.Add(_volumeRow);
        Children.Add(_open);
        Children.Add(_padNote);
        Children.Add(_pads);
    }

    public event Action? Edited;

    public string Code => _code;

    public IReadOnlyList<string> ChosenPads => _chosenPads;

    public Func<Task<string?>>? BrowseFile { get; set; }

    public Func<Task<string?>>? BrowseFolder { get; set; }

    public void SetContext(IReadOnlyList<Layer> layers, IReadOnlyList<Profile> profiles, IReadOnlyList<OutputDevice> outputs)
    {
        _loading = true;
        _outputs = outputs.ToList();
        _layerIds.Clear();
        _layer.Items.Clear();
        foreach (var layer in layers)
        {
            _layerIds.Add(layer.Id);
            _layer.Items.Add(layer.Name);
        }

        _profileIds.Clear();
        _profile.Items.Clear();
        foreach (var profile in profiles)
        {
            _profileIds.Add(profile.Id);
            _profile.Items.Add(profile.Name);
        }

        _loading = false;
    }

    public void Show(string code, Binding seed)
    {
        _code = code ?? "";
        _loading = true;
        _scroll.Value = Math.Max(Math.Abs(seed.ScrollX), Math.Abs(seed.ScrollY));
        if (_scroll.Value < 1)
            _scroll.Value = 120;
        _moveX.Value = seed.ScrollX;
        _moveY.Value = seed.ScrollY;
        _layerMode.SelectedIndex = seed.Kind == BindingKind.LayerToggle ? 1 : 0;
        _layer.SelectedIndex = IndexOf(_layerIds, seed.LayerId);
        _profile.SelectedIndex = IndexOf(_profileIds, seed.ProfileId);
        _volume.Value = Math.Clamp(seed.ScrollY, 0, 100);
        _volumeBox.Value = _volume.Value;
        _path.Text = seed.Path ?? "";
        bool scroll = _code is "mouse.up" or "mouse.down" or "mouse.scroll-left" or "mouse.scroll-right";
        bool move = _code == "mouse.move";
        bool layer = _code == "app.layer";
        bool profile = _code == "app.profile";
        bool volume = _code == "media.volume-custom";
        bool open = _code == "windows.open";
        _scroll.Visibility = scroll ? Visibility.Visible : Visibility.Collapsed;
        _move.Visibility = move ? Visibility.Visible : Visibility.Collapsed;
        _layerMode.Visibility = layer ? Visibility.Visible : Visibility.Collapsed;
        _layer.Visibility = layer ? Visibility.Visible : Visibility.Collapsed;
        _profile.Visibility = profile ? Visibility.Visible : Visibility.Collapsed;
        _volumeRow.Visibility = volume ? Visibility.Visible : Visibility.Collapsed;
        _open.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (ButtonCatalog.PadKind(_code) is { } kind)
            FillPads(kind, seed);
        else
        {
            _pads.Visibility = Visibility.Collapsed;
            _padNote.Visibility = Visibility.Collapsed;
        }

        _loading = false;
        Visibility = ButtonCatalog.Configurable(_code) ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowStep(MacroStep step)
    {
        var seed = ButtonCatalog.Create(step.Code);
        if (step.Code is "mouse.up" or "mouse.down" or "mouse.scroll-left" or "mouse.scroll-right" or "mouse.move" or "media.volume-custom")
        {
            seed.ScrollX = step.ScrollX;
            seed.ScrollY = step.ScrollY;
        }

        if (step.Code == "app.layer")
        {
            seed.Kind = step.LayerToggle ? BindingKind.LayerToggle : BindingKind.LayerHold;
            seed.LayerId = step.LayerId;
        }

        seed.ProfileId = step.ProfileId;
        seed.Path = step.Path;
        seed.Targets = step.Targets?.ToList() ?? [];
        Show(step.Code, seed);
    }

    public void Reshow()
    {
        if (string.IsNullOrEmpty(_code) || !ButtonCatalog.Configurable(_code))
            return;
        var seed = ButtonCatalog.Create(_code);
        ApplyTo(seed);
        Show(_code, seed);
    }

    public void ApplyTo(Binding binding)
    {
        ApplyNumbers(out int x, out int y);
        if (_code is "mouse.up" or "mouse.down" or "mouse.scroll-left" or "mouse.scroll-right" or "mouse.move" or "media.volume-custom")
        {
            binding.ScrollX = x;
            binding.ScrollY = y;
        }

        if (_code == "app.layer")
        {
            binding.Kind = _layerMode.SelectedIndex == 1 ? BindingKind.LayerToggle : BindingKind.LayerHold;
            binding.LayerId = Chosen(_layerIds, _layer.SelectedIndex);
            binding.Action = _code;
        }

        if (_code == "app.profile")
        {
            binding.ProfileId = Chosen(_profileIds, _profile.SelectedIndex);
            binding.Action = _code;
        }

        if (_code == "windows.open")
            binding.Path = _path.Text.Trim();
        if (ButtonCatalog.PadKind(_code) is { } kind)
        {
            binding.Targets = SelectedPads(kind);
            binding.OutputId = binding.Targets.FirstOrDefault() ?? "";
        }
    }

    public void ApplyTo(MacroStep step)
    {
        ApplyNumbers(out int x, out int y);
        step.ScrollX = x;
        step.ScrollY = y;
        step.LayerId = Chosen(_layerIds, _layer.SelectedIndex);
        step.LayerToggle = _layerMode.SelectedIndex == 1;
        step.ProfileId = Chosen(_profileIds, _profile.SelectedIndex);
        step.Path = _path.Text.Trim();
        if (ButtonCatalog.PadKind(_code) is { } kind)
            step.Targets = SelectedPads(kind).ToList();
    }

    private void FillPads(OutputDeviceKind kind, Binding seed)
    {
        _pads.Children.Clear();
        _padBoxes.Clear();
        var pads = _outputs.Where(item => item.Kind == kind).ToList();
        string noun = kind == OutputDeviceKind.Xbox ? "XBOX" : "DS4";
        _padNote.Text = pads.Count == 0
            ? $"Add {(kind == OutputDeviceKind.Xbox ? "an" : "a")} {noun} controller on the Devices page."
            : $"Choose which virtual {noun} gamepads receive this button.";
        var selected = new HashSet<string>(seed.Targets ?? []);
        if (!string.IsNullOrEmpty(seed.OutputId))
            selected.Add(seed.OutputId);
        foreach (var output in pads)
        {
            string id = output.Id;
            var box = new CheckBox { Content = output.Name, IsChecked = selected.Contains(id) };
            box.Checked += (_, _) => SetPad(id, true);
            box.Unchecked += (_, _) => SetPad(id, false);
            _padBoxes.Add((box, id));
            _pads.Children.Add(box);
        }

        if (_padBoxes.Count > 0 && _padBoxes.All(item => item.Box.IsChecked != true))
            _padBoxes[0].Box.IsChecked = true;
        _chosenPads.Clear();
        _chosenPads.AddRange(_padBoxes.Where(item => item.Box.IsChecked == true).Select(item => item.Id));
        _pads.Visibility = Visibility.Visible;
        _padNote.Visibility = Visibility.Visible;
    }

    private void SetPad(string id, bool on)
    {
        if (_loading)
            return;
        if (!on && _chosenPads.Count <= 1 && _chosenPads.Contains(id))
        {
            _loading = true;
            var box = _padBoxes.FirstOrDefault(item => item.Id == id).Box;
            if (box != null)
                box.IsChecked = true;
            _loading = false;
            return;
        }

        if (on)
        {
            if (!_chosenPads.Contains(id))
                _chosenPads.Add(id);
        }
        else
        {
            _chosenPads.Remove(id);
        }

        Notify();
    }

    private List<string> SelectedPads(OutputDeviceKind kind)
    {
        var live = _outputs.Where(item => item.Kind == kind).Select(item => item.Id).ToHashSet();
        return _chosenPads.Where(live.Contains).ToList();
    }

    private void ApplyNumbers(out int x, out int y)
    {
        x = 0;
        y = 0;
        if (_code is "mouse.up" or "mouse.down" or "mouse.scroll-left" or "mouse.scroll-right")
        {
            int amount = Math.Max(1, (int)_scroll.Value);
            x = _code == "mouse.scroll-right" ? amount : _code == "mouse.scroll-left" ? -amount : 0;
            y = _code == "mouse.up" ? amount : _code == "mouse.down" ? -amount : 0;
        }
        else if (_code == "mouse.move")
        {
            x = (int)_moveX.Value;
            y = (int)_moveY.Value;
        }
        else if (_code == "media.volume-custom")
        {
            y = (int)Math.Clamp(_volumeBox.Value, 0, 100);
        }
    }

    private void Notify()
    {
        if (!_loading)
            Edited?.Invoke();
    }

    private static int IndexOf(List<string> ids, string? id)
    {
        if (ids.Count == 0)
            return -1;
        int index = ids.IndexOf(id ?? "");
        return index >= 0 ? index : 0;
    }

    private static string Chosen(List<string> ids, int index)
    {
        if (ids.Count == 0)
            return "";
        return ids[Math.Clamp(index, 0, ids.Count - 1)];
    }

    private static ComboBox Box() => new QuietComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };

    private static NumberBox Number(string header, double min, double max, double value) => new()
    {
        Header = header,
        Minimum = min,
        Maximum = max,
        Value = value,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        CornerRadius = new CornerRadius(9),
    };
}
