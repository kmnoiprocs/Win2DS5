using System.Runtime.InteropServices;
using Win2DS5.App.Theming;

namespace Win2DS5.App.Tray;

internal sealed class TrayIcon : IDisposable
{
    public const int CallbackMessage = 0x8000;
    private bool _added;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private (byte R, byte G, byte B) _accent = (0x7E, 0xB6, 0xFF);

    public event Action? OpenRequested;
    public event Action? ToggleRequested;
    public event Action? KeyboardRequested;
    public event Action? ExitRequested;

    public void Add(IntPtr hwnd)
    {
        _hwnd = hwnd;
        var data = Create(hwnd);
        _added = Shell_NotifyIcon(0, ref data);
    }

    public void SetAccent(Windows.UI.Color color)
    {
        if (_accent == (color.R, color.G, color.B) && _icon != IntPtr.Zero)
            return;
        _accent = (color.R, color.G, color.B);
        if (!_added)
            return;
        var data = Create(_hwnd);
        Shell_NotifyIcon(1, ref data);
    }

    public void Remove(IntPtr hwnd)
    {
        if (!_added)
            return;
        var data = Create(hwnd);
        Shell_NotifyIcon(2, ref data);
        _added = false;
    }

    public void Handle(IntPtr hwnd, nint lParam)
    {
        int message = (int)lParam;
        if (message is 0x0202 or 0x0203)
        {
            OpenRequested?.Invoke();
            return;
        }

        if (message != 0x0205)
            return;

        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, 1, "Toggle desktop control");
        AppendMenu(menu, 0, 2, "On-screen keyboard");
        AppendMenu(menu, 0, 3, "Open Win2DS5");
        AppendMenu(menu, 0x800, 0, null);
        AppendMenu(menu, 0, 4, "Exit");
        GetCursorPos(out var point);
        SetForegroundWindow(hwnd);
        int command = TrackPopupMenu(menu, 0x0100, point.X, point.Y, 0, hwnd, IntPtr.Zero);
        DestroyMenu(menu);
        switch (command)
        {
            case 1: ToggleRequested?.Invoke(); break;
            case 2: KeyboardRequested?.Invoke(); break;
            case 3: OpenRequested?.Invoke(); break;
            case 4: ExitRequested?.Invoke(); break;
        }
    }

    public void Dispose()
    {
    }

    private NOTIFYICONDATA Create(IntPtr hwnd)
    {
        var previous = _icon;
        _icon = CreateIcon(_accent);
        if (previous != IntPtr.Zero)
            DestroyIcon(previous);
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = 7,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = "Win2DS5",
        };
    }

    private static IntPtr CreateIcon((byte R, byte G, byte B) accent) =>
        LogoArt.CreateIcon(accent.R, accent.G, accent.B, 32);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
