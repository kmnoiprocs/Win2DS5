using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Win2DS5.Core;
using Layout = Win2DS5.Core.Layout;

namespace Win2DS5.App.Controls;

public sealed class ButtonBinder : UserControl
{
    private readonly ComboBox _type = new QuietComboBox { Header = "Control Type", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };
    private readonly ComboBox _action = new QuietComboBox { Header = "Actions", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };
    private readonly List<string> _actionCodes = [];
    private readonly ActionExtras _extras = new();
    private readonly CheckBox _advanced = new() { Content = "Use Advanced Controls" };
    private readonly StackPanel _simple = new() { Spacing = 8 };
    private readonly Grid _macro = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _downSteps = new() { Spacing = 4 };
    private readonly StackPanel _upSteps = new() { Spacing = 4 };
    private readonly ActionExtras _downExtras = new();
    private readonly ActionExtras _upExtras = new();
    private readonly List<OutputDevice> _outputs = [];
    private List<Layout> _layouts = [];
    private List<Profile> _profiles = [];
    private bool _loading;
    private int _dragIndex = -1;
    private bool _dragDown;
    private string _code = "";
    private Binding _binding = new();

    public ButtonBinder()
    {
        foreach (var group in ButtonCatalog.Tree)
            _type.Items.Add(group.Group);
        _type.SelectionChanged += (_, _) =>
        {
            if (_loading || _type.SelectedIndex < 0)
                return;
            FillActions(ButtonCatalog.Tree[_type.SelectedIndex].Group, null);
        };
        _action.SelectionChanged += (_, _) =>
        {
            if (_loading || _action.SelectedIndex < 0 || _action.SelectedIndex >= _actionCodes.Count)
                return;
            ChooseAction(_action.SelectedIndex);
        };
        Hook(_extras);
        _extras.Edited += () => Commit();
        Hook(_downExtras);
        Hook(_upExtras);
        _advanced.Checked += (_, _) => SetAdvanced(true);
        _advanced.Unchecked += (_, _) => SetAdvanced(false);
        BuildMacro();
        var clear = new Button { Content = "Clear Binding", HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources["QuietButtonStyle"] };
        clear.Click += (_, _) =>
        {
            _loading = true;
            _code = "";
            _binding = new Binding();
            _type.SelectedIndex = -1;
            _action.Items.Clear();
            _actionCodes.Clear();
            _extras.Show("", _binding);
            _loading = false;
            Changed?.Invoke(null);
        };
        _simple.Children.Add(_type);
        _simple.Children.Add(_action);
        _simple.Children.Add(_extras);
        var root = new StackPanel { Spacing = 8, Padding = new Thickness(18, 4, 18, 12) };
        root.Children.Add(clear);
        root.Children.Add(_simple);
        root.Children.Add(_macro);
        root.Children.Add(_advanced);
        Content = root;
    }

    public bool IsAdvanced => _advanced.IsChecked == true;

    public event Action<bool>? AdvancedChanged;

    public event Action<Binding?>? Changed;

    public Func<Task<string?>>? BrowseFile { get; set; }

    public Func<Task<string?>>? BrowseFolder { get; set; }

    public void Load(Binding? binding, IReadOnlyList<Layout> layouts, IReadOnlyList<Profile> profiles, IReadOnlyList<OutputDevice> outputs)
    {
        bool dirty = false;
        _loading = true;
        try
        {
            Remember(outputs);
            _layouts = layouts.ToList();
            _profiles = profiles.ToList();
            _binding = binding ?? new Binding();
            _code = ButtonCatalog.Match(_binding) ?? "";
            ShareContext();
            SelectChoice(_code);
            _extras.Show(_code, _binding);
            _advanced.IsChecked = _binding.Advanced;
            ApplyAdvancedVisibility();
            dirty = RenderMacros();
            if (!UsesMacros(_binding) && ButtonCatalog.PadKind(_code) is { } kind && !TargetsValid(_code, _binding.Targets) && _outputs.Any(item => item.Kind == kind))
                dirty = true;
        }
        finally
        {
            _loading = false;
        }

        AdvancedChanged?.Invoke(IsAdvanced);
        if (dirty)
            Changed?.Invoke(UsesMacros(_binding) ? AdvancedBinding() : Snapshot());
    }

    public void RefreshOutputs(IReadOnlyList<OutputDevice> outputs)
    {
        _loading = true;
        try
        {
            Remember(outputs);
            ShareContext();
            _extras.Reshow();
            _downExtras.Reshow();
            _upExtras.Reshow();
        }
        finally
        {
            _loading = false;
        }

        bool dirty = RenderMacros();
        if (!UsesMacros(_binding) && ButtonCatalog.PadKind(_code) != null && !SamePads(_extras.ChosenPads, _binding.Targets))
            dirty = true;
        if (dirty)
            Changed?.Invoke(UsesMacros(_binding) ? AdvancedBinding() : Snapshot());
    }

    private void Remember(IReadOnlyList<OutputDevice> outputs)
    {
        _outputs.Clear();
        _outputs.AddRange(outputs);
    }

    private void ShareContext()
    {
        _extras.SetContext(_layouts, _profiles, _outputs);
        _downExtras.SetContext(_layouts, _profiles, _outputs);
        _upExtras.SetContext(_layouts, _profiles, _outputs);
    }

    private void Hook(ActionExtras extras)
    {
        extras.BrowseFile = () => BrowseFile?.Invoke() ?? Task.FromResult<string?>(null);
        extras.BrowseFolder = () => BrowseFolder?.Invoke() ?? Task.FromResult<string?>(null);
    }

    private void SelectChoice(string code)
    {
        int group = -1;
        for (int i = 0; i < ButtonCatalog.Tree.Count; i++)
        {
            if (ButtonCatalog.Tree[i].Actions.Any(action => action.Code == code))
            {
                group = i;
                break;
            }
        }

        if (group < 0)
        {
            _type.SelectedIndex = -1;
            _action.Items.Clear();
            _actionCodes.Clear();
            return;
        }

        _type.SelectedIndex = group;
        FillActions(ButtonCatalog.Tree[group].Group, code);
    }

    private void FillActions(string group, string? selectCode)
    {
        _action.Items.Clear();
        _actionCodes.Clear();
        foreach (var action in ButtonCatalog.Tree.First(item => item.Group == group).Actions)
        {
            _actionCodes.Add(action.Code);
            _action.Items.Add(action.Label);
        }

        int index = selectCode == null ? 0 : _actionCodes.IndexOf(selectCode);
        _action.SelectedIndex = -1;
        _action.SelectedIndex = index < 0 ? 0 : index;
    }

    private void ChooseAction(int index)
    {
        if (index < 0 || index >= _actionCodes.Count)
            return;
        _code = _actionCodes[index];
        _binding = ButtonCatalog.Create(_code);
        _extras.Show(_code, _binding);
        Changed?.Invoke(Snapshot());
    }

    private static bool SamePads(IReadOnlyList<string> left, IReadOnlyList<string>? right)
    {
        var wanted = (right ?? []).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var chosen = left.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return wanted.SequenceEqual(chosen);
    }

    private bool TargetsValid(string code, IReadOnlyList<string>? targets)
    {
        if (ButtonCatalog.PadKind(code) is not { } kind)
            return true;
        var pads = _outputs.Where(item => item.Kind == kind).Select(item => item.Id).ToHashSet();
        return targets != null && targets.Any(id => pads.Contains(id));
    }

    private Binding Snapshot()
    {
        var binding = ButtonCatalog.Create(_code);
        _extras.ApplyTo(binding);
        if (UsesMacros(_binding))
        {
            binding.Advanced = true;
            binding.DownMacros = _binding.DownMacros ?? [];
            binding.UpMacros = _binding.UpMacros ?? [];
        }

        ButtonCatalog.Aim(binding, _outputs);
        _binding = binding;
        return binding;
    }

    private void BuildMacro()
    {
        _macro.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _macro.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _macro.ColumnSpacing = 12;
        var down = Lane("Physical Downpress", down: true, _downExtras);
        var up = Lane("Physical Release", down: false, _upExtras);
        Grid.SetColumn(up, 1);
        _macro.Children.Add(down);
        _macro.Children.Add(up);
    }

    private StackPanel Lane(string title, bool down, ActionExtras extras)
    {
        var type = new QuietComboBox { PlaceholderText = "Control Type", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };
        var action = new QuietComboBox { PlaceholderText = "Action", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(9) };
        var edge = new QuietComboBox { CornerRadius = new CornerRadius(9), Visibility = Visibility.Collapsed };
        var codes = new List<string>();
        edge.Items.Add("Downpress");
        edge.Items.Add("Release");
        edge.Items.Add("Both");
        edge.SelectedIndex = 0;
        foreach (var group in ButtonCatalog.Tree)
            type.Items.Add(group.Group);
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedIndex < 0)
                return;
            codes.Clear();
            action.Items.Clear();
            foreach (var item in ButtonCatalog.Tree[type.SelectedIndex].Actions)
            {
                codes.Add(item.Code);
                action.Items.Add(item.Label);
            }

            action.SelectedIndex = codes.Count > 0 ? 0 : -1;
        };
        action.SelectionChanged += (_, _) =>
        {
            if (action.SelectedIndex < 0 || action.SelectedIndex >= codes.Count)
            {
                edge.Visibility = Visibility.Collapsed;
                extras.Show("", new Binding());
                return;
            }

            string code = codes[action.SelectedIndex];
            edge.Visibility = ButtonCatalog.IsEdgeAction(code) ? Visibility.Visible : Visibility.Collapsed;
            if (extras.Code != code)
                extras.Show(code, ButtonCatalog.Create(code));
        };
        var add = new Button { Content = "Add", Style = (Style)Application.Current.Resources["QuietButtonStyle"], HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            if (action.SelectedIndex < 0 || action.SelectedIndex >= codes.Count)
                return;
            bool edged = edge.Visibility == Visibility.Visible;
            var step = new MacroStep
            {
                Code = codes[action.SelectedIndex],
                Up = edged && edge.SelectedIndex == 1,
                Both = edged && edge.SelectedIndex == 2,
            };
            extras.ApplyTo(step);
            var list = down ? _binding.DownMacros : _binding.UpMacros;
            list ??= [];
            if (down)
                _binding.DownMacros = list;
            else
                _binding.UpMacros = list;
            list.Add(step);
            _binding.Advanced = true;
            RenderMacros();
            Changed?.Invoke(AdvancedBinding());
        };
        var steps = down ? _downSteps : _upSteps;
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(type);
        panel.Children.Add(action);
        panel.Children.Add(extras);
        panel.Children.Add(edge);
        panel.Children.Add(add);
        panel.Children.Add(steps);
        return panel;
    }

    private void SetAdvanced(bool on)
    {
        if (_loading)
            return;
        _binding.Advanced = on;
        _binding.DownMacros ??= [];
        _binding.UpMacros ??= [];
        ApplyAdvancedVisibility();
        RenderMacros();
        AdvancedChanged?.Invoke(on);
        Changed?.Invoke(on ? AdvancedBinding() : _code.Length == 0 ? null : Snapshot());
    }

    private void ApplyAdvancedVisibility()
    {
        bool on = _advanced.IsChecked == true;
        _simple.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        _macro.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool RenderMacros()
    {
        bool dirty = false;
        dirty |= DrawSteps(_downSteps, _binding.DownMacros, down: true);
        dirty |= DrawSteps(_upSteps, _binding.UpMacros, down: false);
        return dirty;
    }

    private bool DrawSteps(StackPanel host, List<MacroStep>? steps, bool down)
    {
        host.Children.Clear();
        if (steps == null)
            return false;
        bool dirty = false;
        for (int i = 0; i < steps.Count; i++)
        {
            int index = i;
            var step = steps[i];
            if (ButtonCatalog.PadKind(step.Code) is { } kind && !TargetsValid(step.Code, step.Targets))
            {
                var first = _outputs.FirstOrDefault(item => item.Kind == kind);
                if (first != null)
                {
                    step.Targets = [first.Id];
                    dirty = true;
                }
            }

            var row = new Border
            {
                Background = ThemeBrush("PanelAltBrush"),
                BorderBrush = ThemeBrush("LineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4, 4, 4, 4),
                AllowDrop = true,
            };
            var grid = new Grid { ColumnSpacing = 6 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var handle = Mark("\uE76F", click: null);
            handle.CanDrag = true;
            ToolTipService.SetToolTip(handle, "Drag to reorder");
            handle.DragStarting += (_, e) =>
            {
                _dragIndex = index;
                _dragDown = down;
                e.AllowedOperations = DataPackageOperation.Move;
                e.Data.SetText("step");
            };
            handle.DropCompleted += (_, _) => _dragIndex = -1;
            row.DragOver += (_, e) =>
            {
                if (_dragIndex < 0 || _dragDown != down)
                    return;
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            };
            row.DragEnter += (_, _) => row.BorderBrush = ThemeBrush("AccentBrush");
            row.DragLeave += (_, _) => row.BorderBrush = ThemeBrush("LineBrush");
            row.Drop += (_, e) =>
            {
                row.BorderBrush = ThemeBrush("LineBrush");
                e.Handled = true;
                MoveStep(down, _dragIndex, index);
                _dragIndex = -1;
            };
            grid.Children.Add(handle);
            var label = new TextBlock
            {
                Text = StepText(step),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            var remove = Mark("\uE711", () =>
            {
                var list = down ? _binding.DownMacros : _binding.UpMacros;
                if (list == null || index >= list.Count)
                    return;
                list.RemoveAt(index);
                RenderMacros();
                Changed?.Invoke(AdvancedBinding());
            });
            ToolTipService.SetToolTip(remove, "Remove");
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);
            row.Child = grid;
            host.Children.Add(row);
        }

        return dirty;
    }

    private Binding AdvancedBinding()
    {
        _binding.Advanced = true;
        _binding.DownMacros ??= [];
        _binding.UpMacros ??= [];
        return _binding;
    }

    private static bool UsesMacros(Binding binding) =>
        binding.Advanced && ((binding.DownMacros?.Count ?? 0) > 0 || (binding.UpMacros?.Count ?? 0) > 0);

    private static Border Mark(string glyph, Action? click)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ThemeBrush("MutedBrush"),
        };
        var mark = new Border
        {
            Width = 28,
            Height = 28,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = icon,
        };
        mark.PointerEntered += (_, _) => icon.Foreground = ThemeBrush("AccentTextBrush");
        mark.PointerExited += (_, _) => icon.Foreground = ThemeBrush("MutedBrush");
        if (click != null)
        {
            mark.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                click();
            };
        }

        return mark;
    }

    private static Microsoft.UI.Xaml.Media.Brush ThemeBrush(string key) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];

    private void MoveStep(bool down, int from, int to)
    {
        var list = down ? _binding.DownMacros : _binding.UpMacros;
        if (list == null || from < 0 || to < 0 || from >= list.Count || to >= list.Count || from == to)
            return;
        var step = list[from];
        list.RemoveAt(from);
        if (to > from)
            to--;
        list.Insert(to, step);
        RenderMacros();
        Changed?.Invoke(AdvancedBinding());
    }

    private string StepText(MacroStep step)
    {
        string name = ButtonCatalog.StepLabel(step);
        string detail = StepDetail(step);
        return detail.Length == 0 ? name : name + " · " + detail;
    }

    private string StepDetail(MacroStep step)
    {
        if (step.Code is "mouse.up" or "mouse.down" or "mouse.scroll-left" or "mouse.scroll-right")
            return Math.Max(Math.Abs(step.ScrollX), Math.Abs(step.ScrollY)).ToString();
        if (step.Code == "mouse.move")
            return step.ScrollX + ", " + step.ScrollY;
        if (step.Code == "media.volume-custom")
            return step.ScrollY.ToString();
        if (step.Code == "app.layer")
        {
            string mode = step.LayoutToggle ? "Toggle" : "Hold";
            string layout = _layouts.FirstOrDefault(item => item.Id == step.LayoutId)?.Name ?? "";
            return layout.Length == 0 ? mode : mode + " " + layout;
        }

        if (step.Code == "app.profile")
            return _profiles.FirstOrDefault(item => item.Id == step.ProfileId)?.Name ?? "";
        if (step.Code == "windows.open")
            return step.Path;
        if (ButtonCatalog.PadKind(step.Code) == null)
            return "";
        var names = step.Targets.Select(id => _outputs.FirstOrDefault(item => item.Id == id)?.Name).Where(name => !string.IsNullOrEmpty(name));
        return string.Join(", ", names);
    }

    private void Commit()
    {
        if (_loading || _code.Length == 0)
            return;
        Changed?.Invoke(Snapshot());
    }
}
