namespace Win2DS5.Core;

public static class ProfilePresets
{
    public static Profile MouseHand()
    {
        var profile = Skeleton("Mouse hand");
        var shift = profile.GetLayer("shift")!;
        var precision = profile.GetLayer("precision")!;
        var layer = profile.BaseLayer;

        layer.Set(ControlId.R2, Binding.MouseClick(MouseButton.Left));
        layer.Set(ControlId.L2, Binding.MouseClick(MouseButton.Right));
        layer.Set(ControlId.R1, Binding.Hold("shift"));
        layer.Set(ControlId.L1, Binding.Hold("precision"));
        layer.Set(ControlId.Cross, Binding.MouseClick(MouseButton.Left));
        layer.Set(ControlId.Circle, Binding.MouseClick(MouseButton.Right));
        layer.Set(ControlId.Square, Binding.Key(KeyCatalog.Enter));
        layer.Set(ControlId.Triangle, Binding.Key(KeyCatalog.Esc));
        layer.Set(ControlId.DPadUp, Binding.Key(KeyCatalog.Up));
        layer.Set(ControlId.DPadDown, Binding.Key(KeyCatalog.Down));
        layer.Set(ControlId.DPadLeft, Binding.Key(KeyCatalog.Left));
        layer.Set(ControlId.DPadRight, Binding.Key(KeyCatalog.Right));
        layer.Set(ControlId.Create, Binding.Keyboard());
        layer.Set(ControlId.Options, Binding.Key(KeyCatalog.Tab));
        layer.Set(ControlId.Ps, Binding.Key(KeyCatalog.LWin));
        layer.Set(ControlId.L3, Binding.MouseClick(MouseButton.Middle));
        layer.Set(ControlId.TouchpadClick, Binding.MouseClick(MouseButton.Left));
        layer.Set(ControlId.R2Full, Binding.Key(KeyCatalog.Delete));

        shift.Set(ControlId.Cross, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('C')));
        shift.Set(ControlId.Circle, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('V')));
        shift.Set(ControlId.Square, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('X')));
        shift.Set(ControlId.Triangle, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('Z')));
        shift.Set(ControlId.DPadUp, Binding.Key(KeyCatalog.PageUp));
        shift.Set(ControlId.DPadDown, Binding.Key(KeyCatalog.PageDown));
        shift.Set(ControlId.DPadLeft, Binding.Key(KeyCatalog.Home));
        shift.Set(ControlId.DPadRight, Binding.Key(KeyCatalog.End));
        shift.Set(ControlId.R2, Binding.Base());
        shift.Set(ControlId.L2, Binding.Base());
        shift.Set(ControlId.Options, Binding.Key(KeyCatalog.LAlt));

        precision.PointerScale = 0.35;
        precision.AffectsBindings = false;
        return profile;
    }

    public static Profile TriggerShift()
    {
        var profile = Skeleton("Trigger shift");
        var shift = profile.GetLayer("shift")!;
        var layer = profile.BaseLayer;

        layer.Set(ControlId.R2, Binding.Hold("shift", Binding.MouseClick(MouseButton.Left), 200));
        layer.Set(ControlId.L2, Binding.MouseClick(MouseButton.Right));
        layer.Set(ControlId.L1, Binding.Hold("precision"));
        layer.Set(ControlId.R1, Binding.MouseClick(MouseButton.Middle));
        layer.Set(ControlId.Cross, Binding.Key(KeyCatalog.Enter));
        layer.Set(ControlId.Circle, Binding.Key(KeyCatalog.Backspace));
        layer.Set(ControlId.Square, Binding.Key(KeyCatalog.Space));
        layer.Set(ControlId.Triangle, Binding.Key(KeyCatalog.Esc));
        layer.Set(ControlId.DPadUp, Binding.Key(KeyCatalog.Up));
        layer.Set(ControlId.DPadDown, Binding.Key(KeyCatalog.Down));
        layer.Set(ControlId.DPadLeft, Binding.Key(KeyCatalog.Left));
        layer.Set(ControlId.DPadRight, Binding.Key(KeyCatalog.Right));
        layer.Set(ControlId.Create, Binding.Keyboard());
        layer.Set(ControlId.Options, Binding.Key(KeyCatalog.Tab));
        layer.Set(ControlId.Ps, Binding.Key(KeyCatalog.LWin));
        layer.Set(ControlId.TouchpadClick, Binding.MouseClick(MouseButton.Left));

        shift.Set(ControlId.Cross, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('C')));
        shift.Set(ControlId.Circle, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('V')));
        shift.Set(ControlId.Square, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('X')));
        shift.Set(ControlId.Triangle, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('Z')));
        shift.Set(ControlId.DPadUp, Binding.Key(KeyCatalog.PageUp));
        shift.Set(ControlId.DPadDown, Binding.Key(KeyCatalog.PageDown));
        shift.Set(ControlId.L2, Binding.Base());
        shift.Set(ControlId.Options, Binding.Key(KeyCatalog.Delete));

        profile.GetLayer("precision")!.PointerScale = 0.35;
        profile.GetLayer("precision")!.AffectsBindings = false;
        return profile;
    }

    private static Profile Skeleton(string name)
    {
        var profile = new Profile
        {
            Name = name,
            LeftStick = new StickSettings
            {
                Mode = StickMode.Mouse,
                Deadzone = 0.14,
                Sensitivity = 1500,
                Curve = 1.4,
            },
            RightStick = new StickSettings
            {
                Mode = StickMode.Scroll,
                Deadzone = 0.16,
                Sensitivity = 1400,
                Curve = 1.2,
            },
            Touchpad = DefaultTouchpad(),
            Gyro = new GyroSettings(),
            Layers =
            [
                new Layer { Id = "base", Name = "Base" },
                new Layer { Id = "shift", Name = "Shift" },
                new Layer { Id = "precision", Name = "Precision", AffectsBindings = false, PointerScale = 0.35 },
            ],
        };
        return profile;
    }

    private static TouchpadSettings DefaultTouchpad()
    {
        var touch = new TouchpadSettings();
        touch.SetGesture(TouchZone.Center, GestureKind.Tap, Binding.MouseClick(MouseButton.Left));
        touch.SetGesture(TouchZone.Center, GestureKind.LongPress, Binding.MouseClick(MouseButton.Right));
        touch.SetGesture(TouchZone.Center, GestureKind.Click, Binding.MouseClick(MouseButton.Left));
        touch.SetGesture(TouchZone.Left, GestureKind.Tap, Binding.MouseClick(MouseButton.X1));
        touch.SetGesture(TouchZone.Left, GestureKind.Click, Binding.MouseClick(MouseButton.X1));
        touch.SetGesture(TouchZone.Right, GestureKind.Tap, Binding.MouseClick(MouseButton.X2));
        touch.SetGesture(TouchZone.Right, GestureKind.Click, Binding.MouseClick(MouseButton.X2));
        touch.SetGesture(TouchZone.Top, GestureKind.Tap, Binding.MouseClick(MouseButton.Middle));
        touch.SetGesture(TouchZone.Top, GestureKind.LongPress, Binding.Key(KeyCatalog.Enter));
        touch.SetGesture(TouchZone.Top, GestureKind.Click, Binding.MouseClick(MouseButton.Middle));
        return touch;
    }

    private static Binding Chord(params KeyStroke[] keys) => Binding.Key(keys);
}
