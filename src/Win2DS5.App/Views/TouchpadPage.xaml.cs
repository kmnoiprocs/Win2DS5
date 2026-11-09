using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Win2DS5.App.Runtime;
using Win2DS5.Core;

namespace Win2DS5.App.Views;

public sealed partial class TouchpadPage : UserControl
{
    private readonly DesktopRuntime _runtime = App.Runtime;
    private bool _loading = true;

    public TouchpadPage()
    {
        InitializeComponent();
        foreach (var mode in new[] { "Off", "Trackpad", "Stick" })
        {
            ModeBox.Items.Add(mode);
            LeftMode.Items.Add(mode == "Trackpad" ? "Mouse" : mode == "Stick" ? "Scroll" : "Off");
            RightMode.Items.Add(mode == "Trackpad" ? "Mouse" : mode == "Stick" ? "Scroll" : "Off");
        }
        LeftMode.Items.Clear();
        RightMode.Items.Clear();
        foreach (var mode in new[] { "Off", "Mouse", "Scroll", "Controller" })
        {
            LeftMode.Items.Add(mode);
            RightMode.Items.Add(mode);
        }

        ZoneBox.Items.Add("Center");
        ZoneBox.Items.Add("Left");
        ZoneBox.Items.Add("Right");
        ZoneBox.Items.Add("Top");
        GestureBox.Items.Add("Tap");
        GestureBox.Items.Add("Long press");
        GestureBox.Items.Add("Click");
        GyroActivator.Items.Add("Always");
        foreach (var control in ControlNames.Bindable)
            GyroActivator.Items.Add(ControlNames.Display(control));

        Zones.ZonesChanged += (top, left, right) =>
        {
            if (_loading) return;
            _runtime.Update(() =>
            {
                var touch = _runtime.Document.Active.Touchpad;
                touch.TopFraction = top;
                touch.LeftFraction = left;
                touch.RightFraction = right;
            });
            _runtime.Save();
        };
        Zones.ZoneSelected += zone => ZoneBox.SelectedIndex = (int)zone;
        Curve.ExponentChanged += value =>
        {
            if (_loading) return;
            _runtime.Update(() => _runtime.Document.Active.Touchpad.Curve = value);
            _runtime.Save();
        };
        GestureEditor.BindingChanged += binding =>
        {
            if (_loading) return;
            var zone = (TouchZone)ZoneBox.SelectedIndex;
            var kind = (GestureKind)GestureBox.SelectedIndex;
            _runtime.Update(() => _runtime.Document.Active.Touchpad.SetGesture(zone, kind, binding), true);
            _runtime.Save();
        };
        Loaded += (_, _) =>
        {
            GestureEditor.Attach(this);
            LoadFromProfile();
        };
    }

    public void Apply(RuntimeSnapshot snapshot) =>
        Zones.SetFinger(snapshot.TouchActive, snapshot.TouchX, snapshot.TouchY);

    private void LoadFromProfile()
    {
        _loading = true;
        var profile = _runtime.Document.Active;
        var touch = profile.Touchpad;
        ModeBox.SelectedIndex = (int)touch.Mode;
        Sensitivity.Value = touch.Mode == TouchpadMode.Stick ? touch.StickSensitivity / 1000.0 : touch.Sensitivity;
        Curve.Exponent = touch.Curve;
        TwoFinger.IsChecked = touch.TwoFingerScroll;
        EdgeScroll.IsChecked = touch.EdgeScroll;
        LongPress.Value = touch.LongPressMs;
        Zones.SetFractions(touch.TopFraction, touch.LeftFraction, touch.RightFraction);
        LeftMode.SelectedIndex = (int)profile.LeftStick.Mode;
        LeftSensitivity.Value = profile.LeftStick.Sensitivity;
        LeftDeadzone.Value = profile.LeftStick.Deadzone;
        LeftCurve.Value = profile.LeftStick.Curve;
        RightMode.SelectedIndex = (int)profile.RightStick.Mode;
        RightSensitivity.Value = profile.RightStick.Sensitivity;
        RightDeadzone.Value = profile.RightStick.Deadzone;
        RightCurve.Value = profile.RightStick.Curve;
        GyroEnabled.IsChecked = profile.Gyro.Enabled;
        GyroSensitivity.Value = profile.Gyro.Sensitivity;
        InvertX.IsChecked = profile.Gyro.InvertX;
        InvertY.IsChecked = profile.Gyro.InvertY;
        GyroActivator.SelectedIndex = profile.Gyro.Activator == ControlId.None
            ? 0
            : ControlNames.Bindable.ToList().IndexOf(profile.Gyro.Activator) + 1;
        ZoneBox.SelectedIndex = 0;
        GestureBox.SelectedIndex = 0;
        UpdateLabels();
        LoadGesture();
        _loading = false;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _runtime.Update(() =>
        {
            var profile = _runtime.Document.Active;
            var touch = profile.Touchpad;
            touch.Mode = (TouchpadMode)ModeBox.SelectedIndex;
            if (touch.Mode == TouchpadMode.Stick)
                touch.StickSensitivity = Sensitivity.Value * 1000;
            else
                touch.Sensitivity = Sensitivity.Value;
            touch.TwoFingerScroll = TwoFinger.IsChecked == true;
            touch.EdgeScroll = EdgeScroll.IsChecked == true;
            touch.LongPressMs = (int)LongPress.Value;
            profile.LeftStick.Mode = (StickMode)LeftMode.SelectedIndex;
            profile.LeftStick.Sensitivity = LeftSensitivity.Value;
            profile.LeftStick.Deadzone = LeftDeadzone.Value;
            profile.LeftStick.Curve = LeftCurve.Value;
            profile.RightStick.Mode = (StickMode)RightMode.SelectedIndex;
            profile.RightStick.Sensitivity = RightSensitivity.Value;
            profile.RightStick.Deadzone = RightDeadzone.Value;
            profile.RightStick.Curve = RightCurve.Value;
            profile.Gyro.Enabled = GyroEnabled.IsChecked == true;
            profile.Gyro.Sensitivity = GyroSensitivity.Value;
            profile.Gyro.InvertX = InvertX.IsChecked == true;
            profile.Gyro.InvertY = InvertY.IsChecked == true;
            profile.Gyro.Activator = GyroActivator.SelectedIndex <= 0
                ? ControlId.None
                : ControlNames.Bindable[GyroActivator.SelectedIndex - 1];
        });
        UpdateLabels();
        _runtime.Save();
    }

    private void OnGestureTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ZoneBox.SelectedIndex >= 0)
            Zones.SelectZone((TouchZone)ZoneBox.SelectedIndex);
        LoadGesture();
    }

    private void LoadGesture()
    {
        if (ZoneBox.SelectedIndex < 0 || GestureBox.SelectedIndex < 0)
            return;
        var zone = (TouchZone)ZoneBox.SelectedIndex;
        var kind = (GestureKind)GestureBox.SelectedIndex;
        var binding = _runtime.Document.Active.Touchpad.GetGesture(zone, kind);
        GestureEditor.Show($"{zone} {kind}", "A drag never fires a zone. Tap and long-press do.", binding, _runtime.Document.Active.Layers, false, false, false);
    }

    private void UpdateLabels()
    {
        var profile = _runtime.Document.Active;
        var touch = profile.Touchpad;
        SensitivityLabel.Text = touch.Mode == TouchpadMode.Stick ? "Stick speed" : "Trackpad sensitivity";
        SensitivityValue.Text = touch.Mode == TouchpadMode.Stick ? $"{touch.StickSensitivity:0}" : $"{touch.Sensitivity:0.00}×";
        LongPressValue.Text = $"{touch.LongPressMs} ms";
        LeftSensitivityValue.Text = $"{profile.LeftStick.Sensitivity:0}";
        LeftDeadzoneValue.Text = $"{profile.LeftStick.Deadzone * 100:0}%";
        LeftCurveValue.Text = $"{profile.LeftStick.Curve:0.00}";
        RightSensitivityValue.Text = $"{profile.RightStick.Sensitivity:0}";
        RightDeadzoneValue.Text = $"{profile.RightStick.Deadzone * 100:0}%";
        RightCurveValue.Text = $"{profile.RightStick.Curve:0.00}";
        GyroSensitivityValue.Text = $"{profile.Gyro.Sensitivity:0}";
    }
}
