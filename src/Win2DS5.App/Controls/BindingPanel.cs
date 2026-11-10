using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Win2DS5.App.Theming;
using Win2DS5.Core;
using Windows.System;

namespace Win2DS5.App.Controls;

public sealed class BindingPanel : UserControl
{
    private readonly TextBlock _eyebrow = new() { Text = "BINDING", Style = (Style)Application.Current.Resources["EyebrowText"] };
    private readonly TextBlock _title = new() { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontFamily = (FontFamily)Application.Current.Resources["DisplayFont"] };
    private readonly TextBlock _hint = new() { Style = (Style)Application.Current.Resources["CaptionText"], TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Theme.Brush("AccentTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _shortLabel = Label("Action");
    private readonly TextBlock _longLabel = Label("Long press");
    private readonly ToggleSwitch _advanced = new() { OnContent = "", OffContent = "", MinWidth = 0 };
    private readonly TextBlock _advancedNote = new() { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionText"] };
    private readonly CheckBox _longPress = new() { Content = "Enable long-press command." };
    private readonly CheckBox _overrideDelay = new() { Content = "Override global delay setting." };
    private readonly Slider _delay = new() { Minimum = 100, Maximum = 1000, StepFrequency = 10 };
    private readonly TextBlock _delayValue = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _delayHeader = new();
    private readonly ActionEditor _short;
    private readonly ActionEditor _long;
    private readonly StackPanel _quick;
    private readonly EdgeListEditor _pressSteps;
    private readonly EdgeListEditor _releaseSteps;
    private Action<KeyStroke>? _applyStepKey;
    private bool _loading;
    private bool _allowLayers = true;
    private bool _allowLongPress = true;
    private bool _allowBase = true;
    private int _holdMs = 350;
    private readonly TextBlock _outputLabel = new() { Text = "Send this button to", Style = (Style)Application.Current.Resources["CaptionText"] };
    private readonly VariableSizedWrapGrid _outputChips = new()
    {
        Orientation = Orientation.Horizontal,
        ItemHeight = 34,
        ItemWidth = 118,
    };
    private readonly List<OutputDevice> _outputs = [];
    private string _outputId = "";
    private ActionEditor? _recording;

    public BindingPanel()
    {
        _short = new ActionEditor(includeQuick: false);
        _long = new ActionEditor(includeQuick: false);
        _quick = QuickRow();
        _short.Changed += Commit;
        _long.Changed += Commit;
        _pressSteps = new EdgeListEditor("On press");
        _releaseSteps = new EdgeListEditor("On release");
        _short.RecordRequested += () => BeginRecord(_short);
        _long.RecordRequested += () => BeginRecord(_long);
        _pressSteps.Changed += Commit;
        _releaseSteps.Changed += Commit;
        _pressSteps.RecordRequested += apply => BeginStepRecord(apply);
        _releaseSteps.RecordRequested += apply => BeginStepRecord(apply);
        _advanced.Toggled += (_, _) =>
        {
            if (_loading)
                return;
            RefreshAdvanced();
            Commit();
        };
        _longPress.Checked += (_, _) => OnLongToggled();
        _longPress.Unchecked += (_, _) => OnLongToggled();
        _overrideDelay.Checked += (_, _) => OnOverrideToggled();
        _overrideDelay.Unchecked += (_, _) => OnOverrideToggled();
        _delay.ValueChanged += (_, e) =>
        {
            _holdMs = (int)Math.Round(e.NewValue);
            _delayValue.Text = $"{_holdMs} ms";
            if (!_loading)
                Commit();
        };

        var root = new StackPanel { Spacing = 12, Padding = new Thickness(18) };
        root.Children.Add(_eyebrow);
        root.Children.Add(_title);
        root.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["ChipStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _summary,
        });
        root.Children.Add(_hint);
        root.Children.Add(_outputLabel);
        root.Children.Add(_outputChips);
        root.Children.Add(new Rectangle { Height = 1, Fill = Theme.Brush("LineBrush"), Margin = new Thickness(0, 2, 0, 2) });
        root.Children.Add(_shortLabel);
        root.Children.Add(_short.Root);
        root.Children.Add(_quick);
        var advancedRow = new Grid();
        advancedRow.Children.Add(new TextBlock { Text = "Use advanced settings.", VerticalAlignment = VerticalAlignment.Center });
        _advanced.HorizontalAlignment = HorizontalAlignment.Right;
        advancedRow.Children.Add(_advanced);
        root.Children.Add(advancedRow);
        root.Children.Add(_advancedNote);
        _delayHeader.Children.Add(new TextBlock { Text = "This button's tap window", VerticalAlignment = VerticalAlignment.Center });
        _delayHeader.Children.Add(_delayValue);
        root.Children.Add(_longPress);
        root.Children.Add(_overrideDelay);
        root.Children.Add(_delayHeader);
        root.Children.Add(_delay);
        root.Children.Add(_longLabel);
        root.Children.Add(_long.Root);
        root.Children.Add(_pressSteps.Root);
        root.Children.Add(_releaseSteps.Root);
        Content = new ScrollViewer { Content = root };
        ConfigureEditors();
        RefreshAdvanced();
    }

    public event Action<Binding?>? BindingChanged;

    public void ShowChrome(bool show)
    {
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _eyebrow.Visibility = visibility;
        _title.Visibility = visibility;
    }

    public void Attach(UIElement root) => root.KeyDown += OnKeyDown;

    public void Show(string title, string hint, Binding? binding, IReadOnlyList<Layer> layers, bool allowLayers, bool allowLongPress, bool allowBase, IReadOnlyList<OutputDevice>? outputs = null)
    {
        _loading = true;
        _allowLayers = allowLayers;
        _allowLongPress = allowLongPress;
        _allowBase = allowBase;
        _title.Text = title;
        _hint.Text = hint;
        _outputs.Clear();
        if (outputs != null)
            _outputs.AddRange(outputs);
        _outputId = OutputCatalog.Infer(binding, _outputs);
        BuildOutputChips();
        _holdMs = binding?.OverrideDelay == true ? Math.Clamp(binding.TapMs, 100, 1000) : GlobalDelay();
        _overrideDelay.IsChecked = binding?.OverrideDelay == true;
        _delay.Value = _holdMs;
        _delayValue.Text = $"{_holdMs} ms";
        CancelRecording();
        _short.SetLayers(layers);
        _long.SetLayers(layers);
        var profiles = App.Runtime.Document.Profiles;
        _short.SetProfiles(profiles);
        _long.SetProfiles(profiles);
        bool split = allowLongPress && binding?.Tap != null;
        _advanced.IsOn = allowLongPress && (binding?.Advanced == true || split || (binding?.PressSteps.Count ?? 0) > 0 || (binding?.ReleaseSteps.Count ?? 0) > 0);
        _longPress.IsChecked = split;
        _pressSteps.Load(binding?.PressSteps);
        _releaseSteps.Load(binding?.ReleaseSteps);
        ConfigureEditors();
        if (split)
        {
            _short.Load(binding?.Tap);
            _long.Load(binding);
        }
        else
        {
            _short.Load(binding);
            _long.Load(null);
        }

        _loading = false;
        RefreshAdvanced();
        _summary.Text = Summary(binding);
    }

    private void BuildOutputChips()
    {
        _outputChips.Children.Clear();
        _outputLabel.Visibility = _outputs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _outputChips.Visibility = _outputLabel.Visibility;
        foreach (var output in _outputs)
        {
            bool on = output.Id == _outputId;
            var button = new Button
            {
                Content = output.Name,
                Tag = output.Id,
                Style = (Style)Application.Current.Resources[on ? "PrimaryButtonStyle" : "QuietButtonStyle"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 6, 6),
            };
            button.Click += (_, _) =>
            {
                if (_loading)
                    return;
                _outputId = (string)button.Tag;
                BuildOutputChips();
                ConfigureEditors();
                if (!KindFits(_short.Kind))
                    _short.Select(DefaultKind());
                Commit();
            };
            _outputChips.Children.Add(button);
        }
    }

    private bool KindFits(BindingKind kind)
    {
        if (kind == BindingKind.Profile)
            return true;
        var output = OutputCatalog.Find(_outputs, _outputId);
        if (output == null)
            return true;
        return output.Kind switch
        {
            OutputDeviceKind.Mouse => kind is BindingKind.None or BindingKind.Mouse or BindingKind.Scroll or BindingKind.LayerHold or BindingKind.LayerToggle or BindingKind.UseBase,
            OutputDeviceKind.Keyboard => kind is BindingKind.None or BindingKind.Key or BindingKind.ToggleKeyboard or BindingKind.LayerHold or BindingKind.LayerToggle or BindingKind.UseBase,
            _ => kind is BindingKind.None or BindingKind.Pad or BindingKind.LayerHold or BindingKind.LayerToggle or BindingKind.UseBase,
        };
    }

    private BindingKind DefaultKind()
    {
        var output = OutputCatalog.Find(_outputs, _outputId);
        return output?.Kind switch
        {
            OutputDeviceKind.Mouse => BindingKind.Mouse,
            OutputDeviceKind.Keyboard => BindingKind.Key,
            OutputDeviceKind.DualShock4 or OutputDeviceKind.Xbox => BindingKind.Pad,
            _ => BindingKind.None,
        };
    }

    private string Summary(Binding? binding)
    {
        var note = ConfigWarnings.ForBinding(App.Runtime.Document, App.Runtime.Editing, binding);
        if (!string.IsNullOrEmpty(note))
        {
            _summary.Foreground = Theme.Brush("AmberBrush");
            return note;
        }

        _summary.Foreground = Theme.Brush("AccentTextBrush");
        var text = BindingText.Describe(binding);
        var output = OutputCatalog.Find(_outputs, binding?.OutputId);
        return output == null ? text : output.Name + "  ·  " + text;
    }

    private void OnLongToggled()
    {
        if (_loading)
            return;
        ConfigureEditors();
        if (_longPress.IsChecked == true && _long.Kind == BindingKind.None)
            _long.Select(_allowLayers ? BindingKind.LayerHold : BindingKind.Mouse);
        RefreshAdvanced();
        Commit();
    }

    private void ConfigureEditors()
    {
        bool split = _longPress.IsChecked == true;
        var output = OutputCatalog.Find(_outputs, _outputId);
        _short.Configure(_allowLayers, _allowBase && !split, output?.Kind);
        _long.Configure(_allowLayers, useBase: false, output?.Kind);
    }

    private void OnOverrideToggled()
    {
        if (_loading)
            return;
        if (_overrideDelay.IsChecked == true)
        {
            _holdMs = GlobalDelay();
            _loading = true;
            _delay.Value = _holdMs;
            _delayValue.Text = $"{_holdMs} ms";
            _loading = false;
        }

        RefreshAdvanced();
        Commit();
    }

    private void RefreshAdvanced()
    {
        bool advanced = _allowLongPress && _advanced.IsOn;
        bool on = advanced && _longPress.IsChecked == true;
        bool custom = on && _overrideDelay.IsChecked == true;
        _advanced.Visibility = _allowLongPress ? Visibility.Visible : Visibility.Collapsed;
        if (_advanced.Parent is Grid advancedRow)
            advancedRow.Visibility = _advanced.Visibility;
        _advancedNote.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        _advancedNote.Text = on
            ? "Long press waits until you release the button, then runs the tap or the long-press command. Press and release steps stay off so they do not also fire."
            : "Steps run with the action above. Press steps run on the way down. Release steps run on the way up, before that action is let go.";
        _longPress.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        _overrideDelay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _delayHeader.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _delay.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _longLabel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _long.Root.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _pressSteps.Root.Visibility = advanced && !on ? Visibility.Visible : Visibility.Collapsed;
        _releaseSteps.Root.Visibility = advanced && !on ? Visibility.Visible : Visibility.Collapsed;
        _shortLabel.Text = on ? "Short tap" : "Action";
        _quick.Visibility = _short.Kind == BindingKind.Key ? Visibility.Visible : Visibility.Collapsed;
    }

    private static int GlobalDelay()
    {
        try
        {
            return Math.Clamp(App.Runtime.Document.Settings.LongPressMs, 100, 1000);
        }
        catch
        {
            return 350;
        }
    }

    private void BeginStepRecord(Action<KeyStroke> apply)
    {
        CancelRecording();
        _applyStepKey = apply;
    }

    private void BeginRecord(ActionEditor editor)
    {
        CancelRecording();
        _recording = editor;
        editor.BeginRecord();
    }

    private void CancelRecording()
    {
        _applyStepKey = null;
        _recording?.CancelRecording();
        _recording = null;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_applyStepKey != null)
        {
            e.Handled = true;
            if (e.Key == VirtualKey.Escape)
            {
                _applyStepKey = null;
                return;
            }

            if (IsModifier(e.Key))
                return;
            var stepKey = FromKey(e.Key);
            if (stepKey == null)
                return;
            var apply = _applyStepKey;
            _applyStepKey = null;
            apply(stepKey);
            return;
        }

        if (_recording == null)
            return;
        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            CancelRecording();
            return;
        }

        if (IsModifier(e.Key))
            return;
        var stroke = FromKey(e.Key);
        if (stroke == null)
            return;
        var keys = new List<KeyStroke>();
        AddIfDown(keys, VirtualKey.Shift, KeyCatalog.LShift);
        AddIfDown(keys, VirtualKey.Control, KeyCatalog.LCtrl);
        AddIfDown(keys, VirtualKey.Menu, KeyCatalog.LAlt);
        AddIfDown(keys, VirtualKey.LeftWindows, KeyCatalog.LWin);
        keys.Add(stroke);
        var editor = _recording;
        CancelRecording();
        editor?.ApplyKeys(keys);
    }

    private static void AddIfDown(List<KeyStroke> keys, VirtualKey key, KeyStroke stroke)
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
        if (state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            keys.Add(Copy(stroke));
    }

    private void ApplyQuick(Binding binding)
    {
        _short.Load(binding);
        RefreshAdvanced();
        Commit();
    }

    private void Commit()
    {
        if (_loading)
            return;
        RefreshAdvanced();
        var binding = Build();
        _summary.Text = Summary(binding);
        BindingChanged?.Invoke(binding);
    }

    private Binding? Build()
    {
        var primary = _short.Build() ?? new Binding { Kind = BindingKind.None };
        primary.OutputId = _outputId;
        OutputCatalog.Remember(primary, _outputs);
        if (!_allowLongPress || !_advanced.IsOn)
            return primary;

        primary.Advanced = true;
        primary.PressSteps = _pressSteps.Snapshot();
        primary.ReleaseSteps = _releaseSteps.Snapshot();
        if (_longPress.IsChecked != true)
        {
            primary.OutputId = _outputId;
            OutputCatalog.Remember(primary, _outputs);
            return primary;
        }

        var held = _long.Build() ?? new Binding { Kind = BindingKind.None };
        held.OutputId = _outputId;
        held.Tap = primary.Kind == BindingKind.None && primary.Keys.Count == 0 ? new Binding { Kind = BindingKind.None, OutputId = _outputId } : WithoutSteps(primary);
        held.Tap.OutputId = _outputId;
        OutputCatalog.Remember(held, _outputs);
        held.Advanced = true;
        held.PressSteps = _pressSteps.Snapshot();
        held.ReleaseSteps = _releaseSteps.Snapshot();
        held.OverrideDelay = _overrideDelay.IsChecked == true;
        held.TapMs = held.OverrideDelay ? Math.Clamp(_holdMs, 100, 1000) : GlobalDelay();
        return held;
    }

    private static Binding WithoutSteps(Binding binding)
    {
        binding.Advanced = false;
        binding.PressSteps = [];
        binding.ReleaseSteps = [];
        return binding;
    }

    private StackPanel QuickRow()
    {
        (string Label, Binding Binding)[] actions =
        [
            ("Left click", Binding.MouseClick(MouseButton.Left)),
            ("Right click", Binding.MouseClick(MouseButton.Right)),
            ("Middle click", Binding.MouseClick(MouseButton.Middle)),
            ("Mouse back", Binding.MouseClick(MouseButton.X1)),
            ("Mouse forward", Binding.MouseClick(MouseButton.X2)),
            ("Scroll up", Binding.Scroll(0, 120)),
            ("Scroll down", Binding.Scroll(0, -120)),
            ("Enter", Binding.Key(KeyCatalog.Enter)),
            ("Esc", Binding.Key(KeyCatalog.Esc)),
            ("Tab", Binding.Key(KeyCatalog.Tab)),
            ("Delete", Binding.Key(KeyCatalog.Delete)),
            ("Backspace", Binding.Key(KeyCatalog.Backspace)),
            ("Space", Binding.Key(KeyCatalog.Space)),
            ("Win", Binding.Key(KeyCatalog.LWin)),
            ("Ctrl", Binding.Key(KeyCatalog.LCtrl)),
            ("Right Ctrl", Binding.Key(KeyCatalog.RCtrl)),
            ("Shift", Binding.Key(KeyCatalog.LShift)),
            ("Right Shift", Binding.Key(KeyCatalog.RShift)),
            ("Alt", Binding.Key(KeyCatalog.LAlt)),
            ("Right Alt", Binding.Key(KeyCatalog.RAlt)),
            ("Up", Binding.Key(KeyCatalog.Up)),
            ("Down", Binding.Key(KeyCatalog.Down)),
            ("Left", Binding.Key(KeyCatalog.Left)),
            ("Right", Binding.Key(KeyCatalog.Right)),
        ];
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (int c = 0; c < 3; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < actions.Length; i++)
        {
            if (i % 3 == 0)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var binding = actions[i].Binding;
            var button = new Button
            {
                Content = actions[i].Label,
                Style = (Style)Application.Current.Resources["QuietButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(6),
                FontSize = 12,
                CornerRadius = new CornerRadius(8),
            };
            button.Click += (_, _) => ApplyQuick(binding);
            Grid.SetRow(button, i / 3);
            Grid.SetColumn(button, i % 3);
            grid.Children.Add(button);
        }

        return new StackPanel { Spacing = 6, Children = { Label("Quick actions"), grid } };
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, Foreground = Theme.Brush("MutedBrush") };

    private static KeyStroke Copy(KeyStroke key) => new() { Name = key.Name, ScanCode = key.ScanCode, Extended = key.Extended };

    private static bool IsModifier(VirtualKey key) => key is VirtualKey.Shift or VirtualKey.Control or VirtualKey.Menu
        or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftControl or VirtualKey.RightControl
        or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static KeyStroke? FromKey(VirtualKey key)
    {
        uint scan = MapVirtualKey((uint)key, 4);
        bool extended = (scan & 0xFF00) == 0xE000 || (scan & 0xFF00) == 0xE100;
        ushort code = (ushort)(scan & 0xFF);
        var match = KeyCatalog.Match(code, extended) ?? KeyCatalog.Match(code, false);
        if (match != null)
            return Copy(match);
        if (code == 0)
            return null;
        return new KeyStroke { Name = key.ToString(), ScanCode = code, Extended = extended };
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    private sealed class ActionEditor
    {
        private readonly List<BindingKind> _options = [];
        private readonly List<ControlId> _pads = [];
        private readonly ComboBox _kind = Combo();
        private readonly ComboBox _mouse = Combo();
        private readonly ComboBox _scroll = Combo();
        private readonly ComboBox _layer = Combo();
        private readonly ComboBox _pad = Combo();
        private readonly ComboBox _profile = Combo();
        private readonly List<string> _profileIds = [];
        private readonly TextBlock _keys = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Theme.Brush("AccentTextBrush") };
        private readonly Border _keysBox = new() { Style = (Style)Application.Current.Resources["InsetStyle"], MinHeight = 46 };
        private readonly Button _record = new() { Content = "Record keys", Style = (Style)Application.Current.Resources["PrimaryButtonStyle"] };
        private readonly StackPanel _keySection = new() { Spacing = 8 };
        private readonly StackPanel _mouseSection = new() { Spacing = 8 };
        private readonly StackPanel _scrollSection = new() { Spacing = 8 };
        private readonly StackPanel _layerSection = new() { Spacing = 8 };
        private readonly StackPanel _padSection = new() { Spacing = 8 };
        private readonly StackPanel _profileSection = new() { Spacing = 8 };
        private readonly List<KeyStroke> _recorded = [];
        private IReadOnlyList<Layer> _layers = [];
        private bool _loading;

        public ActionEditor(bool includeQuick)
        {
            _ = includeQuick;
            Root = new StackPanel { Spacing = 8 };
            Root.Children.Add(_kind);
            _keysBox.Child = _keys;
            _keysBox.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(160) };
            _keySection.Children.Add(Label("Keys"));
            _keySection.Children.Add(_keysBox);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _record.Click += (_, _) => RecordRequested?.Invoke();
            var clear = new Button { Content = "Clear", Style = (Style)Application.Current.Resources["QuietButtonStyle"] };
            clear.Click += (_, _) =>
            {
                _recorded.Clear();
                _keys.Text = "Unbound";
                Changed?.Invoke();
            };
            row.Children.Add(_record);
            row.Children.Add(clear);
            _keySection.Children.Add(row);
            _mouseSection.Children.Add(Label("Mouse button"));
            _mouseSection.Children.Add(_mouse);
            _scrollSection.Children.Add(Label("Scroll"));
            _scrollSection.Children.Add(_scroll);
            _layerSection.Children.Add(Label("Layer"));
            _layerSection.Children.Add(_layer);
            _padSection.Children.Add(Label("Controller button"));
            _padSection.Children.Add(_pad);
            _profileSection.Children.Add(Label("Profile"));
            _profileSection.Children.Add(_profile);
            _profileSection.Children.Add(new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionText"],
                TextWrapping = TextWrapping.Wrap,
                Text = "Pressing this button switches the profile on the controller you pressed. Other controllers keep the profile they are using.",
            });
            _padSection.Children.Add(new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionText"],
                TextWrapping = TextWrapping.Wrap,
                Text = "Desktop mode keeps the real DualSense hidden and sends this through a virtual XBOX controller. Cross, Circle, Square, and Triangle arrive as A, B, X, and Y. Share is Back, Options is Start, and PS, the touchpad click, and Mute arrive as Guide.",
            });
            Root.Children.Add(_keySection);
            Root.Children.Add(_mouseSection);
            Root.Children.Add(_scrollSection);
            Root.Children.Add(_layerSection);
            Root.Children.Add(_padSection);
            Root.Children.Add(_profileSection);
            Fill(_mouse, "Left click", "Right click", "Middle click", "Mouse back", "Mouse forward");
            Fill(_scroll, "Scroll up", "Scroll down", "Scroll left", "Scroll right");
            foreach (var button in ControlNames.OutputButtons)
            {
                _pads.Add(button);
                _pad.Items.Add(ControlNames.PadLabel(button));
            }

            _pad.SelectedIndex = 0;
            _kind.SelectionChanged += (_, _) => { if (!_loading) { ShowSections(); Changed?.Invoke(); } };
            _mouse.SelectionChanged += (_, _) => { if (!_loading) Changed?.Invoke(); };
            _scroll.SelectionChanged += (_, _) => { if (!_loading) Changed?.Invoke(); };
            _layer.SelectionChanged += (_, _) => { if (!_loading) Changed?.Invoke(); };
            _pad.SelectionChanged += (_, _) => { if (!_loading) Changed?.Invoke(); };
            _profile.SelectionChanged += (_, _) => { if (!_loading) Changed?.Invoke(); };
            Configure(true, true);
        }

        public StackPanel Root { get; }
        public event Action? Changed;
        public event Action? RecordRequested;
        public BindingKind Kind => _options.Count == 0 ? BindingKind.None : _options[Math.Clamp(_kind.SelectedIndex, 0, _options.Count - 1)];

        public void SetLayers(IReadOnlyList<Layer> layers)
        {
            _layers = layers;
            var selected = _layer.SelectedItem as string;
            _layer.Items.Clear();
            string home = layers.FirstOrDefault()?.Id ?? "base";
            foreach (var layer in layers.Where(layer => layer.Id != "base" && layer.Id != home))
                _layer.Items.Add(layer.Name);
            if (_layer.Items.Count == 0)
                _layer.Items.Add("Shift");
            _layer.SelectedItem = selected;
            if (_layer.SelectedIndex < 0)
                _layer.SelectedIndex = 0;
        }

        public void SetProfiles(IReadOnlyList<Profile> profiles)
        {
            string selected = SelectedProfileId();
            _profile.Items.Clear();
            _profileIds.Clear();
            foreach (var profile in profiles)
            {
                _profileIds.Add(profile.Id);
                _profile.Items.Add(profile.Name);
            }

            if (_profileIds.Count == 0)
            {
                _profileIds.Add("");
                _profile.Items.Add("No profiles");
            }

            int index = _profileIds.IndexOf(selected);
            _profile.SelectedIndex = index >= 0 ? index : 0;
        }

        private string SelectedProfileId() =>
            _profile.SelectedIndex >= 0 && _profile.SelectedIndex < _profileIds.Count
                ? _profileIds[_profile.SelectedIndex]
                : "";

        public void Configure(bool layers, bool useBase, OutputDeviceKind? output = null)
        {
            var current = _options.Count == 0 ? BindingKind.None : Kind;
            _loading = true;
            _options.Clear();
            _kind.Items.Clear();
            bool key = output is null or OutputDeviceKind.Keyboard;
            bool mouse = output is null or OutputDeviceKind.Mouse;
            bool pad = output is null or OutputDeviceKind.DualShock4 or OutputDeviceKind.Xbox;
            Add(BindingKind.None, "None");
            if (key)
                Add(BindingKind.Key, "Key");
            if (mouse)
            {
                Add(BindingKind.Mouse, "Mouse button");
                Add(BindingKind.Scroll, "Scroll");
            }

            if (layers)
            {
                Add(BindingKind.LayerHold, "Hold layer");
                Add(BindingKind.LayerToggle, "Toggle layer");
                if (key)
                    Add(BindingKind.ToggleKeyboard, "On-screen keyboard");
            }

            if (useBase)
                Add(BindingKind.UseBase, "Use base binding");
            if (pad)
                Add(BindingKind.Pad, output == OutputDeviceKind.DualShock4 ? "DS4 button" : "Controller button");
            Add(BindingKind.Profile, "Profiles");
            Select(current);
            _loading = false;
            ShowSections();
        }

        public void Select(BindingKind kind)
        {
            int index = _options.IndexOf(kind);
            _kind.SelectedIndex = index >= 0 ? index : 0;
            ShowSections();
        }

        public void Load(Binding? binding)
        {
            _loading = true;
            CancelRecording();
            _recorded.Clear();
            if (binding?.Keys != null)
                _recorded.AddRange(binding.Keys.Select(Copy));
            _keys.Text = _recorded.Count == 0 ? "Unbound" : string.Join(" + ", _recorded.Select(key => key.Name));
            Select(binding?.Kind ?? BindingKind.None);
            _mouse.SelectedIndex = binding == null ? 0 : Math.Clamp((int)binding.Mouse, 0, _mouse.Items.Count - 1);
            _scroll.SelectedIndex = ScrollIndex(binding);
            var layerName = _layers.FirstOrDefault(layer => layer.Id == binding?.LayerId)?.Name;
            if (!string.IsNullOrEmpty(layerName))
                _layer.SelectedItem = layerName;
            if (_layer.SelectedIndex < 0 && _layer.Items.Count > 0)
                _layer.SelectedIndex = 0;
            int pad = _pads.IndexOf(binding?.PadButton ?? ControlId.Cross);
            _pad.SelectedIndex = pad >= 0 ? pad : 0;
            int profileIndex = _profileIds.IndexOf(binding?.ProfileId ?? "");
            if (profileIndex >= 0)
                _profile.SelectedIndex = profileIndex;
            _loading = false;
            ShowSections();
        }

        public Binding? Build()
        {
            return Kind switch
            {
                BindingKind.Key => _recorded.Count == 0 ? null : Binding.Key(_recorded.Select(Copy).ToArray()),
                BindingKind.Mouse => Binding.MouseClick((MouseButton)_mouse.SelectedIndex),
                BindingKind.Scroll => _scroll.SelectedIndex switch
                {
                    0 => Binding.Scroll(0, 120),
                    1 => Binding.Scroll(0, -120),
                    2 => Binding.Scroll(-120, 0),
                    _ => Binding.Scroll(120, 0),
                },
                BindingKind.LayerHold => Binding.Hold(SelectedLayerId()),
                BindingKind.LayerToggle => Binding.Toggle(SelectedLayerId()),
                BindingKind.ToggleKeyboard => Binding.Keyboard(),
                BindingKind.UseBase => Binding.Base(),
                BindingKind.Pad => Binding.Pad(_pads[Math.Clamp(_pad.SelectedIndex, 0, _pads.Count - 1)]),
                BindingKind.Profile => Binding.SwitchProfile(SelectedProfileId()),
                _ => null,
            };
        }

        public void BeginRecord()
        {
            _recorded.Clear();
            _keys.Text = "Waiting…";
            _record.Content = "Press a key…";
            _keysBox.BorderBrush = Theme.Brush("AccentBrush");
            _keysBox.Background = Theme.Brush("AccentSoftBrush");
        }

        public void CancelRecording()
        {
            _record.Content = "Record keys";
            _keysBox.BorderBrush = Theme.Brush("LineBrush");
            _keysBox.Background = Theme.Brush("PanelAltBrush");
            _keys.Text = _recorded.Count == 0 ? "Unbound" : string.Join(" + ", _recorded.Select(key => key.Name));
        }

        public void ApplyKeys(IReadOnlyList<KeyStroke> keys)
        {
            _loading = true;
            _recorded.Clear();
            _recorded.AddRange(keys.Select(Copy));
            _keys.Text = string.Join(" + ", _recorded.Select(key => key.Name));
            Select(BindingKind.Key);
            _loading = false;
            ShowSections();
            Changed?.Invoke();
        }

        private void Add(BindingKind kind, string label)
        {
            _options.Add(kind);
            _kind.Items.Add(label);
        }

        private void ShowSections()
        {
            var kind = Kind;
            _keySection.Visibility = kind == BindingKind.Key ? Visibility.Visible : Visibility.Collapsed;
            _mouseSection.Visibility = kind == BindingKind.Mouse ? Visibility.Visible : Visibility.Collapsed;
            _scrollSection.Visibility = kind == BindingKind.Scroll ? Visibility.Visible : Visibility.Collapsed;
            _layerSection.Visibility = kind is BindingKind.LayerHold or BindingKind.LayerToggle ? Visibility.Visible : Visibility.Collapsed;
            _padSection.Visibility = kind == BindingKind.Pad ? Visibility.Visible : Visibility.Collapsed;
            _profileSection.Visibility = kind == BindingKind.Profile ? Visibility.Visible : Visibility.Collapsed;
        }

        private string SelectedLayerId()
        {
            var name = _layer.SelectedItem as string;
            return _layers.FirstOrDefault(layer => layer.Name == name)?.Id ?? "shift";
        }

        private static int ScrollIndex(Binding? binding)
        {
            if (binding == null || binding.Kind != BindingKind.Scroll)
                return 1;
            if (binding.ScrollY > 0) return 0;
            if (binding.ScrollY < 0) return 1;
            if (binding.ScrollX < 0) return 2;
            return 3;
        }

        private static void Fill(ComboBox box, params string[] items)
        {
            foreach (var item in items)
                box.Items.Add(item);
            box.SelectedIndex = 0;
        }

        private static ComboBox Combo() => new QuietComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };
    }

    private sealed class EdgeListEditor
    {
        private readonly StackPanel _rows = new() { Spacing = 6 };
        private readonly List<EdgeRow> _items = [];

        public EdgeListEditor(string title)
        {
            var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            keys.Children.Add(Small("Add key down", () => Add(EdgeKind.KeyDown)));
            keys.Children.Add(Small("Add key up", () => Add(EdgeKind.KeyUp)));
            var mouse = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            mouse.Children.Add(Small("Add mouse down", () => Add(EdgeKind.MouseDown)));
            mouse.Children.Add(Small("Add mouse up", () => Add(EdgeKind.MouseUp)));
            Root = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Label(title),
                    _rows,
                    keys,
                    mouse,
                },
            };
        }

        public StackPanel Root { get; }
        public event Action? Changed;
        public event Action<Action<KeyStroke>>? RecordRequested;

        public void Load(IReadOnlyList<EdgeStep>? steps)
        {
            _rows.Children.Clear();
            _items.Clear();
            if (steps == null)
                return;
            foreach (var step in steps)
                Add(step.Kind, step.Mouse, step.Key, notify: false);
        }

        public List<EdgeStep> Snapshot() => _items.Select(row => row.Snapshot()).ToList();

        private void Add(EdgeKind kind) => Add(kind, MouseButton.Left, Clone(KeyCatalog.LShift), notify: true);

        private void Add(EdgeKind kind, MouseButton mouse, KeyStroke? key, bool notify)
        {
            var row = new EdgeRow(kind, mouse, key ?? Clone(KeyCatalog.LShift));
            row.Changed += () => Changed?.Invoke();
            row.RemoveRequested += () =>
            {
                _items.Remove(row);
                _rows.Children.Remove(row.Root);
                Changed?.Invoke();
            };
            row.RecordRequested += apply => RecordRequested?.Invoke(apply);
            _items.Add(row);
            _rows.Children.Add(row.Root);
            if (notify)
                Changed?.Invoke();
        }

        private static Button Small(string text, Action click)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)Application.Current.Resources["QuietButtonStyle"],
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 12,
            };
            button.Click += (_, _) => click();
            return button;
        }

        private static KeyStroke Clone(KeyStroke key) => new() { Name = key.Name, ScanCode = key.ScanCode, Extended = key.Extended };

        private sealed class EdgeRow
        {
            private readonly ComboBox _kind = new QuietComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };
            private readonly ComboBox _mouse = new QuietComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };
            private readonly Button _key = new() { Style = (Style)Application.Current.Resources["QuietButtonStyle"], Padding = new Thickness(8, 4, 8, 4) };
            private KeyStroke _stroke;

            public EdgeRow(EdgeKind kind, MouseButton mouse, KeyStroke key)
            {
                _stroke = Clone(key);
                foreach (var label in new[] { "Key down", "Key up", "Mouse down", "Mouse up" })
                    _kind.Items.Add(label);
                foreach (var label in new[] { "Left", "Right", "Middle", "Back", "Forward" })
                    _mouse.Items.Add(label);
                _kind.SelectedIndex = (int)kind;
                _mouse.SelectedIndex = (int)mouse;
                _kind.SelectionChanged += (_, _) =>
                {
                    ShowTarget();
                    Changed?.Invoke();
                };
                _mouse.SelectionChanged += (_, _) => Changed?.Invoke();
                _key.Click += (_, _) =>
                {
                    _key.Content = "Press a key…";
                    RecordRequested?.Invoke(stroke =>
                    {
                        _stroke = Clone(stroke);
                        _key.Content = _stroke.Name;
                        Changed?.Invoke();
                    });
                };
                var remove = new Button { Content = "Remove", Style = (Style)Application.Current.Resources["QuietButtonStyle"], Padding = new Thickness(8, 4, 8, 4) };
                remove.Click += (_, _) => RemoveRequested?.Invoke();
                Root = new Grid { ColumnSpacing = 6 };
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.Children.Add(_kind);
                Grid.SetColumn(_mouse, 1);
                Grid.SetColumn(_key, 1);
                Grid.SetColumn(remove, 2);
                Root.Children.Add(_mouse);
                Root.Children.Add(_key);
                Root.Children.Add(remove);
                ShowTarget();
            }

            public Grid Root { get; }
            public event Action? Changed;
            public event Action? RemoveRequested;
            public event Action<Action<KeyStroke>>? RecordRequested;

            public EdgeStep Snapshot() => new()
            {
                Kind = (EdgeKind)Math.Clamp(_kind.SelectedIndex, 0, 3),
                Mouse = (MouseButton)Math.Clamp(_mouse.SelectedIndex, 0, 4),
                Key = Clone(_stroke),
            };

            private void ShowTarget()
            {
                bool key = _kind.SelectedIndex is 0 or 1;
                _key.Visibility = key ? Visibility.Visible : Visibility.Collapsed;
                _mouse.Visibility = key ? Visibility.Collapsed : Visibility.Visible;
                _key.Content = string.IsNullOrWhiteSpace(_stroke.Name) ? "Set key" : _stroke.Name;
            }
        }
    }
}
