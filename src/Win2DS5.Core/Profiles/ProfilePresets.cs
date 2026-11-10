namespace Win2DS5.Core;

public static class ProfilePresets
{
    public static Profile Blank() => Skeleton("New profile");

    public static Profile MouseHand()
    {
        var profile = Skeleton("Mouse hand");
        var shift = profile.GetLayout("shift")!;
        var precision = profile.GetLayout("precision")!;
        var layout = profile.BaseLayout;

        layout.Set(ControlId.R2, Binding.MouseClick(MouseButton.Left));
        layout.Set(ControlId.L2, Binding.MouseClick(MouseButton.Right));
        layout.Set(ControlId.R1, Binding.Hold("shift"));
        layout.Set(ControlId.L1, Binding.Hold("precision"));
        layout.Set(ControlId.Cross, Binding.MouseClick(MouseButton.Left));
        layout.Set(ControlId.Circle, Binding.MouseClick(MouseButton.Right));
        layout.Set(ControlId.Square, Binding.Key(KeyCatalog.Enter));
        layout.Set(ControlId.Triangle, Binding.Key(KeyCatalog.Esc));
        layout.Set(ControlId.DPadUp, Binding.Key(KeyCatalog.Up));
        layout.Set(ControlId.DPadDown, Binding.Key(KeyCatalog.Down));
        layout.Set(ControlId.DPadLeft, Binding.Key(KeyCatalog.Left));
        layout.Set(ControlId.DPadRight, Binding.Key(KeyCatalog.Right));
        layout.Set(ControlId.Create, Binding.Keyboard());
        layout.Set(ControlId.Options, Binding.Key(KeyCatalog.Tab));
        layout.Set(ControlId.Ps, Binding.Key(KeyCatalog.LWin));
        layout.Set(ControlId.L3, Binding.MouseClick(MouseButton.Middle));
        layout.Set(ControlId.TouchpadClick, Binding.MouseClick(MouseButton.Left));
        layout.Set(ControlId.R2Full, Binding.Key(KeyCatalog.Delete));

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
        var shift = profile.GetLayout("shift")!;
        var layout = profile.BaseLayout;

        layout.Set(ControlId.R2, Binding.Hold("shift", Binding.MouseClick(MouseButton.Left), 200));
        layout.Set(ControlId.L2, Binding.MouseClick(MouseButton.Right));
        layout.Set(ControlId.L1, Binding.Hold("precision"));
        layout.Set(ControlId.R1, Binding.MouseClick(MouseButton.Middle));
        layout.Set(ControlId.Cross, Binding.Key(KeyCatalog.Enter));
        layout.Set(ControlId.Circle, Binding.Key(KeyCatalog.Backspace));
        layout.Set(ControlId.Square, Binding.Key(KeyCatalog.Space));
        layout.Set(ControlId.Triangle, Binding.Key(KeyCatalog.Esc));
        layout.Set(ControlId.DPadUp, Binding.Key(KeyCatalog.Up));
        layout.Set(ControlId.DPadDown, Binding.Key(KeyCatalog.Down));
        layout.Set(ControlId.DPadLeft, Binding.Key(KeyCatalog.Left));
        layout.Set(ControlId.DPadRight, Binding.Key(KeyCatalog.Right));
        layout.Set(ControlId.Create, Binding.Keyboard());
        layout.Set(ControlId.Options, Binding.Key(KeyCatalog.Tab));
        layout.Set(ControlId.Ps, Binding.Key(KeyCatalog.LWin));
        layout.Set(ControlId.TouchpadClick, Binding.MouseClick(MouseButton.Left));

        shift.Set(ControlId.Cross, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('C')));
        shift.Set(ControlId.Circle, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('V')));
        shift.Set(ControlId.Square, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('X')));
        shift.Set(ControlId.Triangle, Chord(KeyCatalog.LCtrl, KeyCatalog.Letter('Z')));
        shift.Set(ControlId.DPadUp, Binding.Key(KeyCatalog.PageUp));
        shift.Set(ControlId.DPadDown, Binding.Key(KeyCatalog.PageDown));
        shift.Set(ControlId.L2, Binding.Base());
        shift.Set(ControlId.Options, Binding.Key(KeyCatalog.Delete));

        profile.GetLayout("precision")!.PointerScale = 0.35;
        profile.GetLayout("precision")!.AffectsBindings = false;
        return profile;
    }

    public const string Ds4Id = "ds4";
    public const string XboxId = "xbox";
    public const string TouchpadId = "touchpad";
    public const string SticksId = "sticks";
    public const string ProfileSelectId = "profile-select";

    public static ProfileDocument Shipping()
    {
        var select = ProfileSelect();
        var ds4 = Ds4();
        var xbox = Xbox();
        var touchpad = Touchpad();
        var sticks = Sticks();
        ds4.Id = Ds4Id;
        xbox.Id = XboxId;
        touchpad.Id = TouchpadId;
        sticks.Id = SticksId;
        Share(select, ds4, xbox, touchpad, sticks);
        return new ProfileDocument
        {
            ActiveId = touchpad.Id,
            Profiles = [ds4, xbox, touchpad, sticks],
        };
    }

    public static Profile Ds4() => Gamepad("DS4", "base-ds4", OutputDeviceKind.DualShock4);

    public static Profile Xbox() => Gamepad("XBOX", "base-xbox", OutputDeviceKind.Xbox);

    public static Profile Touchpad()
    {
        var profile = Desktop("Touchpad", "base-touch", "shift-touch", "precision-touch");
        profile.LeftStick.Mode = StickMode.Off;
        profile.RightStick = new StickSettings
        {
            Mode = StickMode.Scroll,
            Deadzone = 0.16,
            Sensitivity = 1400,
            Curve = 1.2,
        };
        profile.Touchpad = DefaultTouchpad();
        return profile;
    }

    public static Profile Sticks()
    {
        var profile = Desktop("Sticks", "base-sticks", "shift-sticks", "precision-sticks");
        profile.LeftStick = new StickSettings
        {
            Mode = StickMode.Mouse,
            Deadzone = 0.14,
            Sensitivity = 1500,
            Curve = 1.4,
        };
        profile.RightStick = new StickSettings
        {
            Mode = StickMode.Scroll,
            Deadzone = 0.16,
            Sensitivity = 1400,
            Curve = 1.2,
        };
        profile.Touchpad = new TouchpadSettings { Mode = TouchpadMode.Off };
        return profile;
    }

    public static Layout ProfileSelect()
    {
        var layout = new Layout { Id = ProfileSelectId, Name = "Profile Select" };
        layout.Set(ControlId.Cross, Binding.SwitchProfile(Ds4Id));
        layout.Set(ControlId.Circle, Binding.SwitchProfile(XboxId));
        layout.Set(ControlId.Square, Binding.SwitchProfile(TouchpadId));
        layout.Set(ControlId.Triangle, Binding.SwitchProfile(SticksId));
        return layout;
    }

    private static void Share(Layout select, params Profile[] profiles)
    {
        foreach (var profile in profiles)
        {
            int index = profile.Layouts.FindIndex(layout => layout.Id == ProfileSelectId);
            if (index >= 0)
                profile.Layouts[index] = select;
        }
    }

    private static Profile Gamepad(string name, string layoutId, OutputDeviceKind kind)
    {
        var profile = new Profile
        {
            Name = name,
            LeftStick = GamepadStick(kind),
            RightStick = GamepadStick(kind),
            Touchpad = new TouchpadSettings { Mode = TouchpadMode.Off },
            Gyro = new GyroSettings(),
            ToggleChord = [],
            BaseLayoutId = layoutId,
            Layouts =
            [
                new Layout { Id = layoutId, Name = name },
                ProfileSelect(),
            ],
        };
        var layout = profile.Layouts[0];
        foreach (var id in ControlNames.Bindable)
        {
            if (id == ControlId.Mute)
                continue;
            var target = id switch
            {
                ControlId.L2Full => ControlId.L2,
                ControlId.R2Full => ControlId.R2,
                _ => id,
            };
            layout.Set(id, Aimed(Binding.Pad(target), kind, 1));
        }

        layout.Set(ControlId.Mute, Binding.Hold(ProfileSelectId));
        return profile;
    }

    private static Profile Desktop(string name, string baseId, string shiftId, string precisionId)
    {
        var profile = new Profile
        {
            Name = name,
            Gyro = new GyroSettings(),
            ToggleChord = [],
            BaseLayoutId = baseId,
            Layouts =
            [
                new Layout { Id = baseId, Name = name },
                new Layout { Id = shiftId, Name = name + " shift" },
                new Layout { Id = precisionId, Name = name + " precision", AffectsBindings = false, PointerScale = 0.35 },
                ProfileSelect(),
            ],
        };
        var layout = profile.Layouts[0];
        var shift = profile.GetLayout(shiftId)!;
        layout.Set(ControlId.R2, Binding.MouseClick(MouseButton.Left));
        layout.Set(ControlId.L2, Binding.MouseClick(MouseButton.Right));
        layout.Set(ControlId.R1, Binding.Hold(shiftId));
        layout.Set(ControlId.L1, Binding.Hold(precisionId));
        layout.Set(ControlId.Cross, Binding.Key(KeyCatalog.Enter));
        layout.Set(ControlId.Circle, Binding.Key(KeyCatalog.Backspace));
        layout.Set(ControlId.Square, Binding.Key(KeyCatalog.Space));
        layout.Set(ControlId.Triangle, Binding.Key(KeyCatalog.Esc));
        layout.Set(ControlId.DPadUp, Binding.Key(KeyCatalog.Up));
        layout.Set(ControlId.DPadDown, Binding.Key(KeyCatalog.Down));
        layout.Set(ControlId.DPadLeft, Binding.Key(KeyCatalog.Left));
        layout.Set(ControlId.DPadRight, Binding.Key(KeyCatalog.Right));
        layout.Set(ControlId.Create, Binding.Keyboard());
        layout.Set(ControlId.Options, Binding.Key(KeyCatalog.Tab));
        layout.Set(ControlId.Ps, Binding.Key(KeyCatalog.LWin));
        layout.Set(ControlId.L3, Binding.MouseClick(MouseButton.Middle));
        layout.Set(ControlId.TouchpadClick, Binding.MouseClick(MouseButton.Left));
        layout.Set(ControlId.Mute, Binding.Hold(ProfileSelectId));

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
        shift.Set(ControlId.Options, Binding.Key(KeyCatalog.Delete));
        return profile;
    }

    private static StickSettings GamepadStick(OutputDeviceKind kind) => new()
    {
        Mode = StickMode.Gamepad,
        Deadzone = 0.06,
        Sensitivity = 1,
        Curve = 1,
        OutputKnown = true,
        OutputKind = kind,
        OutputSlot = 1,
    };

    private static Binding Aimed(Binding binding, OutputDeviceKind kind, int slot)
    {
        binding.OutputKnown = true;
        binding.OutputKind = kind;
        binding.OutputSlot = slot;
        return binding;
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
            BaseLayoutId = "base",
            Layouts =
            [
                new Layout { Id = "base", Name = "Base" },
                new Layout { Id = "shift", Name = "Shift" },
                new Layout { Id = "precision", Name = "Precision", AffectsBindings = false, PointerScale = 0.35 },
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
