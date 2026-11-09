using System.Runtime.InteropServices;
using Win2DS5.Core;

namespace Win2DS5.App.Overlay;

internal static class ScreenMetrics
{
    public static PixelPoint Cursor()
    {
        if (!GetCursorPos(out var point))
            return new PixelPoint(0, 0);
        return new PixelPoint(point.X, point.Y);
    }

    public static PixelRect WorkAreaAt(PixelPoint point)
    {
        var monitor = MonitorFromPoint(new POINT { X = point.X, Y = point.Y }, 2);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            return Virtual();
        return From(info.rcWork);
    }

    public static PixelRect Virtual()
    {
        return new PixelRect(
            GetSystemMetrics(76),
            GetSystemMetrics(77),
            Math.Max(1, GetSystemMetrics(78)),
            Math.Max(1, GetSystemMetrics(79)));
    }

    public static double ScaleAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, 2);
        if (monitor != 0 && GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0)
            return dpi / 96.0;
        return 1;
    }

    private static PixelRect From(RECT rect)
    {
        return new PixelRect(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
