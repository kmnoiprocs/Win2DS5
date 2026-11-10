using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

public class OutputCatalogTests
{
    [Fact]
    public void Limits_CapMouseKeyboardAndVirtualPads()
    {
        var outputs = new List<OutputDevice>();
        Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Mouse));
        Assert.False(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Mouse));
        Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Keyboard));
        Assert.False(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Keyboard));

        for (int i = 0; i < 8; i++)
            Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.DualShock4));
        Assert.False(OutputCatalog.TryAdd(outputs, OutputDeviceKind.DualShock4));
        Assert.False(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Xbox));
        Assert.Equal(Enumerable.Range(1, 8), outputs.Where(output => output.Kind == OutputDeviceKind.DualShock4).Select(output => output.Slot));

        outputs.RemoveAll(output => output.Kind == OutputDeviceKind.DualShock4);
        for (int i = 0; i < 4; i++)
            Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Xbox));
        Assert.False(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Xbox));
        for (int i = 0; i < 4; i++)
            Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.DualShock4));
        Assert.False(OutputCatalog.TryAdd(outputs, OutputDeviceKind.DualShock4));
        Assert.False(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Xbox));
        Assert.Equal(new[] { 1, 2, 3, 4 }, outputs.Where(output => output.Kind == OutputDeviceKind.Xbox).Select(output => output.Slot));
        Assert.Equal(8, OutputCatalog.PadCount(outputs));
    }

    [Fact]
    public void Pads_ConnectInSlotOrderAndANewPadDoesNotInheritARemovedId()
    {
        var outputs = new List<OutputDevice>();
        Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.Xbox));
        Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.DualShock4));
        Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.DualShock4));
        var removed = outputs.First(output => output.Kind == OutputDeviceKind.DualShock4);
        outputs.Remove(removed);
        Assert.True(OutputCatalog.TryAdd(outputs, OutputDeviceKind.DualShock4));
        var added = outputs.Last();
        Assert.NotEqual(removed.Id, added.Id);
        Assert.Null(OutputCatalog.PadTarget(new OutputCommand(OutputKind.PadDown, (int)ControlId.Cross, 0, removed.Id), outputs));
        Assert.Null(OutputCatalog.PadTarget(new OutputCommand(OutputKind.PadDown, (int)ControlId.Cross, 0, ""), outputs));
        Assert.Equal(added.Id, OutputCatalog.PadTarget(new OutputCommand(OutputKind.PadDown, (int)ControlId.Cross, 0, added.Id), outputs));
        Assert.Equal(["DS4 1", "DS4 2", "XBOX 1"], OutputCatalog.PadsInSlotOrder(outputs).Select(output => output.Name));
    }

    [Fact]
    public void Repair_LeavesARemovedPadBindingUnset()
    {
        var document = new ProfileDocument { Profiles = [ProfilePresets.Blank()] };
        ProfileStore.Repair(document);
        var binding = Binding.Pad(ControlId.Circle);
        binding.OutputId = "missing-pad";
        document.Profiles[0].BaseLayer.Set(ControlId.Cross, binding);
        ProfileStore.Repair(document);
        Assert.Equal("missing-pad", document.Profiles[0].BaseLayer.Get(ControlId.Cross)?.OutputId);
        Assert.Null(OutputCatalog.PadTarget(new OutputCommand(OutputKind.PadDown, (int)ControlId.Circle, 0, "missing-pad"), document.Outputs));
    }

    [Fact]
    public void Repair_AddsMouseAndKeyboardAndRemembersWhereAButtonGoes()
    {
        var document = new ProfileDocument { Profiles = [ProfilePresets.MouseHand()] };
        ProfileStore.Repair(document);
        Assert.Contains(document.Outputs, output => output.Kind == OutputDeviceKind.Mouse);
        Assert.Contains(document.Outputs, output => output.Kind == OutputDeviceKind.Keyboard);
        var cross = document.Profiles[0].BaseLayer.Get(ControlId.Cross);
        Assert.Equal(BindingKind.Mouse, cross?.Kind);
        Assert.False(string.IsNullOrEmpty(cross?.OutputId));
    }

    [Fact]
    public void Profiles_AreSharedAndAssignedPerController()
    {
        var first = ProfilePresets.MouseHand();
        var second = ProfilePresets.TriggerShift();
        var document = new ProfileDocument
        {
            ActiveId = first.Id,
            Profiles = [first, second],
        };
        Assert.Equal(first.Id, document.ProfileFor("pad-a").Id);
        document.Assign("pad-a", second.Id);
        Assert.Equal(second.Id, document.ProfileFor("pad-a").Id);
        Assert.Equal(first.Id, document.ProfileFor("pad-b").Id);
        document.Assign("pad-b", second.Id);
        Assert.Equal(second.Id, document.ProfileFor("pad-a").Id);
        Assert.Equal(second.Id, document.ProfileFor("pad-b").Id);
    }
}
