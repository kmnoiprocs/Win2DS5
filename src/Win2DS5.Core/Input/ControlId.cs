namespace Win2DS5.Core;

public enum ControlId
{
    None = 0,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    Square,
    Cross,
    Circle,
    Triangle,
    L1,
    R1,
    L2,
    R2,
    L2Full,
    R2Full,
    L3,
    R3,
    Create,
    Options,
    Ps,
    TouchpadClick,
    Mute,
}

public static class ControlNames
{
    public static string Display(ControlId id) => id switch
    {
        ControlId.DPadUp => "D-Pad Up",
        ControlId.DPadDown => "D-Pad Down",
        ControlId.DPadLeft => "D-Pad Left",
        ControlId.DPadRight => "D-Pad Right",
        ControlId.L2Full => "L2 Full Press",
        ControlId.R2Full => "R2 Full Press",
        ControlId.Ps => "PS",
        ControlId.TouchpadClick => "Touchpad Click",
        _ => id.ToString(),
    };

    public static string PadLabel(ControlId id) => id switch
    {
        ControlId.Create => "Share",
        ControlId.Ps => "PS",
        ControlId.TouchpadClick => "Touchpad Click",
        ControlId.DPadUp => "D-Pad Up",
        ControlId.DPadDown => "D-Pad Down",
        ControlId.DPadLeft => "D-Pad Left",
        ControlId.DPadRight => "D-Pad Right",
        _ => Display(id),
    };

    public static IReadOnlyList<ControlId> Bindable { get; } = Enum.GetValues<ControlId>()
        .Where(id => id != ControlId.None)
        .ToArray();

    public static IReadOnlyList<ControlId> OutputButtons { get; } =
    [
        ControlId.Cross,
        ControlId.Circle,
        ControlId.Square,
        ControlId.Triangle,
        ControlId.DPadUp,
        ControlId.DPadDown,
        ControlId.DPadLeft,
        ControlId.DPadRight,
        ControlId.L1,
        ControlId.R1,
        ControlId.L2,
        ControlId.R2,
        ControlId.L3,
        ControlId.R3,
        ControlId.Create,
        ControlId.Options,
        ControlId.Ps,
        ControlId.TouchpadClick,
        ControlId.Mute,
    ];
}
