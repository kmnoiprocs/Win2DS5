using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Win2DS5.App.Runtime;
using Win2DS5.App.Theming;
using Win2DS5.Core;

namespace Win2DS5.App.Views;

public sealed partial class ControlsPage : UserControl
{
    private readonly DesktopRuntime _runtime = App.Runtime;
    private string _layerId = "base";
    private ControlId _control = ControlId.Cross;

    public ControlsPage()
    {
        InitializeComponent();
        Diagram.ControlChosen += OnControlChosen;
        Diagram.HoverChanged += OnHover;
        Editor.BindingChanged += OnBindingChanged;
        Motion.Fade(HoverCard, 140);
        Motion.Fade(HintText, 140);
        HoverCard.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(180) };
        HoverCard.Translation = new Vector3(0, 6, 0);
        Loaded += (_, _) =>
        {
            Editor.Attach(this);
            RefreshLayers();
            LoadEditor();
        };
    }

    public void Apply(RuntimeSnapshot snapshot)
    {
        Diagram.Apply(snapshot);
        var text = snapshot.LayerName + " layer active";
        if (LayerState.Text != text)
            LayerState.Text = text;
        var editing = "Editing " + EditingName();
        if (EditingState.Text != editing)
            EditingState.Text = editing;
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

        var profile = _runtime.Document.Active;
        var layer = profile.GetLayer(_layerId) ?? profile.BaseLayer;
        HoverName.Text = ControlNames.Display(control);
        HoverBinding.Text = BindingText.Describe(layer.Get(control));
        HoverCard.Opacity = 1;
        HoverCard.Translation = Vector3.Zero;
        HintText.Opacity = 0;
    }

    private void OnAddLayer(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(LayerName.Text) ? "Layer" : LayerName.Text.Trim();
        var id = new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (id.Length == 0)
            id = "layer";
        _runtime.Update(() =>
        {
            var profile = _runtime.Document.Active;
            var unique = id;
            int n = 2;
            while (profile.GetLayer(unique) != null)
                unique = id + n++;
            var layer = new Layer { Id = unique, Name = name };
            foreach (var control in ControlNames.Bindable)
                layer.Set(control, Binding.Base());
            profile.Layers.Add(layer);
            _layerId = unique;
        }, true);
        _runtime.Save();
        LayerName.Text = "";
        RefreshLayers();
        LoadEditor();
    }

    private void RefreshLayers()
    {
        LayerTabs.Children.Clear();
        foreach (var layer in _runtime.Document.Active.Layers)
        {
            bool on = layer.Id == _layerId;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock
            {
                Text = layer.Name,
                Foreground = Theme.Brush(on ? "AccentInkBrush" : "TextBrush"),
                FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var button = new Button
            {
                Content = content,
                Tag = layer.Id,
                Style = (Style)Application.Current.Resources[on ? "PrimaryButtonStyle" : "QuietButtonStyle"],
                CornerRadius = new CornerRadius(17),
                Padding = new Thickness(16, 7, layer.Id == "base" ? 16 : 8, 7),
            };
            if (layer.Id != "base")
            {
                var remove = new Button
                {
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 9 },
                    Tag = layer.Id,
                    Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                    Foreground = Theme.Brush(on ? "AccentInkBrush" : "MutedBrush"),
                    Padding = new Thickness(5),
                    CornerRadius = new CornerRadius(9),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                ToolTipService.SetToolTip(remove, "Remove layer");
                remove.Click += (_, _) => RemoveLayer((string)remove.Tag);
                content.Children.Add(remove);
            }

            button.Click += (_, _) =>
            {
                _layerId = (string)button.Tag;
                RefreshLayers();
                LoadEditor();
            };
            LayerTabs.Children.Add(button);
        }
    }

    private void RemoveLayer(string id)
    {
        _runtime.Update(() =>
        {
            var profile = _runtime.Document.Active;
            profile.Layers.RemoveAll(layer => layer.Id == id);
            foreach (var layer in profile.Layers)
            {
                foreach (var key in layer.Bindings.Keys.ToList())
                {
                    if (layer.Bindings[key].LayerId == id)
                        layer.Bindings.Remove(key);
                }
            }
        }, true);
        if (_layerId == id)
            _layerId = "base";
        _runtime.Save();
        RefreshLayers();
        LoadEditor();
    }

    private void OnControlChosen(ControlId id)
    {
        _control = id;
        LoadEditor();
    }

    private void LoadEditor()
    {
        var profile = _runtime.Document.Active;
        var layer = profile.GetLayer(_layerId) ?? profile.BaseLayer;
        var binding = layer.Get(_control);
        bool locked = _layerId != "base" && profile.BaseLayer.Get(_control)?.Kind is BindingKind.LayerHold or BindingKind.LayerToggle or BindingKind.ToggleKeyboard;
        string hint = locked
            ? "On Base, this control changes layers or opens the keyboard, so it stays available in every layer."
            : "Turn on long press to give a quick tap and a held action different jobs. Holding a layer still switches as soon as you press it.";
        Editor.Show(ControlNames.Display(_control), hint, binding, profile.Layers, !locked, !locked, _layerId != "base" && !locked);
        EditingState.Text = "Editing " + EditingName();
    }

    private void OnBindingChanged(Binding? binding)
    {
        var control = _control;
        var layerId = _layerId;
        _runtime.Update(() =>
        {
            var layer = _runtime.Document.Active.GetLayer(layerId);
            layer?.Set(control, binding);
        }, true);
        _runtime.Save();
    }

    private string EditingName() =>
        _runtime.Document.Active.GetLayer(_layerId)?.Name ?? "Base";
}
