namespace Win2DS5.Core;

public struct TouchContact
{
    public bool Active;
    public int Id;
    public int X;
    public int Y;
}

public struct ControllerFrame
{
    public byte LeftX;
    public byte LeftY;
    public byte RightX;
    public byte RightY;
    public byte L2;
    public byte R2;
    public byte Hat;
    public bool Square;
    public bool Cross;
    public bool Circle;
    public bool Triangle;
    public bool L1;
    public bool R1;
    public bool L2Button;
    public bool R2Button;
    public bool Create;
    public bool Options;
    public bool L3;
    public bool R3;
    public bool Ps;
    public bool TouchpadClick;
    public bool Mute;
    public short GyroX;
    public short GyroY;
    public short GyroZ;
    public short AccelX;
    public short AccelY;
    public short AccelZ;
    public TouchContact Touch0;
    public TouchContact Touch1;
    public int BatteryPercent;
    public bool Charging;
    public bool FullReport;
    public bool IsEdge;
    public bool Bluetooth;

    public static ControllerFrame Neutral() => new()
    {
        LeftX = 128,
        LeftY = 128,
        RightX = 128,
        RightY = 128,
        Hat = 8,
        FullReport = true,
    };
}
