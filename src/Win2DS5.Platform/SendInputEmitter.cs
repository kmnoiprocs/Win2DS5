using System.Runtime.InteropServices;
using Win2DS5.Core;

namespace Win2DS5.Platform;

public sealed class SendInputEmitter
{
    private readonly HashSet<(ushort Scan, bool Extended)> _keys = [];
    private readonly HashSet<MouseButton> _buttons = [];

    public void Apply(IReadOnlyList<OutputCommand> commands)
    {
        if (commands.Count == 0)
            return;

        var inputs = new INPUT[commands.Count];
        int count = 0;
        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case OutputKind.KeyDown:
                    inputs[count++] = Keyboard((ushort)command.A, command.B != 0, up: false);
                    _keys.Add(((ushort)command.A, command.B != 0));
                    break;
                case OutputKind.KeyUp:
                    inputs[count++] = Keyboard((ushort)command.A, command.B != 0, up: true);
                    _keys.Remove(((ushort)command.A, command.B != 0));
                    break;
                case OutputKind.MouseDown:
                    inputs[count++] = MouseButtonInput((MouseButton)command.A, up: false);
                    _buttons.Add((MouseButton)command.A);
                    break;
                case OutputKind.MouseUp:
                    inputs[count++] = MouseButtonInput((MouseButton)command.A, up: true);
                    _buttons.Remove((MouseButton)command.A);
                    break;
                case OutputKind.MouseMove:
                    inputs[count++] = MouseMove(command.A, command.B);
                    break;
                case OutputKind.Scroll:
                    inputs[count++] = MouseWheel(command.A, command.B);
                    break;
            }
        }

        if (count > 0)
            NativeMethods.SendInput((uint)count, inputs, Marshal.SizeOf<INPUT>());
    }

    public void ReleaseAll()
    {
        var commands = new List<OutputCommand>();
        foreach (var key in _keys)
            commands.Add(OutputCommand.KeyUp(key.Scan, key.Extended));
        foreach (var button in _buttons)
            commands.Add(OutputCommand.MouseUp(button));
        _keys.Clear();
        _buttons.Clear();
        if (commands.Count > 0)
            Apply(commands);
    }

    private static INPUT Keyboard(ushort scan, bool extended, bool up)
    {
        uint flags = 0x0008;
        if (extended) flags |= 0x0001;
        if (up) flags |= 0x0002;
        return new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = scan,
                    dwFlags = flags,
                },
            },
        };
    }

    private static INPUT MouseButtonInput(MouseButton button, bool up)
    {
        uint flags = button switch
        {
            MouseButton.Left => up ? 0x0004u : 0x0002u,
            MouseButton.Right => up ? 0x0010u : 0x0008u,
            MouseButton.Middle => up ? 0x0040u : 0x0020u,
            _ => up ? 0x0100u : 0x0080u,
        };
        uint data = button switch
        {
            MouseButton.X1 => 1,
            MouseButton.X2 => 2,
            _ => 0,
        };
        return new INPUT
        {
            type = 0,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dwFlags = flags, mouseData = data },
            },
        };
    }

    private static INPUT MouseMove(int dx, int dy) => new()
    {
        type = 0,
        U = new InputUnion
        {
            mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = 0x0001 },
        },
    };

    private static INPUT MouseWheel(int dx, int dy)
    {
        if (dx != 0 && dy == 0)
        {
            return new INPUT
            {
                type = 0,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT { mouseData = unchecked((uint)dx), dwFlags = 0x1000 },
                },
            };
        }

        return new INPUT
        {
            type = 0,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { mouseData = unchecked((uint)dy), dwFlags = 0x0800 },
            },
        };
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
}
