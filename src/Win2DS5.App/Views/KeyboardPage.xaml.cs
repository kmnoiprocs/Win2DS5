using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Win2DS5.App.Views;

public sealed partial class KeyboardPage : UserControl
{
    private bool _loading = true;

    public KeyboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var settings = App.Runtime.Document.Settings;
            OpacitySlider.Value = settings.KeyboardOpacity;
            WidthSlider.Value = settings.KeyboardWidth;
            UpdateLabels();
            _loading = false;
        };
    }

    private void OnShow(object sender, RoutedEventArgs e)
    {
        App.Runtime.ToggleKeyboard();
        var open = false;
        App.Runtime.Update(() => open = App.Runtime.Session.Keyboard.IsOpen);
        ShowButton.Content = open ? "Hide keyboard" : "Show keyboard";
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        App.Runtime.Update(() =>
        {
            var keyboard = App.Runtime.Session.Keyboard;
            var settings = App.Runtime.Document.Settings;
            keyboard.Opacity = OpacitySlider.Value;
            keyboard.Width = WidthSlider.Value;
            keyboard.WriteSettings(settings);
        });
        UpdateLabels();
        App.Runtime.Save();
    }

    private void UpdateLabels()
    {
        OpacityLabel.Text = $"{OpacitySlider.Value * 100:0}%";
        WidthLabel.Text = $"{WidthSlider.Value:0} px";
    }
}
