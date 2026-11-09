using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Win2DS5.Platform;

public static class DevicePaths
{
    public static string ToInstanceId(string devicePath)
    {
        var path = devicePath.Trim();
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            path = path[4..];
        int brace = path.IndexOf('{');
        if (brace >= 0)
            path = path[..brace];
        path = path.TrimEnd('#').Replace('#', '\\');
        return path.ToUpperInvariant();
    }

    public static string ToNtPath(string win32Path)
    {
        var root = Path.GetPathRoot(win32Path);
        if (string.IsNullOrEmpty(root))
            return win32Path;
        var drive = root.TrimEnd('\\');
        var buffer = new StringBuilder(1024);
        if (QueryDosDevice(drive, buffer, buffer.Capacity) == 0)
            return win32Path;
        var device = buffer.ToString();
        int end = device.IndexOf('\0');
        if (end >= 0)
            device = device[..end];
        var rest = win32Path[root.Length..].TrimStart('\\');
        return string.IsNullOrEmpty(rest) ? device : device + "\\" + rest;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint QueryDosDevice(string name, StringBuilder path, int capacity);
}

public static class DualSenseDevices
{
    private static readonly Guid HidGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    public static IReadOnlyList<string> InstanceIds()
    {
        var found = new List<string>();
        var hidGuid = HidGuid;
        var handle = SetupApi.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return found;

        try
        {
            var interfaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint index = 0; SetupApi.SetupDiEnumDeviceInterfaces(handle, IntPtr.Zero, ref hidGuid, index, ref interfaceData); index++)
            {
                var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                var detail = new SP_DEVICE_INTERFACE_DETAIL_DATA
                {
                    cbSize = IntPtr.Size == 8 ? 8 : 6,
                    DevicePath = "",
                };
                if (!SetupApi.SetupDiGetDeviceInterfaceDetail(handle, ref interfaceData, ref detail, Marshal.SizeOf(detail), out _, ref info))
                    continue;

                var id = new StringBuilder(512);
                if (!SetupApi.SetupDiGetDeviceInstanceId(handle, ref info, id, id.Capacity, out _))
                    continue;
                var text = id.ToString().ToUpperInvariant();
                if (!text.Contains("VID_054C", StringComparison.Ordinal))
                    continue;
                if (!text.Contains("PID_0CE6", StringComparison.Ordinal) && !text.Contains("PID_0DF2", StringComparison.Ordinal))
                    continue;
                if (!found.Contains(text, StringComparer.OrdinalIgnoreCase))
                    found.Add(text);
            }
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(handle);
        }

        return found;
    }
}

public static class DeviceCycler
{
    public static bool TryCycle(IReadOnlyList<string> instanceIds, out string error, out bool restarted)
    {
        error = "";
        restarted = false;
        if (instanceIds.Count == 0)
        {
            error = "There was no controller interface to restart.";
            return false;
        }

        bool any = false;
        bool blocked = false;
        foreach (var id in instanceIds)
        {
            if (CM_Locate_DevNode(out uint node, id, 0) != 0)
                continue;
            int disabled = CM_Disable_DevNode(node, 0);
            if (disabled != 0)
            {
                blocked = true;
                continue;
            }

            Thread.Sleep(250);
            CM_Enable_DevNode(node, 0);
            any = true;
        }

        if (any)
        {
            restarted = true;
            Thread.Sleep(400);
            return true;
        }

        // Restarting needs administrator rights. Once HidHide is already on the device,
        // the hide list applies to every new open, so a failed restart is not a problem.
        if (blocked && HidHideIsFiltering(instanceIds))
            return true;

        if (blocked)
            error = "Unplug the controller and plug it back in once so games stop seeing it.";
        return false;
    }

    private static bool HidHideIsFiltering(IReadOnlyList<string> instanceIds)
    {
        foreach (var id in instanceIds)
        {
            if (CM_Locate_DevNode(out uint node, id, 0) != 0)
                continue;
            if (StackContainsHidHide(node))
                return true;
        }

        return false;
    }

    private static bool StackContainsHidHide(uint node)
    {
        var key = DeviceStack;
        var buffer = new byte[4096];
        int size = buffer.Length;
        if (CM_Get_DevNode_Property(node, ref key, out _, buffer, ref size, 0) != 0 || size <= 2)
            return false;
        var text = Encoding.Unicode.GetString(buffer, 0, size);
        return text.Contains("HidHide", StringComparison.OrdinalIgnoreCase);
    }

    private static DevPropKey DeviceStack { get; } = new(
        new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"),
        14);

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint device, string instanceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Property(
        uint device,
        ref DevPropKey key,
        out uint propertyType,
        byte[] buffer,
        ref int bufferSize,
        uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(uint device, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(uint device, uint flags);
}

public static class StartupRegistration
{
    private const string ValueName = "Win2DS5";
    private const string TaskName = "Win2DS5";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled() => RunValue() != null || TaskExists();

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            RemoveRunValue();
            DeleteTask();
            return;
        }

            if (TryRegisterLogonTask())
            RemoveRunValue();
        else
            WriteRunValue();
    }

    private static bool TryRegisterLogonTask()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var user = WindowsIdentity.GetCurrent().Name;
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Start Win2DS5 when you sign in.";
            definition.Principal.UserId = user;
            definition.Principal.LogonType = 3;
            definition.Principal.RunLevel = Elevation.IsElevated ? 1 : 0;
            definition.Settings.AllowDemandStart = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.ExecutionTimeLimit = "PT0S";
            definition.Settings.MultipleInstances = 2;
            dynamic trigger = definition.Triggers.Create(9);
            trigger.UserId = user;
            dynamic action = definition.Actions.Create(0);
            action.Path = path;
            action.Arguments = "--background";
            folder.RegisterTaskDefinition(TaskName, definition, 6, Type.Missing, Type.Missing, 3, Type.Missing);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TaskExists()
    {
        try
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            dynamic task = folder.GetTask(TaskName);
            return (bool)task.Enabled;
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteTask()
    {
        try
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            folder.DeleteTask(TaskName, 0);
        }
        catch
        {
            // Already absent.
        }
    }

    private static string? RunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) as string;
    }

    private static void WriteRunValue()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.SetValue(ValueName, $"\"{path}\" --background");
    }

    private static void RemoveRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(ValueName, false);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVICE_INTERFACE_DATA
{
    public int cbSize;
    public Guid InterfaceClassGuid;
    public int Flags;
    public nint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVINFO_DATA
{
    public int cbSize;
    public Guid ClassGuid;
    public uint DevInst;
    public nint Reserved;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SP_DEVICE_INTERFACE_DETAIL_DATA
{
    public int cbSize;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
    public string DevicePath;
}

internal static class SetupApi
{
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr list,
        IntPtr deviceInfo,
        ref Guid interfaceClass,
        uint index,
        ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr list,
        ref SP_DEVICE_INTERFACE_DATA data,
        ref SP_DEVICE_INTERFACE_DETAIL_DATA detail,
        int detailSize,
        out int required,
        ref SP_DEVINFO_DATA deviceInfo);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInstanceId(
        IntPtr list,
        ref SP_DEVINFO_DATA deviceInfo,
        StringBuilder buffer,
        int bufferChars,
        out int required);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
}

