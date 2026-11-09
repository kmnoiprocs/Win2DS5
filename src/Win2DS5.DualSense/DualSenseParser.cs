using Win2DS5.Core;

namespace Win2DS5.DualSense;

public static class DualSenseIds
{
    public const int Vendor = 0x054C;
    public const int DualSense = 0x0CE6;
    public const int Edge = 0x0DF2;
    public const int TouchWidth = 1920;
    public const int TouchHeight = 1080;
}

public static class DualSenseParser
{
    public static bool TryParse(ReadOnlySpan<byte> report, bool edge, out ControllerFrame frame)
    {
        frame = ControllerFrame.Neutral();
        frame.IsEdge = edge;
        if (report.Length < 10)
            return false;

        if (report[0] == 0x01 && report.Length < 48)
            return TryParseReduced(report, edge, out frame);

        int origin;
        if (report[0] == 0x01)
            origin = 1;
        else if (report[0] == 0x31)
        {
            if (report.Length < 12)
                return false;
            origin = 2;
            frame.Bluetooth = true;
        }
        else
        {
            return false;
        }

        if (report.Length <= origin + 9)
            return false;

        frame.FullReport = report.Length >= origin + 48;
        frame.LeftX = report[origin];
        frame.LeftY = report[origin + 1];
        frame.RightX = report[origin + 2];
        frame.RightY = report[origin + 3];
        frame.L2 = report[origin + 4];
        frame.R2 = report[origin + 5];

        int buttons = origin + 7;
        if (report.Length <= buttons + 2)
            return false;

        byte face = report[buttons];
        byte shoulders = report[buttons + 1];
        byte system = report[buttons + 2];
        frame.Hat = (byte)(face & 0x0F);
        frame.Square = (face & 0x10) != 0;
        frame.Cross = (face & 0x20) != 0;
        frame.Circle = (face & 0x40) != 0;
        frame.Triangle = (face & 0x80) != 0;
        frame.L1 = (shoulders & 0x01) != 0;
        frame.R1 = (shoulders & 0x02) != 0;
        frame.L2Button = (shoulders & 0x04) != 0;
        frame.R2Button = (shoulders & 0x08) != 0;
        frame.Create = (shoulders & 0x10) != 0;
        frame.Options = (shoulders & 0x20) != 0;
        frame.L3 = (shoulders & 0x40) != 0;
        frame.R3 = (shoulders & 0x80) != 0;
        frame.Ps = (system & 0x01) != 0;
        frame.TouchpadClick = (system & 0x02) != 0;
        frame.Mute = (system & 0x04) != 0;

        int gyro = origin + 15;
        if (report.Length >= gyro + 12)
        {
            frame.GyroX = ReadInt16(report, gyro);
            frame.GyroY = ReadInt16(report, gyro + 2);
            frame.GyroZ = ReadInt16(report, gyro + 4);
            frame.AccelX = ReadInt16(report, gyro + 6);
            frame.AccelY = ReadInt16(report, gyro + 8);
            frame.AccelZ = ReadInt16(report, gyro + 10);
        }

        int touch = origin + 32;
        if (report.Length >= touch + 8)
        {
            frame.Touch0 = ReadTouch(report, touch);
            frame.Touch1 = ReadTouch(report, touch + 4);
        }

        int status = origin + 52;
        if (report.Length > status)
            ApplyBattery(report[status], ref frame);

        return true;
    }

    public static TouchContact ReadTouch(ReadOnlySpan<byte> report, int offset)
    {
        byte contact = report[offset];
        int x = ((report[offset + 2] & 0x0F) << 8) | report[offset + 1];
        int y = (report[offset + 3] << 4) | ((report[offset + 2] & 0xF0) >> 4);
        return new TouchContact
        {
            Active = (contact & 0x80) == 0,
            Id = contact & 0x7F,
            X = x,
            Y = y,
        };
    }

    private static bool TryParseReduced(ReadOnlySpan<byte> report, bool edge, out ControllerFrame frame)
    {
        frame = ControllerFrame.Neutral();
        frame.IsEdge = edge;
        frame.FullReport = false;
        frame.Bluetooth = true;
        frame.LeftX = report[1];
        frame.LeftY = report[2];
        frame.RightX = report[3];
        frame.RightY = report[4];
        if (report.Length > 5)
        {
            byte face = report[5];
            frame.Hat = (byte)(face & 0x0F);
            frame.Square = (face & 0x10) != 0;
            frame.Cross = (face & 0x20) != 0;
            frame.Circle = (face & 0x40) != 0;
            frame.Triangle = (face & 0x80) != 0;
        }

        if (report.Length > 6)
        {
            byte shoulders = report[6];
            frame.L1 = (shoulders & 0x01) != 0;
            frame.R1 = (shoulders & 0x02) != 0;
            frame.L2Button = (shoulders & 0x04) != 0;
            frame.R2Button = (shoulders & 0x08) != 0;
            frame.Create = (shoulders & 0x10) != 0;
            frame.Options = (shoulders & 0x20) != 0;
            frame.L3 = (shoulders & 0x40) != 0;
            frame.R3 = (shoulders & 0x80) != 0;
        }

        if (report.Length > 7)
        {
            frame.Ps = (report[7] & 0x01) != 0;
            frame.TouchpadClick = (report[7] & 0x02) != 0;
            frame.Mute = (report[7] & 0x04) != 0;
        }

        if (report.Length > 9)
        {
            frame.L2 = report[8];
            frame.R2 = report[9];
        }

        return true;
    }

    private static void ApplyBattery(byte status, ref ControllerFrame frame)
    {
        int level = status & 0x0F;
        int charging = (status >> 4) & 0x0F;
        frame.Charging = charging is 1 or 2;
        frame.BatteryPercent = charging switch
        {
            2 => 100,
            0 or 1 => Math.Min((level * 10) + 5, 100),
            _ => 0,
        };
    }

    private static short ReadInt16(ReadOnlySpan<byte> report, int offset) =>
        (short)(report[offset] | (report[offset + 1] << 8));
}
