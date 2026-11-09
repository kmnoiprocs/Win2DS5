using System.Runtime.InteropServices;

namespace Win2DS5.Platform;

public static class Elevation
{
    private static readonly int OwnLevel = ReadLevel(GetCurrentProcess());

    public static bool IsElevated { get; } = OwnLevel >= 0x3000;

    /// <summary>
    /// The administrator approval window runs on a desktop this process cannot see.
    /// Mouse input from here never arrives there.
    /// </summary>
    public static bool SecureDesktopActive()
    {
        var desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop != IntPtr.Zero)
        {
            CloseDesktop(desktop);
            return false;
        }

        return Marshal.GetLastWin32Error() == 5;
    }

    public static bool ForegroundIsUs()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    public static bool ForegroundIsAboveUs()
    {
        if (IsElevated)
            return false;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
            return false;

        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero)
            return false;
        try
        {
            int level = ReadLevel(process);
            return level > OwnLevel && OwnLevel > 0;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static int ReadLevel(IntPtr process)
    {
        if (process == IntPtr.Zero || !OpenProcessToken(process, 0x0008, out var token))
            return 0;
        try
        {
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out int needed);
            if (needed <= 0)
                return 0;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, needed, out _))
                    return 0;
                var sid = Marshal.ReadIntPtr(buffer);
                if (sid == IntPtr.Zero)
                    return 0;
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0)
                    return 0;
                return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr buffer, int length, out int returned);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
}
