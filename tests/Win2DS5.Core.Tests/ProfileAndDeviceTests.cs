using System.Runtime.InteropServices;
using System.Text.Json;
using Win2DS5.Core;
using Win2DS5.DualSense;
using Win2DS5.Platform;

namespace Win2DS5.Core.Tests;

public class ProfileAndDeviceTests
{
    [Fact]
    public void Profile_RoundTripsTheShiftBinding()
    {
        var document = ProfileStore.CreateDefault();
        var json = JsonSerializer.Serialize(document, ProfileStore.Options);
        var loaded = JsonSerializer.Deserialize<ProfileDocument>(json, ProfileStore.Options);
        Assert.NotNull(loaded);
        ProfileStore.Repair(loaded);
        var shift = loaded.Active.GetLayer("shift");
        Assert.NotNull(shift);
        Assert.Equal(BindingKind.Key, shift.Get(ControlId.Cross)?.Kind);
        Assert.Equal("Mouse hand", loaded.Active.Name);
        Assert.Contains("Mute", loaded.Active.ToggleChord);
        Assert.Contains("Create", loaded.Active.ToggleChord);
    }

    [Fact]
    public void OutputReport_PlacesLightbarAndSignsBluetooth()
    {
        var usb = DualSenseOutput.BuildUsb(9, 8, 1, 2, 3, true, false);
        Assert.Equal(63, usb.Length);
        Assert.Equal(0x02, usb[0]);
        Assert.Equal(9, usb[3]);
        Assert.Equal(8, usb[4]);
        Assert.Equal(1, usb[45]);
        Assert.Equal(2, usb[46]);
        Assert.Equal(3, usb[47]);

        byte sequence = 1;
        var first = DualSenseOutput.BuildBluetooth(ref sequence, 0, 0, 10, 20, 30, true, false);
        sequence = 1;
        var second = DualSenseOutput.BuildBluetooth(ref sequence, 0, 0, 11, 20, 30, true, false);
        Assert.Equal(78, first.Length);
        Assert.Equal(0x31, first[0]);
        Assert.Equal(0x10, first[2]);
        Assert.NotEqual(first.AsSpan(74, 4).ToArray(), second.AsSpan(74, 4).ToArray());
        Assert.False(first.Skip(74).All(value => value == 0));
    }

    [Fact]
    public void HidHideCodes_MatchTheDriverContract()
    {
        Assert.Equal(0x80016020u, HidHideIoctl.Code(HidHideIoctl.AddSession));
        Assert.Equal(0x80016024u, HidHideIoctl.Code(HidHideIoctl.ClearSession));
    }

    [Fact]
    public void DevicePath_BecomesAnInstanceId()
    {
        var id = DevicePaths.ToInstanceId(@"\\?\hid#vid_054c&pid_0ce6&mi_03#7&1234&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        Assert.Equal(@"HID\VID_054C&PID_0CE6&MI_03\7&1234&0&0000", id);
    }

    [Fact]
    public void NtPath_UsesTheVolumeDevice()
    {
        var path = DevicePaths.ToNtPath(Environment.ProcessPath ?? Environment.SystemDirectory);
        Assert.StartsWith(@"\Device\", path);
    }

    [Fact]
    public void SendInputStruct_IsTheNativeSize()
    {
        if (nint.Size != 8)
            return;
        Assert.Equal(40, Marshal.SizeOf<INPUT>());
    }
}
