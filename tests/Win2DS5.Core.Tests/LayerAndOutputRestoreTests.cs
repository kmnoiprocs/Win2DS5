using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

public class LayerAndOutputRestoreTests
{
    [Fact]
    public void Restore_AddsARememberedPadAndPointsTheBindingAtIt()
    {
        var profile = ProfilePresets.Blank();
        var binding = Binding.Pad(ControlId.Cross);
        binding.OutputId = "old-ds4";
        binding.OutputKnown = true;
        binding.OutputKind = OutputDeviceKind.DualShock4;
        binding.OutputSlot = 2;
        profile.BaseLayer.Set(ControlId.Cross, binding);
        var document = new ProfileDocument { Profiles = [profile] };
        ProfileStore.Repair(document);
        var missing = OutputRestore.Find(profile, document.Outputs);
        Assert.Contains(missing, item => item.OldId == "old-ds4" && item.Kind == OutputDeviceKind.DualShock4 && item.Slot == 2);
        Assert.Empty(OutputRestore.Restore(document, profile, missing));
        var created = Assert.Single(document.Outputs, output => output.Kind == OutputDeviceKind.DualShock4);
        Assert.Equal(created.Id, profile.BaseLayer.Get(ControlId.Cross)!.OutputId);
    }

    [Fact]
    public void Shipping_ReplacesTheLibraryWithFourProfilesAndASharedPicker()
    {
        var document = ProfileStore.CreateDefault();
        Assert.Equal(["DS4", "XBOX", "Touchpad", "Sticks"], document.Profiles.Select(profile => profile.Name));
        Assert.Equal(ProfilePresets.TouchpadId, document.ActiveId);

        var ds4 = document.Profiles[0];
        var xbox = document.Profiles[1];
        Assert.Equal(StickMode.Gamepad, ds4.LeftStick.Mode);
        Assert.Equal(OutputDeviceKind.DualShock4, ds4.LeftStick.OutputKind);
        Assert.Equal(BindingKind.Pad, ds4.BaseLayer.Get(ControlId.Cross)!.Kind);
        Assert.Equal(ControlId.Cross, ds4.BaseLayer.Get(ControlId.Cross)!.PadButton);
        Assert.Equal(OutputDeviceKind.Xbox, xbox.BaseLayer.Get(ControlId.Circle)!.OutputKind);
        Assert.Single(ds4.Layers, layer => layer.Name != "Profile Select");
        Assert.Single(xbox.Layers, layer => layer.Name != "Profile Select");

        foreach (var profile in document.Profiles)
        {
            Assert.Equal(BindingKind.LayerHold, profile.BaseLayer.Get(ControlId.Mute)!.Kind);
            Assert.Equal(ProfilePresets.ProfileSelectId, profile.BaseLayer.Get(ControlId.Mute)!.LayerId);
            Assert.Same(document.Profiles[0].GetLayer(ProfilePresets.ProfileSelectId), profile.GetLayer(ProfilePresets.ProfileSelectId));
        }

        var touch = document.Profiles[2];
        var sticks = document.Profiles[3];
        Assert.Equal(TouchpadMode.Trackpad, touch.Touchpad.Mode);
        Assert.Equal(StickMode.Off, touch.LeftStick.Mode);
        Assert.Equal(StickMode.Mouse, sticks.LeftStick.Mode);
        Assert.Equal(TouchpadMode.Off, sticks.Touchpad.Mode);
        Assert.Equal("DS4", ds4.BaseLayer.Name);
        Assert.Equal("base-ds4", ds4.BaseLayerId);
        Assert.Equal("XBOX", xbox.BaseLayer.Name);
        Assert.Equal("Touchpad", touch.BaseLayer.Name);
        Assert.Equal("Sticks", sticks.BaseLayer.Name);
        Assert.Contains(touch.Layers, layer => layer.Name == "Touchpad shift");
        Assert.Contains(sticks.Layers, layer => layer.Name == "Sticks precision");
        Assert.Equal(BindingKind.LayerHold, touch.BaseLayer.Get(ControlId.R1)!.Kind);
        Assert.Equal(BindingKind.LayerHold, sticks.BaseLayer.Get(ControlId.L1)!.Kind);

        var picker = touch.GetLayer(ProfilePresets.ProfileSelectId)!;
        Assert.Equal(ProfilePresets.Ds4Id, picker.Get(ControlId.Cross)!.ProfileId);
        Assert.Equal(ProfilePresets.XboxId, picker.Get(ControlId.Circle)!.ProfileId);
        Assert.Equal(ProfilePresets.TouchpadId, picker.Get(ControlId.Square)!.ProfileId);
        Assert.Equal(ProfilePresets.SticksId, picker.Get(ControlId.Triangle)!.ProfileId);
        Assert.Contains(document.Outputs, output => output.Kind == OutputDeviceKind.DualShock4 && output.Id == ds4.LeftStick.OutputId);
        Assert.Contains(document.Outputs, output => output.Kind == OutputDeviceKind.Xbox && output.Id == xbox.LeftStick.OutputId);
    }

    [Fact]
    public void Layers_StaySeparateUntilTheSameLayerIsApplied()
    {
        var first = ProfilePresets.MouseHand();
        var second = ProfilePresets.TriggerShift();
        var document = new ProfileDocument { Profiles = [first, second] };
        ProfileStore.Repair(document);
        Assert.NotSame(first.BaseLayer, second.BaseLayer);
        Assert.Equal(BindingKind.Mouse, first.BaseLayer.Get(ControlId.Cross)!.Kind);
        Assert.Equal(BindingKind.Key, second.BaseLayer.Get(ControlId.Cross)!.Kind);

        var aim = new Layer { Id = "aim", Name = "Aim" };
        aim.Set(ControlId.Cross, Binding.MouseClick(MouseButton.Right));
        document.LayerLibrary.Add(aim);
        first.Layers.Add(aim);
        second.Layers.Add(aim);
        aim.Set(ControlId.Circle, Binding.MouseClick(MouseButton.Left));
        Assert.Same(first.Layers.Last(), second.Layers.Last());
        Assert.Equal(BindingKind.Mouse, second.GetLayer("aim")!.Get(ControlId.Circle)!.Kind);
    }
}
