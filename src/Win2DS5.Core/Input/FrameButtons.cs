namespace Win2DS5.Core;

public static class FrameButtons
{
    public const int SoftThreshold = 30;
    public const int FullThreshold = 230;

    public static void Collect(in ControllerFrame frame, HashSet<ControlId> into)
    {
        into.Clear();
        switch (frame.Hat)
        {
            case 0: into.Add(ControlId.DPadUp); break;
            case 1:
                into.Add(ControlId.DPadUp);
                into.Add(ControlId.DPadRight);
                break;
            case 2: into.Add(ControlId.DPadRight); break;
            case 3:
                into.Add(ControlId.DPadDown);
                into.Add(ControlId.DPadRight);
                break;
            case 4: into.Add(ControlId.DPadDown); break;
            case 5:
                into.Add(ControlId.DPadDown);
                into.Add(ControlId.DPadLeft);
                break;
            case 6: into.Add(ControlId.DPadLeft); break;
            case 7:
                into.Add(ControlId.DPadUp);
                into.Add(ControlId.DPadLeft);
                break;
        }

        if (frame.Square) into.Add(ControlId.Square);
        if (frame.Cross) into.Add(ControlId.Cross);
        if (frame.Circle) into.Add(ControlId.Circle);
        if (frame.Triangle) into.Add(ControlId.Triangle);
        if (frame.L1) into.Add(ControlId.L1);
        if (frame.R1) into.Add(ControlId.R1);
        if (frame.Create) into.Add(ControlId.Create);
        if (frame.Options) into.Add(ControlId.Options);
        if (frame.L3) into.Add(ControlId.L3);
        if (frame.R3) into.Add(ControlId.R3);
        if (frame.Ps) into.Add(ControlId.Ps);
        if (frame.TouchpadClick) into.Add(ControlId.TouchpadClick);
        if (frame.Mute) into.Add(ControlId.Mute);
        if (frame.L2 >= SoftThreshold || frame.L2Button) into.Add(ControlId.L2);
        if (frame.L2 >= FullThreshold) into.Add(ControlId.L2Full);
        if (frame.R2 >= SoftThreshold || frame.R2Button) into.Add(ControlId.R2);
        if (frame.R2 >= FullThreshold) into.Add(ControlId.R2Full);
    }
}
