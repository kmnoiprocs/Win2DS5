using Win2DS5.DualSense;

namespace Win2DS5.Core.Tests;

public class ParserTests
{
    [Fact]
    public void UsbReport_ReadsFaceButtonMuteTouchAndBattery()
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[1] = 10;
        report[2] = 20;
        report[8] = 0x20;
        report[10] = 0x04;
        report[53] = 0x18;
        WriteTouch(report, 33, 0x123, 0x456, active: true);

        Assert.True(DualSenseParser.TryParse(report, false, out var frame));
        Assert.True(frame.Cross);
        Assert.False(frame.Square);
        Assert.True(frame.Mute);
        Assert.Equal(10, frame.LeftX);
        Assert.Equal(20, frame.LeftY);
        Assert.True(frame.Touch0.Active);
        Assert.Equal(0x123, frame.Touch0.X);
        Assert.Equal(0x456, frame.Touch0.Y);
        Assert.True(frame.Charging);
        Assert.Equal(85, frame.BatteryPercent);
        Assert.True(frame.FullReport);
        Assert.False(frame.Bluetooth);
    }

    [Fact]
    public void BluetoothReport_ShiftsPayloadByOne()
    {
        var report = new byte[78];
        report[0] = 0x31;
        report[2] = 200;
        report[9] = 0x10;
        report[11] = 0x01;
        report[17] = 0x00;
        report[18] = 0x04;

        Assert.True(DualSenseParser.TryParse(report, true, out var frame));
        Assert.True(frame.Bluetooth);
        Assert.True(frame.IsEdge);
        Assert.Equal(200, frame.LeftX);
        Assert.True(frame.Square);
        Assert.True(frame.Ps);
        Assert.Equal(1024, frame.GyroX);
    }

    [Fact]
    public void ReducedBluetoothReport_ReadsButtonsWithoutTouch()
    {
        var report = new byte[10];
        report[0] = 0x01;
        report[1] = 128;
        report[5] = 0x80;
        report[6] = 0x10;
        report[9] = 40;

        Assert.True(DualSenseParser.TryParse(report, false, out var frame));
        Assert.False(frame.FullReport);
        Assert.True(frame.Triangle);
        Assert.True(frame.Create);
        Assert.Equal(40, frame.R2);
        Assert.False(frame.Touch0.Active);
    }

    [Fact]
    public void InactiveTouch_ClearsTheContact()
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[33] = 0x80;
        Assert.True(DualSenseParser.TryParse(report, false, out var frame));
        Assert.False(frame.Touch0.Active);
    }

    private static void WriteTouch(byte[] report, int offset, int x, int y, bool active)
    {
        report[offset] = active ? (byte)0x00 : (byte)0x80;
        report[offset + 1] = (byte)(x & 0xFF);
        report[offset + 2] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        report[offset + 3] = (byte)((y >> 4) & 0xFF);
    }
}
