using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

public class ConfigWarningTests
{
    [Fact]
    public void RemovedOutput_IsReportedAndANormalClickStaysSilent()
    {
        var document = new ProfileDocument { Profiles = [ProfilePresets.MouseHand()] };
        ProfileStore.Repair(document);
        var profile = document.Profiles[0];
        var cross = profile.BaseLayer.Get(ControlId.Cross);
        Assert.Null(ConfigWarnings.ForBinding(document, profile, cross));
        var quiet = ConfigWarnings.Collect(document, []);
        Assert.DoesNotContain(quiet, warning => warning.Message.Contains("never releases"));
        Assert.DoesNotContain(quiet, warning => warning.Message.Contains("Cross"));

        var broken = ProfilePresets.Blank();
        broken.Name = "Trigger shift";
        var pad = Binding.Pad(ControlId.Circle);
        pad.OutputId = "gone";
        broken.BaseLayer.Set(ControlId.Cross, pad);
        document.Profiles.Add(broken);
        var warnings = ConfigWarnings.Collect(document, []);
        Assert.Contains(warnings, warning => warning.Message.Contains("Trigger shift") && warning.Message.Contains("removed"));
    }

    [Fact]
    public void UnbalancedPress_IsReported()
    {
        var profile = ProfilePresets.Blank();
        profile.Name = "Trigger shift";
        profile.BaseLayer.Set(ControlId.Cross, new Binding
        {
            Kind = BindingKind.None,
            Advanced = true,
            PressSteps = [new EdgeStep { Kind = EdgeKind.KeyDown, Key = KeyCatalog.LShift }],
        });
        var document = new ProfileDocument { Profiles = [profile], ActiveId = profile.Id };
        var warnings = ConfigWarnings.Collect(document, []);
        Assert.Contains(warnings, warning => warning.Message.Contains("Cross") && warning.Message.Contains("Left Shift") && warning.Message.Contains("never releases"));
        Assert.Contains("Presses Left Shift", ConfigWarnings.ForBinding(document, profile, profile.BaseLayer.Get(ControlId.Cross)));
    }

    [Fact]
    public void SharedPadButton_IsReported()
    {
        var hand = ProfilePresets.Blank();
        hand.Name = "Mouse hand";
        var shift = ProfilePresets.Blank();
        shift.Name = "Trigger shift";
        var document = new ProfileDocument { ActiveId = hand.Id, Profiles = [hand, shift] };
        Assert.True(OutputCatalog.TryAdd(document.Outputs, OutputDeviceKind.Xbox));
        var xbox = document.Outputs[0];
        hand.BaseLayer.Set(ControlId.Cross, PadOn(xbox.Id));
        shift.BaseLayer.Set(ControlId.Cross, PadOn(xbox.Id));
        document.Assign("controller-a", hand.Id);
        document.Assign("controller-b", shift.Id);
        var warnings = ConfigWarnings.Collect(document, ["controller-a", "controller-b"]);
        Assert.Contains(warnings, warning =>
            warning.OutputId == xbox.Id
            && warning.Message.Contains("Mouse hand")
            && warning.Message.Contains("Trigger shift")
            && warning.Message.Contains("Cross")
            && warning.Message.Contains("XBOX 1"));
    }

    private static Binding PadOn(string outputId)
    {
        var binding = Binding.Pad(ControlId.Cross);
        binding.OutputId = outputId;
        return binding;
    }
}
