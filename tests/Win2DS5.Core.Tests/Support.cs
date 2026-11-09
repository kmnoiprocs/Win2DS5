using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

internal static class Pad
{
    public static ControllerFrame Hold(params ControlId[] controls)
    {
        var frame = ControllerFrame.Neutral();
        bool up = controls.Contains(ControlId.DPadUp);
        bool down = controls.Contains(ControlId.DPadDown);
        bool left = controls.Contains(ControlId.DPadLeft);
        bool right = controls.Contains(ControlId.DPadRight);
        frame.Hat = (up, down, left, right) switch
        {
            (true, false, false, true) => 1,
            (false, false, false, true) => 2,
            (false, true, false, true) => 3,
            (false, true, false, false) => 4,
            (false, true, true, false) => 5,
            (false, false, true, false) => 6,
            (true, false, true, false) => 7,
            (true, false, false, false) => 0,
            _ => 8,
        };
        frame.Square = controls.Contains(ControlId.Square);
        frame.Cross = controls.Contains(ControlId.Cross);
        frame.Circle = controls.Contains(ControlId.Circle);
        frame.Triangle = controls.Contains(ControlId.Triangle);
        frame.L1 = controls.Contains(ControlId.L1);
        frame.R1 = controls.Contains(ControlId.R1);
        frame.Create = controls.Contains(ControlId.Create);
        frame.Options = controls.Contains(ControlId.Options);
        frame.L3 = controls.Contains(ControlId.L3);
        frame.R3 = controls.Contains(ControlId.R3);
        frame.Ps = controls.Contains(ControlId.Ps);
        frame.TouchpadClick = controls.Contains(ControlId.TouchpadClick);
        frame.Mute = controls.Contains(ControlId.Mute);
        if (controls.Contains(ControlId.L2Full)) frame.L2 = 250;
        else if (controls.Contains(ControlId.L2)) frame.L2 = 80;
        if (controls.Contains(ControlId.R2Full)) frame.R2 = 250;
        else if (controls.Contains(ControlId.R2)) frame.R2 = 80;
        return frame;
    }

    public static bool Has(MappingResult result, OutputKind kind, int a) =>
        result.Commands.Any(command => command.Kind == kind && command.A == a);
}
