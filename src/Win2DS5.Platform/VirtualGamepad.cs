using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Win2DS5.Core;

namespace Win2DS5.Platform;

public sealed class VirtualGamepad : IDisposable
{
    public const string MissingDriver = "Install ViGEmBus so games can see controller buttons in desktop mode.";

    private readonly HashSet<ControlId> _held = [];
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;
    private short _leftX;
    private short _leftY;
    private short _rightX;
    private short _rightY;

    public bool IsConnected => _pad != null;
    public bool IsAvailable { get; private set; }

    public void EnsureConnected()
    {
        if (_pad != null)
            return;
        try
        {
            _client ??= new ViGEmClient();
            _pad = _client.CreateXbox360Controller();
            _pad.Connect();
            IsAvailable = true;
            Write();
        }
        catch (Exception)
        {
            IsAvailable = false;
            _pad = null;
        }
    }

    public void Disconnect()
    {
        if (_pad == null)
            return;
        try
        {
            _held.Clear();
            _leftX = _leftY = _rightX = _rightY = 0;
            Write();
            _pad.Disconnect();
        }
        catch (Exception)
        {
            // The virtual pad is already gone.
        }

        _pad = null;
    }

    public void Apply(IReadOnlyList<OutputCommand> commands)
    {
        if (_pad == null)
            return;
        _leftX = _leftY = _rightX = _rightY = 0;
        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case OutputKind.PadDown:
                    _held.Add((ControlId)command.A);
                    break;
                case OutputKind.PadUp:
                    _held.Remove((ControlId)command.A);
                    break;
                case OutputKind.LeftStick:
                    _leftX = Clamp(command.A);
                    _leftY = Clamp(command.B);
                    break;
                case OutputKind.RightStick:
                    _rightX = Clamp(command.A);
                    _rightY = Clamp(command.B);
                    break;
            }
        }

        Write();
    }

    public void Dispose()
    {
        Disconnect();
        _client?.Dispose();
        _client = null;
    }

    private void Write()
    {
        if (_pad == null)
            return;
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, _leftX);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, _leftY);
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, _rightX);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, _rightY);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, _held.Contains(ControlId.L2) ? (byte)255 : (byte)0);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, _held.Contains(ControlId.R2) ? (byte)255 : (byte)0);
        _pad.SetButtonState(Xbox360Button.A, _held.Contains(ControlId.Cross));
        _pad.SetButtonState(Xbox360Button.B, _held.Contains(ControlId.Circle));
        _pad.SetButtonState(Xbox360Button.X, _held.Contains(ControlId.Square));
        _pad.SetButtonState(Xbox360Button.Y, _held.Contains(ControlId.Triangle));
        _pad.SetButtonState(Xbox360Button.Up, _held.Contains(ControlId.DPadUp));
        _pad.SetButtonState(Xbox360Button.Down, _held.Contains(ControlId.DPadDown));
        _pad.SetButtonState(Xbox360Button.Left, _held.Contains(ControlId.DPadLeft));
        _pad.SetButtonState(Xbox360Button.Right, _held.Contains(ControlId.DPadRight));
        _pad.SetButtonState(Xbox360Button.LeftShoulder, _held.Contains(ControlId.L1));
        _pad.SetButtonState(Xbox360Button.RightShoulder, _held.Contains(ControlId.R1));
        _pad.SetButtonState(Xbox360Button.LeftThumb, _held.Contains(ControlId.L3));
        _pad.SetButtonState(Xbox360Button.RightThumb, _held.Contains(ControlId.R3));
        _pad.SetButtonState(Xbox360Button.Back, _held.Contains(ControlId.Create));
        _pad.SetButtonState(Xbox360Button.Start, _held.Contains(ControlId.Options));
        _pad.SetButtonState(Xbox360Button.Guide, _held.Contains(ControlId.Ps) || _held.Contains(ControlId.TouchpadClick) || _held.Contains(ControlId.Mute));
    }

    private static short Clamp(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);
}
