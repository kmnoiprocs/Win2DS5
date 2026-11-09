using System.Runtime.InteropServices;

namespace Win2DS5.App.Overlay;

internal static class NativeOverlay
{
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    public static void Configure(IntPtr hwnd)
    {
        int ex = GetWindowLong(hwnd, -20);
        ex |= 0x08000000 | 0x00000080 | 0x00000008;
        SetWindowLong(hwnd, -20, ex);
        int corner = 2;
        DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
    }

    public static void AllowClicks(IntPtr hwnd)
    {
        int ex = GetWindowLong(hwnd, -20);
        SetWindowLong(hwnd, -20, ex & ~0x08000000);
    }

    public static void Place(IntPtr hwnd, double physicalX, double physicalY, double dipW, double dipH, double opacity)
    {
        int x = (int)Math.Round(physicalX);
        int y = (int)Math.Round(physicalY);
        double scale = ScreenMetrics.ScaleAt(x, y);
        int w = Math.Max(120, (int)Math.Round(dipW * scale));
        int h = Math.Max(80, (int)Math.Round(dipH * scale));
        double settled = ScreenMetrics.ScaleAt(x + (w / 2), y + (h / 2));
        if (Math.Abs(settled - scale) > 0.01)
        {
            scale = settled;
            w = Math.Max(120, (int)Math.Round(dipW * scale));
            h = Math.Max(80, (int)Math.Round(dipH * scale));
        }

        SetWindowPos(hwnd, new IntPtr(-1), x, y, w, h, 0x0010);
        byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 70, 255);
        int style = GetWindowLong(hwnd, -20);
        if ((style & 0x00080000) == 0)
            SetWindowLong(hwnd, -20, style | 0x00080000);
        SetLayeredWindowAttributes(hwnd, 0, alpha, 0x02);
    }

    public static double Scale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    public static void SetBorderColor(IntPtr hwnd, Windows.UI.Color color)
    {
        int colorRef = color.R | (color.G << 8) | (color.B << 16);
        DwmSetWindowAttribute(hwnd, 34, ref colorRef, 4);
    }

    public static void ShowWithoutActivating(IntPtr hwnd) => ShowWindow(hwnd, 4);

    public static void Hide(IntPtr hwnd) => ShowWindow(hwnd, 0);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
