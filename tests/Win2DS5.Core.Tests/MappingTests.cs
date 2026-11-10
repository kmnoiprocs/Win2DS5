using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

public class MappingTests
{
    [Fact]
    public void MouseHand_TriggerHoldsLeftClick()
    {
        var session = Session(ProfilePresets.MouseHand());
        var down = session.Step(Pad.Hold(ControlId.R2), Ms(0));
        var up = session.Step(Pad.Hold(), Ms(30));

        Assert.True(Pad.Has(down, OutputKind.MouseDown, (int)MouseButton.Left));
        Assert.True(Pad.Has(up, OutputKind.MouseUp, (int)MouseButton.Left));
    }

    [Fact]
    public void ShiftLayout_ChangesTheNextButton()
    {
        var session = Session(ProfilePresets.MouseHand());
        session.Step(Pad.Hold(ControlId.R1), Ms(0));
        var pressed = session.Step(Pad.Hold(ControlId.R1, ControlId.Cross), Ms(20));
        var released = session.Step(Pad.Hold(ControlId.R1), Ms(40));

        Assert.Equal("shift", pressed.ActiveLayoutId);
        Assert.Contains(pressed.Commands, command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.LCtrl.ScanCode);
        Assert.Contains(pressed.Commands, command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.Letter('C').ScanCode);
        Assert.Contains(released.Commands, command => command.Kind == OutputKind.KeyUp && command.A == KeyCatalog.Letter('C').ScanCode);
        Assert.DoesNotContain(pressed.Commands, command => command.Kind == OutputKind.MouseDown);
    }

    [Fact]
    public void ShiftLayout_DoesNotFallThrough()
    {
        var session = Session(ProfilePresets.MouseHand());
        session.Step(Pad.Hold(ControlId.R1), Ms(0));
        var pressed = session.Step(Pad.Hold(ControlId.R1, ControlId.L3), Ms(20));
        Assert.Empty(pressed.Commands);
    }

    [Fact]
    public void UseBase_KeepsTheClickWhileShifted()
    {
        var session = Session(ProfilePresets.MouseHand());
        session.Step(Pad.Hold(ControlId.R1), Ms(0));
        var pressed = session.Step(Pad.Hold(ControlId.R1, ControlId.R2), Ms(20));
        Assert.True(Pad.Has(pressed, OutputKind.MouseDown, (int)MouseButton.Left));
    }

    [Fact]
    public void TriggerShift_TapClicksAndHoldChangesLayout()
    {
        var session = Session(ProfilePresets.TriggerShift());
        session.Step(Pad.Hold(ControlId.R2), Ms(0));
        var tap = session.Step(Pad.Hold(), Ms(80));
        Assert.True(Pad.Has(tap, OutputKind.MouseDown, (int)MouseButton.Left));
        Assert.True(Pad.Has(tap, OutputKind.MouseUp, (int)MouseButton.Left));

        var held = Session(ProfilePresets.TriggerShift());
        held.Step(Pad.Hold(ControlId.R2), Ms(0));
        var chord = held.Step(Pad.Hold(ControlId.R2, ControlId.Cross), Ms(40));
        var release = held.Step(Pad.Hold(), Ms(80));
        Assert.Equal("shift", chord.ActiveLayoutId);
        Assert.Contains(chord.Commands, command => command.A == KeyCatalog.Letter('C').ScanCode && command.Kind == OutputKind.KeyDown);
        Assert.DoesNotContain(release.Commands, command => command.Kind == OutputKind.MouseDown);
    }

    [Fact]
    public void MuteAndCreate_TogglesOnceAndDoesNotOpenTheKeyboard()
    {
        var session = Session(ProfilePresets.MouseHand());
        var both = session.Step(Pad.Hold(ControlId.Mute, ControlId.Create), Ms(0));
        var held = session.Step(Pad.Hold(ControlId.Mute, ControlId.Create), Ms(20));
        Assert.True(both.ToggleDesktop);
        Assert.False(both.ToggleKeyboard);
        Assert.False(held.ToggleDesktop);
    }

    [Fact]
    public void CreateAlone_TogglesTheKeyboard()
    {
        var session = Session(ProfilePresets.MouseHand());
        var pressed = session.Step(Pad.Hold(ControlId.Create), Ms(0));
        Assert.True(pressed.ToggleKeyboard);
        Assert.False(pressed.ToggleDesktop);
    }

    [Fact]
    public void DesktopOff_EmitsNothingAndReleaseClearsTheClick()
    {
        var session = Session(ProfilePresets.MouseHand());
        session.Step(Pad.Hold(ControlId.R2), Ms(0));
        var released = session.ReleaseAll();
        session.DesktopEnabled = false;
        var ignored = session.Step(Pad.Hold(ControlId.R2), Ms(30));

        Assert.Contains(released, command => command.Kind == OutputKind.MouseUp && command.A == (int)MouseButton.Left);
        Assert.Empty(ignored.Commands);
    }

    [Fact]
    public void PrecisionLayout_SlowsThePointer()
    {
        var fast = MoveRight(ProfilePresets.MouseHand(), precision: false);
        var slow = MoveRight(ProfilePresets.MouseHand(), precision: true);
        Assert.True(fast > slow);
        Assert.InRange(slow, fast * 0.25, fast * 0.5);
    }

    [Fact]
    public void StickScroll_DownIsNegativeWheel()
    {
        var profile = ProfilePresets.MouseHand();
        profile.RightStick.Deadzone = 0;
        profile.RightStick.Curve = 1;
        profile.RightStick.Sensitivity = 4000;
        var session = Session(profile);
        var frame = ControllerFrame.Neutral();
        frame.RightY = 255;
        session.Step(frame, Ms(0));
        var moved = session.Step(frame, Ms(40));
        var scroll = moved.Commands.Single(command => command.Kind == OutputKind.Scroll);
        Assert.True(scroll.B < -100);
    }

    [Fact]
    public void Gyro_MovesOnlyWhileTheActivatorIsHeld()
    {
        var profile = ProfilePresets.MouseHand();
        profile.Gyro.Enabled = true;
        profile.Gyro.Activator = ControlId.L3;
        profile.Gyro.Deadzone = 0;
        profile.Gyro.Sensitivity = 400;
        profile.LeftStick.Mode = StickMode.Off;
        profile.RightStick.Mode = StickMode.Off;
        profile.Touchpad.Mode = TouchpadMode.Off;
        var session = Session(profile);
        var frame = ControllerFrame.Neutral();
        frame.GyroY = 1024;
        frame.L3 = true;
        session.Step(frame, Ms(0));
        var moved = session.Step(frame, Ms(40));
        Assert.Contains(moved.Commands, command => command.Kind == OutputKind.MouseMove && command.A >= 9);

        var idle = ControllerFrame.Neutral();
        idle.GyroY = 1024;
        var quiet = Session(profile);
        quiet.Step(idle, Ms(0));
        var still = quiet.Step(idle, Ms(40));
        Assert.DoesNotContain(still.Commands, command => command.Kind == OutputKind.MouseMove);
    }

    [Fact]
    public void OnScreenKeyboard_TypesInsteadOfClicking()
    {
        var session = Session(ProfilePresets.MouseHand());
        session.Keyboard.IsOpen = true;
        var typed = session.Step(Pad.Hold(ControlId.Cross), Ms(0));
        Assert.Contains(typed.Commands, command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.Letter('Q').ScanCode);
        Assert.Contains(typed.Commands, command => command.Kind == OutputKind.KeyUp && command.A == KeyCatalog.Letter('Q').ScanCode);
        Assert.DoesNotContain(typed.Commands, command => command.Kind == OutputKind.MouseDown);
    }

    [Fact]
    public void OnScreenKeyboard_RightStickMovesTheWindow()
    {
        var session = Session(ProfilePresets.MouseHand());
        session.Keyboard.IsOpen = true;
        double start = session.Keyboard.Left;
        var frame = ControllerFrame.Neutral();
        frame.RightX = 255;
        session.Step(frame, Ms(0));
        session.Step(frame, Ms(40));
        Assert.True(session.Keyboard.Left > start + 40);
    }

    [Fact]
    public void OnScreenKeyboard_R2ChangesOpacity()
    {
        var session = Session(ProfilePresets.MouseHand());
        session.Keyboard.IsOpen = true;
        session.Keyboard.Opacity = 0.9;
        var frame = ControllerFrame.Neutral();
        frame.RightY = 255;
        frame.R2 = 200;
        var clock = TimeSpan.Zero;
        session.Step(frame, clock);
        for (int i = 0; i < 8; i++)
        {
            clock += Ms(40);
            session.Step(frame, clock);
        }
        Assert.True(session.Keyboard.Opacity < 0.7);
    }

    [Fact]
    public void LongPress_TapSendsTheShortActionAndHoldSendsTheOther()
    {
        var profile = ProfilePresets.MouseHand();
        profile.BaseLayout.Set(ControlId.Square, new Binding
        {
            Kind = BindingKind.Key,
            Keys = [KeyCatalog.Space],
            Tap = Binding.Key(KeyCatalog.Enter),
            TapMs = 300,
        });

        var tapped = Session(profile);
        tapped.LongPressMs = 300;
        var down = tapped.Step(Pad.Hold(ControlId.Square), Ms(0));
        Assert.DoesNotContain(down.Commands, command => command.Kind == OutputKind.KeyDown);
        var released = tapped.Step(Pad.Hold(), Ms(80));
        Assert.Contains(released.Commands, command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.Enter.ScanCode);
        Assert.Contains(released.Commands, command => command.Kind == OutputKind.KeyUp && command.A == KeyCatalog.Enter.ScanCode);
        Assert.DoesNotContain(released.Commands, command => command.A == KeyCatalog.Space.ScanCode);

        var held = Session(profile);
        held.LongPressMs = 300;
        held.Step(Pad.Hold(ControlId.Square), Ms(0));
        var waiting = held.Step(Pad.Hold(ControlId.Square), Ms(120));
        Assert.DoesNotContain(waiting.Commands, command => command.Kind == OutputKind.KeyDown);
        var stillHeld = held.Step(Pad.Hold(ControlId.Square), Ms(320));
        Assert.DoesNotContain(stillHeld.Commands, command => command.Kind == OutputKind.KeyDown);
        var up = held.Step(Pad.Hold(), Ms(400));
        Assert.Contains(up.Commands, command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.Space.ScanCode);
        Assert.Contains(up.Commands, command => command.Kind == OutputKind.KeyUp && command.A == KeyCatalog.Space.ScanCode);
        Assert.DoesNotContain(up.Commands, command => command.A == KeyCatalog.Enter.ScanCode);
    }

    [Fact]
    public void LongPress_OverrideDelayBeatsTheGlobalWindow()
    {
        var profile = ProfilePresets.MouseHand();
        profile.BaseLayout.Set(ControlId.Square, new Binding
        {
            Kind = BindingKind.Key,
            Keys = [KeyCatalog.Space],
            Tap = Binding.Key(KeyCatalog.Enter),
            TapMs = 200,
            OverrideDelay = true,
        });
        var session = Session(profile);
        session.LongPressMs = 800;
        session.Step(Pad.Hold(ControlId.Square), Ms(0));
        var up = session.Step(Pad.Hold(), Ms(250));
        Assert.Contains(up.Commands, command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.Space.ScanCode);
        Assert.DoesNotContain(up.Commands, command => command.A == KeyCatalog.Enter.ScanCode);
    }

    [Fact]
    public void AdvancedSteps_RunOnPressAndReleaseInOrder()
    {
        var profile = ProfilePresets.MouseHand();
        profile.BaseLayout.Set(ControlId.R2, new Binding
        {
            Kind = BindingKind.None,
            Advanced = true,
            PressSteps =
            [
                new EdgeStep { Kind = EdgeKind.MouseDown, Mouse = MouseButton.Left },
                new EdgeStep { Kind = EdgeKind.KeyDown, Key = KeyCatalog.LShift },
                new EdgeStep { Kind = EdgeKind.KeyUp, Key = KeyCatalog.LShift },
            ],
            ReleaseSteps =
            [
                new EdgeStep { Kind = EdgeKind.MouseUp, Mouse = MouseButton.Left },
                new EdgeStep { Kind = EdgeKind.MouseDown, Mouse = MouseButton.Right },
                new EdgeStep { Kind = EdgeKind.MouseUp, Mouse = MouseButton.Right },
            ],
        });
        var session = Session(profile);
        var down = session.Step(Pad.Hold(ControlId.R2), Ms(0));
        Assert.Equal([OutputKind.MouseDown, OutputKind.KeyDown, OutputKind.KeyUp], down.Commands.Select(command => command.Kind).ToArray());
        var held = session.Step(Pad.Hold(ControlId.R2), Ms(40));
        Assert.DoesNotContain(held.Commands, command => command.Kind is OutputKind.MouseDown or OutputKind.MouseUp or OutputKind.KeyDown);
        var up = session.Step(Pad.Hold(), Ms(80));
        Assert.Equal([OutputKind.MouseUp, OutputKind.MouseDown, OutputKind.MouseUp], up.Commands.Select(command => command.Kind).ToArray());
        Assert.Equal((int)MouseButton.Left, up.Commands[0].A);
        Assert.Equal((int)MouseButton.Right, up.Commands[1].A);
        Assert.Equal((int)MouseButton.Right, up.Commands[2].A);
    }

    [Fact]
    public void AdvancedSteps_StayQuietWhileLongPressIsEnabled()
    {
        var profile = ProfilePresets.MouseHand();
        profile.BaseLayout.Set(ControlId.Square, new Binding
        {
            Kind = BindingKind.Key,
            Keys = [KeyCatalog.Space],
            Tap = Binding.Key(KeyCatalog.Enter),
            Advanced = true,
            PressSteps = [new EdgeStep { Kind = EdgeKind.KeyDown, Key = KeyCatalog.LShift }],
            ReleaseSteps = [new EdgeStep { Kind = EdgeKind.KeyDown, Key = KeyCatalog.LCtrl }],
        });
        var session = Session(profile);
        session.LongPressMs = 300;
        var down = session.Step(Pad.Hold(ControlId.Square), Ms(0));
        Assert.Empty(down.Commands);
        var up = session.Step(Pad.Hold(), Ms(40));
        Assert.Contains(up.Commands, command => command.Kind == OutputKind.KeyDown && command.A == KeyCatalog.Enter.ScanCode);
        Assert.DoesNotContain(up.Commands, command => command.A == KeyCatalog.LShift.ScanCode || command.A == KeyCatalog.LCtrl.ScanCode);
    }

    [Fact]
    public void PadBinding_PressesTheChosenControllerButton()
    {
        var profile = ProfilePresets.MouseHand();
        profile.BaseLayout.Set(ControlId.Triangle, Binding.Pad(ControlId.Circle));
        var session = Session(profile);
        var down = session.Step(Pad.Hold(ControlId.Triangle), Ms(0));
        var up = session.Step(Pad.Hold(), Ms(30));
        Assert.Contains(down.Commands, command => command.Kind == OutputKind.PadDown && command.A == (int)ControlId.Circle);
        Assert.Contains(up.Commands, command => command.Kind == OutputKind.PadUp && command.A == (int)ControlId.Circle);
    }

    [Fact]
    public void GamepadStick_ReportsAxesInsteadOfMouse()
    {
        var profile = ProfilePresets.MouseHand();
        profile.LeftStick.Mode = StickMode.Gamepad;
        profile.LeftStick.Deadzone = 0;
        profile.LeftStick.Curve = 1;
        var session = Session(profile);
        var frame = ControllerFrame.Neutral();
        frame.LeftX = 255;
        var moved = session.Step(frame, Ms(0));
        var stick = Assert.Single(moved.Commands, command => command.Kind == OutputKind.LeftStick);
        Assert.True(stick.A > 20000);
        Assert.DoesNotContain(moved.Commands, command => command.Kind == OutputKind.MouseMove);
    }

    [Fact]
    public void ReleaseAll_CentersSticksTheSessionWasDriving()
    {
        var profile = ProfilePresets.Blank();
        profile.LeftStick.Mode = StickMode.Gamepad;
        profile.LeftStick.Deadzone = 0;
        profile.LeftStick.Curve = 1;
        profile.LeftStick.OutputId = "pad-1";
        var session = Session(profile);
        var frame = ControllerFrame.Neutral();
        frame.LeftX = 255;
        session.Step(frame, Ms(0));
        var released = session.ReleaseAll();
        Assert.Contains(released, command => command.Kind == OutputKind.LeftStick && command.A == 0 && command.B == 0 && command.OutputId == "pad-1");
        Assert.Contains(released, command => command.Kind == OutputKind.RightStick && command.A == 0 && command.B == 0 && command.OutputId == "pad-1");
    }

    [Fact]
    public void PadBinding_PressesEverySelectedGamepad()
    {
        var profile = ProfilePresets.Blank();
        profile.BaseLayout.Set(ControlId.Cross, new Binding
        {
            Kind = BindingKind.Pad,
            PadButton = ControlId.Cross,
            Action = "xbox.a",
            OutputKind = OutputDeviceKind.Xbox,
            Targets = ["xbox-1", "xbox-2"],
        });
        var session = Session(profile);
        var pressed = session.Step(Pad.Hold(ControlId.Cross), Ms(0));
        var downs = pressed.Commands.Where(command => command.Kind == OutputKind.PadDown).Select(command => command.OutputId).ToArray();
        Assert.Equal(["xbox-1", "xbox-2"], downs);
    }

    [Fact]
    public void MacroMouseMove_UsesTheStepDistanceAndPositiveYMovesUp()
    {
        var profile = ProfilePresets.Blank();
        profile.BaseLayout.Set(ControlId.Square, new Binding
        {
            Advanced = true,
            DownMacros = [new MacroStep { Code = "mouse.move", ScrollX = 5, ScrollY = 25 }],
        });
        var session = Session(profile);
        var moved = session.Step(Pad.Hold(ControlId.Square), Ms(0));
        var move = Assert.Single(moved.Commands, command => command.Kind == OutputKind.MouseMove);
        Assert.Equal(5, move.A);
        Assert.Equal(-25, move.B);
    }

    [Fact]
    public void MouseMove_PositiveYMovesThePointerUp()
    {
        var profile = ProfilePresets.Blank();
        profile.BaseLayout.Set(ControlId.Square, new Binding
        {
            Kind = BindingKind.MouseMove,
            ScrollX = 12,
            ScrollY = 40,
        });
        var session = Session(profile);
        var moved = session.Step(Pad.Hold(ControlId.Square), Ms(0));
        var move = Assert.Single(moved.Commands, command => command.Kind == OutputKind.MouseMove);
        Assert.Equal(12, move.A);
        Assert.Equal(-40, move.B);
    }

    [Fact]
    public void BothEdge_PressesAndReleasesInOneStep()
    {
        var profile = ProfilePresets.Blank();
        profile.BaseLayout.Set(ControlId.Cross, new Binding
        {
            Advanced = true,
            DownMacros = [new MacroStep { Code = "mouse.left", Both = true }],
        });
        var session = Session(profile);
        var down = session.Step(Pad.Hold(ControlId.Cross), Ms(0));
        Assert.Equal([OutputKind.MouseDown, OutputKind.MouseUp], down.Commands.Select(command => command.Kind).ToArray());
        var up = session.Step(Pad.Hold(), Ms(30));
        Assert.DoesNotContain(up.Commands, command => command.Kind is OutputKind.MouseDown or OutputKind.MouseUp);
    }

    [Fact]
    public void ToggleWin2DS5_PausesAndResumesOnTheSameButton()
    {
        var profile = ProfilePresets.Blank();
        profile.BaseLayout.Set(ControlId.Mute, new Binding { Kind = BindingKind.Pause });
        var session = Session(profile);
        var pause = session.Step(Pad.Hold(ControlId.Mute), Ms(0));
        Assert.True(pause.Pause);
        session.Passthrough = true;
        session.Step(Pad.Hold(), Ms(20));
        var resume = session.Step(Pad.Hold(ControlId.Mute), Ms(40));
        Assert.True(resume.Pause);
        Assert.Equal("app.pause", ButtonCatalog.Match(new Binding { Kind = BindingKind.Resume }));
        Assert.Equal("Toggle Win2DS5", ButtonCatalog.Find("app.pause")?.Label);
    }

    private static MappingSession Session(Profile profile) => new(profile) { DesktopEnabled = true };

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private static int MoveRight(Profile profile, bool precision)
    {
        profile.LeftStick.Deadzone = 0;
        profile.LeftStick.Curve = 1;
        profile.LeftStick.Sensitivity = 1000;
        profile.Touchpad.Mode = TouchpadMode.Off;
        var session = Session(profile);
        var frame = ControllerFrame.Neutral();
        frame.LeftX = 255;
        if (precision)
            frame.L1 = true;
        session.Step(frame, Ms(0));
        var moved = session.Step(frame, Ms(40));
        return moved.Commands.Where(command => command.Kind == OutputKind.MouseMove).Sum(command => command.A);
    }
}

