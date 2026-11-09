using System.Runtime.InteropServices;
using System.Text;

namespace Win2DS5.Platform;

public static class HidHideIoctl
{
    public const ushort GetWhitelist = 2048;
    public const ushort SetWhitelist = 2049;
    public const ushort GetBlacklist = 2050;
    public const ushort SetBlacklist = 2051;
    public const ushort GetActive = 2052;
    public const ushort SetActive = 2053;
    public const ushort AddSession = 2056;
    public const ushort ClearSession = 2057;

    public static uint Code(ushort function) =>
        (32769u << 16) | (1u << 14) | ((uint)function << 2);
}

public sealed class HidHideClient(string journalPath)
{
    private readonly string _journal = journalPath;

    public HidHideClient()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Win2DS5",
            "hidhide-cloak.txt"))
    {
    }

    public bool IsInstalled()
    {
        var handle = Open();
        if (handle == IntPtr.Zero)
            return false;
        Close(handle);
        return true;
    }

    public bool TryPrepare(out string error)
    {
        error = "";
        var handle = Open();
        if (handle == IntPtr.Zero)
        {
            error = "HidHide is not installed. Games can still see the controller.";
            return false;
        }

        try
        {
            if (!GetActive(handle) && !SetActive(handle, true))
            {
                error = "HidHide is installed, but it could not be switched on.";
                return false;
            }

            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Win2DS5 could not find its own program path to whitelist.";
                return false;
            }

            var native = DevicePaths.ToNtPath(path);
            var current = GetMultiSz(handle, HidHideIoctl.GetWhitelist);
            if (current == null)
            {
                error = "HidHide did not return its application whitelist.";
                return false;
            }

            if (current.Any(entry => entry.Equals(native, StringComparison.OrdinalIgnoreCase)))
                return true;

            current.Add(native);
            if (!SetMultiSz(handle, HidHideIoctl.SetWhitelist, current))
            {
                error = "HidHide rejected the whitelist update.";
                return false;
            }

            return true;
        }
        finally
        {
            Close(handle);
        }
    }

    public bool TryCloak(IReadOnlyList<string> instanceIds, out string error)
    {
        error = "";
        if (instanceIds.Count == 0)
        {
            error = "No DualSense interfaces were found to hide.";
            return false;
        }

        var handle = Open();
        if (handle == IntPtr.Zero)
        {
            error = "HidHide is not installed. Games can still see the controller.";
            return false;
        }

        try
        {
            if (DeviceIoControl(handle, HidHideIoctl.Code(HidHideIoctl.AddSession), MultiSz(instanceIds), out _, 0))
                return true;

            // Released drivers (1.5 and older) have no session list, so fall back to the persistent
            // blacklist and journal what was added so a crash can be undone on the next launch.
            var blacklist = GetMultiSz(handle, HidHideIoctl.GetBlacklist);
            if (blacklist == null)
            {
                error = "HidHide could not hide the controller.";
                return false;
            }

            var journal = ReadJournal();
            foreach (var id in instanceIds)
            {
                if (blacklist.Any(entry => entry.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                blacklist.Add(id);
                journal.Add(id);
            }

            WriteJournal(journal);
            if (!SetMultiSz(handle, HidHideIoctl.SetBlacklist, blacklist))
            {
                error = "HidHide rejected the controller hide list.";
                return false;
            }

            return true;
        }
        finally
        {
            Close(handle);
        }
    }

    public bool TryUncloak(out string error)
    {
        error = "";
        var handle = Open();
        if (handle == IntPtr.Zero)
        {
            error = "HidHide is not installed.";
            return false;
        }

        try
        {
            bool session = DeviceIoControl(handle, HidHideIoctl.Code(HidHideIoctl.ClearSession), null, out _, 0);
            var journal = ReadJournal();
            if (journal.Count == 0)
            {
                if (!session)
                    error = "HidHide could not release the controller.";
                return session;
            }

            var blacklist = GetMultiSz(handle, HidHideIoctl.GetBlacklist);
            if (blacklist == null)
            {
                error = "HidHide could not release the controller.";
                return false;
            }

            blacklist.RemoveAll(entry => journal.Contains(entry, StringComparer.OrdinalIgnoreCase));
            if (!SetMultiSz(handle, HidHideIoctl.SetBlacklist, blacklist))
            {
                error = "HidHide could not release the controller.";
                return false;
            }

            WriteJournal([]);
            return true;
        }
        finally
        {
            Close(handle);
        }
    }

    /// <summary>Releases anything a previous run hid through the blacklist but never released.</summary>
    public void RecoverStale()
    {
        if (ReadJournal().Count > 0)
            TryUncloak(out _);
    }

    private List<string> ReadJournal()
    {
        try
        {
            return File.Exists(_journal)
                ? File.ReadAllLines(_journal).Where(line => line.Length > 0).ToList()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private void WriteJournal(IReadOnlyCollection<string> ids)
    {
        try
        {
            if (ids.Count == 0)
            {
                File.Delete(_journal);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_journal)!);
            File.WriteAllLines(_journal, ids.Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool GetActive(IntPtr handle)
    {
        var output = new byte[1];
        if (!DeviceIoControl(handle, HidHideIoctl.Code(HidHideIoctl.GetActive), null, output, out int read) || read < 1)
            return false;
        return output[0] != 0;
    }

    private static bool SetActive(IntPtr handle, bool active)
    {
        var input = new byte[] { active ? (byte)1 : (byte)0 };
        return DeviceIoControl(handle, HidHideIoctl.Code(HidHideIoctl.SetActive), input, out _, 0);
    }

    private static List<string>? GetMultiSz(IntPtr handle, ushort function)
    {
        // HidHide rejects any output buffer that is not exactly the size it reports.
        if (!DeviceIoControl(handle, HidHideIoctl.Code(function), null, null, out int needed))
            return null;
        if (needed <= 0)
            return [];

        var output = new byte[needed];
        if (!DeviceIoControl(handle, HidHideIoctl.Code(function), null, output, out int read))
            return null;
        if (read <= 0)
            return [];
        return Encoding.Unicode.GetString(output, 0, read)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static bool SetMultiSz(IntPtr handle, ushort function, IReadOnlyList<string> values) =>
        DeviceIoControl(handle, HidHideIoctl.Code(function), MultiSz(values), out _, 0);

    private static byte[] MultiSz(IEnumerable<string> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                builder.Append(value).Append('\0');
        }

        builder.Append('\0');
        return Encoding.Unicode.GetBytes(builder.ToString());
    }

    private static IntPtr Open()
    {
        var handle = CreateFile(@"\\.\HidHide", 0x80000000 | 0x40000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        return handle == new IntPtr(-1) ? IntPtr.Zero : handle;
    }

    private static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            CloseHandle(handle);
    }

    private static bool DeviceIoControl(IntPtr handle, uint code, byte[]? input, out int read, int outputLength)
    {
        var output = outputLength == 0 ? null : new byte[outputLength];
        return DeviceIoControl(handle, code, input, output, out read);
    }

    private static bool DeviceIoControl(IntPtr handle, uint code, byte[]? input, byte[]? output, out int read)
    {
        read = 0;
        return NativeDeviceIoControl(
            handle,
            code,
            input,
            input?.Length ?? 0,
            output,
            output?.Length ?? 0,
            out read,
            IntPtr.Zero);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string name,
        uint access,
        uint share,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    private static extern bool NativeDeviceIoControl(
        IntPtr device,
        uint code,
        byte[]? input,
        int inputLength,
        byte[]? output,
        int outputLength,
        out int bytesReturned,
        IntPtr overlapped);
}
