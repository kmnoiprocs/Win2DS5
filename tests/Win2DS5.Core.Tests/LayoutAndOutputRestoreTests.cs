using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

public class LayoutAndOutputRestoreTests
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
        profile.BaseLayout.Set(ControlId.Cross, binding);
        var document = new ProfileDocument { Profiles = [profile] };
        ProfileStore.Repair(document);
        var missing = OutputRestore.Find(profile, document.Outputs);
        Assert.Contains(missing, item => item.OldId == "old-ds4" && item.Kind == OutputDeviceKind.DualShock4 && item.Slot == 2);
        Assert.Empty(OutputRestore.Restore(document, profile, missing));
        var created = Assert.Single(document.Outputs, output => output.Kind == OutputDeviceKind.DualShock4);
        Assert.Equal(created.Id, profile.BaseLayout.Get(ControlId.Cross)!.OutputId);
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
        Assert.Equal(BindingKind.Pad, ds4.BaseLayout.Get(ControlId.Cross)!.Kind);
        Assert.Equal(ControlId.Cross, ds4.BaseLayout.Get(ControlId.Cross)!.PadButton);
        Assert.Equal(OutputDeviceKind.Xbox, xbox.BaseLayout.Get(ControlId.Circle)!.OutputKind);
        Assert.Single(ds4.Layouts, layout => layout.Name != "Profile Select");
        Assert.Single(xbox.Layouts, layout => layout.Name != "Profile Select");

        foreach (var profile in document.Profiles)
        {
            Assert.Equal(BindingKind.LayoutHold, profile.BaseLayout.Get(ControlId.Mute)!.Kind);
            Assert.Equal(ProfilePresets.ProfileSelectId, profile.BaseLayout.Get(ControlId.Mute)!.LayoutId);
            Assert.Same(document.Profiles[0].GetLayout(ProfilePresets.ProfileSelectId), profile.GetLayout(ProfilePresets.ProfileSelectId));
        }

        var touch = document.Profiles[2];
        var sticks = document.Profiles[3];
        Assert.Equal(TouchpadMode.Trackpad, touch.Touchpad.Mode);
        Assert.Equal(StickMode.Off, touch.LeftStick.Mode);
        Assert.Equal(StickMode.Mouse, sticks.LeftStick.Mode);
        Assert.Equal(TouchpadMode.Off, sticks.Touchpad.Mode);
        Assert.Equal("DS4", ds4.BaseLayout.Name);
        Assert.Equal("base-ds4", ds4.BaseLayoutId);
        Assert.Equal("XBOX", xbox.BaseLayout.Name);
        Assert.Equal("Touchpad", touch.BaseLayout.Name);
        Assert.Equal("Sticks", sticks.BaseLayout.Name);
        Assert.Contains(touch.Layouts, layout => layout.Name == "Touchpad shift");
        Assert.Contains(sticks.Layouts, layout => layout.Name == "Sticks precision");
        Assert.Equal(BindingKind.LayoutHold, touch.BaseLayout.Get(ControlId.R1)!.Kind);
        Assert.Equal(BindingKind.LayoutHold, sticks.BaseLayout.Get(ControlId.L1)!.Kind);

        var picker = touch.GetLayout(ProfilePresets.ProfileSelectId)!;
        Assert.Equal(ProfilePresets.Ds4Id, picker.Get(ControlId.Cross)!.ProfileId);
        Assert.Equal(ProfilePresets.XboxId, picker.Get(ControlId.Circle)!.ProfileId);
        Assert.Equal(ProfilePresets.TouchpadId, picker.Get(ControlId.Square)!.ProfileId);
        Assert.Equal(ProfilePresets.SticksId, picker.Get(ControlId.Triangle)!.ProfileId);
        Assert.Contains(document.Outputs, output => output.Kind == OutputDeviceKind.DualShock4 && output.Id == ds4.LeftStick.OutputId);
        Assert.Contains(document.Outputs, output => output.Kind == OutputDeviceKind.Xbox && output.Id == xbox.LeftStick.OutputId);
    }

    [Fact]
    public void Layouts_StaySeparateUntilTheSameLayoutIsApplied()
    {
        var first = ProfilePresets.MouseHand();
        var second = ProfilePresets.TriggerShift();
        var document = new ProfileDocument { Profiles = [first, second] };
        ProfileStore.Repair(document);
        Assert.NotSame(first.BaseLayout, second.BaseLayout);
        Assert.Equal(BindingKind.Mouse, first.BaseLayout.Get(ControlId.Cross)!.Kind);
        Assert.Equal(BindingKind.Key, second.BaseLayout.Get(ControlId.Cross)!.Kind);

        var aim = new Layout { Id = "aim", Name = "Aim" };
        aim.Set(ControlId.Cross, Binding.MouseClick(MouseButton.Right));
        document.LayoutLibrary.Add(aim);
        first.Layouts.Add(aim);
        second.Layouts.Add(aim);
        aim.Set(ControlId.Circle, Binding.MouseClick(MouseButton.Left));
        Assert.Same(first.Layouts.Last(), second.Layouts.Last());
        Assert.Equal(BindingKind.Mouse, second.GetLayout("aim")!.Get(ControlId.Circle)!.Kind);
    }

    [Fact]
    public void ShownLayouts_AreTheBaseAndModifierTargets()
    {
        var document = ProfileStore.CreateDefault();
        var touch = document.Profiles.First(profile => profile.Id == ProfilePresets.TouchpadId);
        var shown = touch.ShownLayouts().Select(layout => layout.Name).ToList();
        Assert.Equal("Touchpad", shown[0]);
        Assert.Contains("Touchpad shift", shown);
        Assert.Contains("Touchpad precision", shown);
        Assert.Contains("Profile Select", shown);

        var spare = new Layout { Id = "spare", Name = "Spare" };
        touch.Layouts.Add(spare);
        Assert.DoesNotContain(touch.ShownLayouts(), layout => layout.Id == "spare");
        Assert.Contains(touch.ShownLayouts(["spare"]), layout => layout.Id == "spare");

        touch.UseBase(spare);
        Assert.Equal("spare", touch.BaseLayoutId);
        Assert.Equal("Spare", touch.ShownLayouts()[0].Name);
    }

    [Fact]
    public void ResetToStock_ReplacesSavedProfilesAndSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "win2ds5-reset-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var dirty = ProfileStore.CreateDefault();
            dirty.Settings.Theme = "Light";
            dirty.Settings.CheckForUpdates = false;
            dirty.Profiles.Clear();
            dirty.Profiles.Add(ProfilePresets.Blank());
            ProfileStore.Save(path, dirty);
            var restored = ProfileStore.ResetToStock(path);
            Assert.Equal(["DS4", "XBOX", "Touchpad", "Sticks"], restored.Profiles.Select(profile => profile.Name));
            Assert.Equal("Dark", restored.Settings.Theme);
            Assert.True(restored.Settings.CheckForUpdates);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
