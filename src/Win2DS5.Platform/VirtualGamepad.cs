using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Win2DS5.Core;

namespace Win2DS5.Platform;

public sealed class VirtualGamepad : IDisposable
{
    public const string MissingDriver = "Install ViGEmBus so virtual controllers can be created.";

    private readonly Dictionary<string, PadSlot> _slots = [];
    private ViGEmClient? _client;

    public bool IsAvailable { get; private set; } = true;

    public void Sync(IReadOnlyList<OutputDevice> outputs)
    {
        var pads = OutputCatalog.PadsInSlotOrder(outputs);
        foreach (var id in _slots.Keys.Where(id => pads.All(output => output.Id != id)).ToList())
        {
            _slots[id].Dispose();
            _slots.Remove(id);
        }

        if (pads.Count == 0)
        {
            IsAvailable = true;
            return;
        }

        try
        {
            _client ??= new ViGEmClient();
            foreach (var output in pads)
            {
                if (_slots.ContainsKey(output.Id))
                    continue;
                var slot = output.Kind == OutputDeviceKind.Xbox
                    ? PadSlot.Xbox(_client)
                    : PadSlot.DualShock(_client);
                slot.Connect();
                _slots[output.Id] = slot;
            }

            IsAvailable = true;
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    public void Disconnect()
    {
        foreach (var slot in _slots.Values)
            slot.Dispose();
        _slots.Clear();
    }

    public void Apply(IReadOnlyList<OutputCommand> commands)
    {
        if (_slots.Count == 0)
            return;
        foreach (var group in commands.GroupBy(command => command.OutputId))
        {
            if (string.IsNullOrEmpty(group.Key))
                continue;
            if (_slots.TryGetValue(group.Key, out var slot))
                slot.Apply(group);
        }
    }

    public void Dispose()
    {
        Disconnect();
        _client?.Dispose();
        _client = null;
    }

    private sealed class PadSlot : IDisposable
    {
        private readonly IXbox360Controller? _xbox;
        private readonly IDualShock4Controller? _ds4;
        private readonly HashSet<ControlId> _held = [];
        private short _leftX;
        private short _leftY;
        private short _rightX;
        private short _rightY;

        private PadSlot(IXbox360Controller xbox) => _xbox = xbox;

        private PadSlot(IDualShock4Controller ds4) => _ds4 = ds4;

        public static PadSlot Xbox(ViGEmClient client) => new(client.CreateXbox360Controller());

        public static PadSlot DualShock(ViGEmClient client) => new(client.CreateDualShock4Controller());

        public void Connect()
        {
            _xbox?.Connect();
            _ds4?.Connect();
            Write();
        }

        public void Apply(IEnumerable<OutputCommand> commands)
        {
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
            try
            {
                _held.Clear();
                _leftX = _leftY = _rightX = _rightY = 0;
                Write();
                _xbox?.Disconnect();
                _ds4?.Disconnect();
            }
            catch (Exception)
            {
                // The virtual pad is already gone.
            }
        }

        private void Write()
        {
            if (_xbox != null)
                WriteXbox();
            if (_ds4 != null)
                WriteDualShock();
        }

        private void WriteXbox()
        {
            var pad = _xbox!;
            pad.SetAxisValue(Xbox360Axis.LeftThumbX, _leftX);
            pad.SetAxisValue(Xbox360Axis.LeftThumbY, _leftY);
            pad.SetAxisValue(Xbox360Axis.RightThumbX, _rightX);
            pad.SetAxisValue(Xbox360Axis.RightThumbY, _rightY);
            pad.SetSliderValue(Xbox360Slider.LeftTrigger, Down(ControlId.L2));
            pad.SetSliderValue(Xbox360Slider.RightTrigger, Down(ControlId.R2));
            pad.SetButtonState(Xbox360Button.A, Held(ControlId.Cross));
            pad.SetButtonState(Xbox360Button.B, Held(ControlId.Circle));
            pad.SetButtonState(Xbox360Button.X, Held(ControlId.Square));
            pad.SetButtonState(Xbox360Button.Y, Held(ControlId.Triangle));
            pad.SetButtonState(Xbox360Button.Up, Held(ControlId.DPadUp));
            pad.SetButtonState(Xbox360Button.Down, Held(ControlId.DPadDown));
            pad.SetButtonState(Xbox360Button.Left, Held(ControlId.DPadLeft));
            pad.SetButtonState(Xbox360Button.Right, Held(ControlId.DPadRight));
            pad.SetButtonState(Xbox360Button.LeftShoulder, Held(ControlId.L1));
            pad.SetButtonState(Xbox360Button.RightShoulder, Held(ControlId.R1));
            pad.SetButtonState(Xbox360Button.LeftThumb, Held(ControlId.L3));
            pad.SetButtonState(Xbox360Button.RightThumb, Held(ControlId.R3));
            pad.SetButtonState(Xbox360Button.Back, Held(ControlId.Create));
            pad.SetButtonState(Xbox360Button.Start, Held(ControlId.Options));
            pad.SetButtonState(Xbox360Button.Guide, Held(ControlId.Ps) || Held(ControlId.TouchpadClick) || Held(ControlId.Mute));
        }

        private void WriteDualShock()
        {
            var pad = _ds4!;
            pad.SetAxisValue(DualShock4Axis.LeftThumbX, Axis(_leftX));
            pad.SetAxisValue(DualShock4Axis.LeftThumbY, Axis(_leftY));
            pad.SetAxisValue(DualShock4Axis.RightThumbX, Axis(_rightX));
            pad.SetAxisValue(DualShock4Axis.RightThumbY, Axis(_rightY));
            pad.SetSliderValue(DualShock4Slider.LeftTrigger, Down(ControlId.L2));
            pad.SetSliderValue(DualShock4Slider.RightTrigger, Down(ControlId.R2));
            pad.SetButtonState(DualShock4Button.Cross, Held(ControlId.Cross));
            pad.SetButtonState(DualShock4Button.Circle, Held(ControlId.Circle));
            pad.SetButtonState(DualShock4Button.Square, Held(ControlId.Square));
            pad.SetButtonState(DualShock4Button.Triangle, Held(ControlId.Triangle));
            pad.SetButtonState(DualShock4Button.ShoulderLeft, Held(ControlId.L1));
            pad.SetButtonState(DualShock4Button.ShoulderRight, Held(ControlId.R1));
            pad.SetButtonState(DualShock4Button.ThumbLeft, Held(ControlId.L3));
            pad.SetButtonState(DualShock4Button.ThumbRight, Held(ControlId.R3));
            pad.SetButtonState(DualShock4Button.Share, Held(ControlId.Create));
            pad.SetButtonState(DualShock4Button.Options, Held(ControlId.Options));
            pad.SetButtonState(DualShock4SpecialButton.Ps, Held(ControlId.Ps) || Held(ControlId.Mute));
            pad.SetButtonState(DualShock4SpecialButton.Touchpad, Held(ControlId.TouchpadClick));
            pad.SetDPadDirection(Direction());
        }

        private bool Held(ControlId id) => _held.Contains(id);

        private byte Down(ControlId id) => Held(id) ? (byte)255 : (byte)0;

        private DualShock4DPadDirection Direction()
        {
            bool up = Held(ControlId.DPadUp);
            bool down = Held(ControlId.DPadDown);
            bool left = Held(ControlId.DPadLeft);
            bool right = Held(ControlId.DPadRight);
            if (up && right) return DualShock4DPadDirection.Northeast;
            if (up && left) return DualShock4DPadDirection.Northwest;
            if (down && right) return DualShock4DPadDirection.Southeast;
            if (down && left) return DualShock4DPadDirection.Southwest;
            if (up) return DualShock4DPadDirection.North;
            if (down) return DualShock4DPadDirection.South;
            if (left) return DualShock4DPadDirection.West;
            if (right) return DualShock4DPadDirection.East;
            return DualShock4DPadDirection.None;
        }

        private static short Clamp(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

        private static byte Axis(short value) => (byte)Math.Clamp((value + 32768) * 255 / 65535, 0, 255);
    }
}
