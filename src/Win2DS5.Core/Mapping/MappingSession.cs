namespace Win2DS5.Core;

public sealed class MappingResult
{
    public List<OutputCommand> Commands { get; } = [];
    public bool ToggleDesktop { get; set; }
    public bool ToggleKeyboard { get; set; }
    public bool Pause { get; set; }
    public bool Resume { get; set; }
    public string SystemAction { get; set; } = "";
    public string SystemPath { get; set; } = "";
    public int Volume { get; set; } = -1;
    public string SwitchProfileId { get; set; } = "";
    public string ActiveLayoutId { get; set; } = "base";
    public string ActiveLayoutName { get; set; } = "Base";
}

public sealed class MappingSession
{
    private readonly HashSet<ControlId> _now = [];
    private readonly HashSet<ControlId> _then = [];
    private readonly List<(ControlId Control, string LayoutId)> _holds = [];
    private readonly Dictionary<ControlId, double> _scales = [];
    private readonly Dictionary<ControlId, ArmedTap> _taps = [];
    private readonly Dictionary<ControlId, List<KeyStroke>> _keysByControl = [];
    private readonly Dictionary<(ushort Scan, bool Extended), int> _keyRefs = [];
    private readonly Dictionary<ControlId, List<MouseButton>> _mouseByControl = [];
    private readonly Dictionary<ControlId, Binding> _edges = [];
    private readonly Dictionary<MouseButton, int> _mouseRefs = [];
    private readonly Dictionary<ControlId, List<(ControlId Button, string Output)>> _padByControl = [];
    private readonly Dictionary<(string Output, ControlId Button), int> _padRefs = [];
    private readonly Dictionary<ControlId, PendingLong> _pending = [];
    private readonly Dictionary<ControlId, (Binding Binding, TimeSpan Next)> _scrollHolds = [];
    private readonly HashSet<string> _stickOutputs = [];
    private readonly Dictionary<ControlId, (int Stick, int X, int Y, string[] Outputs)> _nudges = [];
    private readonly TouchGestureEngine _touch = new();
    private string? _toggled;
    private bool _chordWasDown;
    private bool _haveTime;
    private bool _navHeld;
    private bool _oskTouch;
    private TimeSpan _lastTime;
    private TimeSpan _navNext;
    private double _fracX;
    private double _fracY;
    private double _wheelX;
    private double _wheelY;

    public MappingSession(Profile profile)
    {
        Profile = profile;
    }

    public Profile Profile { get; set; }
    public InputLayout? Layout { get; set; }
    public bool DesktopEnabled { get; set; } = true;
    public bool Passthrough { get; set; }
    public int LongPressMs { get; set; } = 350;
    public KeyboardModel Keyboard { get; } = new();

    public MappingResult Step(in ControllerFrame frame, TimeSpan now)
    {
        var result = new MappingResult();
        double dt = 0.008;
        if (_haveTime)
        {
            dt = (now - _lastTime).TotalSeconds;
            if (dt <= 0 || dt > 0.05)
                dt = 0.008;
        }

        _lastTime = now;
        _haveTime = true;
        FrameButtons.Collect(frame, _now);

        if (Passthrough)
        {
            foreach (var id in Rising())
            {
                if (TogglesPassthrough(Base.Get(id)))
                    result.Pause = true;
            }

            PublishLayout(result);
            Copy();
            return result;
        }

        bool chord = ChordDown();
        if (chord && !_chordWasDown)
            result.ToggleDesktop = true;
        _chordWasDown = chord;

        if (!DesktopEnabled)
        {
            _touch.Reset();
            PublishLayout(result);
            Copy();
            return result;
        }

        if (Keyboard.IsOpen)
        {
            HandleKeyboard(frame, dt, now, result);
            PublishLayout(result);
            Copy();
            return result;
        }

        DispatchButtons(result, now);
        DispatchPointer(frame, dt, now, result);
        ApplyNudges(result.Commands);
        PublishLayout(result);
        Copy();
        return result;
    }

    public List<OutputCommand> ReleaseAll()
    {
        var commands = new List<OutputCommand>();
        foreach (var pair in _keyRefs)
        {
            if (pair.Value > 0)
                commands.Add(OutputCommand.KeyUp(pair.Key.Scan, pair.Key.Extended));
        }

        foreach (var pair in _mouseRefs)
        {
            if (pair.Value > 0)
                commands.Add(OutputCommand.MouseUp(pair.Key));
        }

        foreach (var pair in _padRefs)
        {
            if (pair.Value > 0)
                commands.Add(OutputCommand.PadUp(pair.Key.Button) with { OutputId = pair.Key.Output });
        }

        CenterSticks(commands, null);
        foreach (var nudge in _nudges.Values)
        {
            foreach (var output in nudge.Outputs)
                EmitNudge(nudge.Stick, 0, 0, output, commands);
        }

        _nudges.Clear();
        _keyRefs.Clear();
        _mouseRefs.Clear();
        _padRefs.Clear();
        _keysByControl.Clear();
        _mouseByControl.Clear();
        _padByControl.Clear();
        _scrollHolds.Clear();
        _holds.Clear();
        _scales.Clear();
        _taps.Clear();
        _pending.Clear();
        _edges.Clear();
        _toggled = null;
        _fracX = _fracY = _wheelX = _wheelY = 0;
        _navHeld = false;
        return commands;
    }

    public List<OutputCommand> ReleaseOutput(OutputDevice output)
    {
        var commands = new List<OutputCommand>();
        if (output.Kind == OutputDeviceKind.Keyboard)
        {
            foreach (var pair in _keyRefs)
            {
                if (pair.Value > 0)
                    commands.Add(OutputCommand.KeyUp(pair.Key.Scan, pair.Key.Extended));
            }

            _keyRefs.Clear();
            _keysByControl.Clear();
        }
        else if (output.Kind == OutputDeviceKind.Mouse)
        {
            foreach (var pair in _mouseRefs)
            {
                if (pair.Value > 0)
                    commands.Add(OutputCommand.MouseUp(pair.Key));
            }

            _mouseRefs.Clear();
            _mouseByControl.Clear();
        }
        else if (OutputCatalog.IsPad(output.Kind))
        {
            foreach (var pair in _padRefs.Where(pair => pair.Key.Output == output.Id && pair.Value > 0).ToList())
                commands.Add(OutputCommand.PadUp(pair.Key.Button) with { OutputId = output.Id });
            foreach (var key in _padRefs.Keys.Where(key => key.Output == output.Id).ToList())
                _padRefs.Remove(key);
            foreach (var pair in _padByControl.ToList())
            {
                pair.Value.RemoveAll(item => item.Output == output.Id);
                if (pair.Value.Count == 0)
                    _padByControl.Remove(pair.Key);
            }

            foreach (var pair in _nudges.ToList())
            {
                if (!pair.Value.Outputs.Contains(output.Id))
                    continue;
                EmitNudge(pair.Value.Stick, 0, 0, output.Id, commands);
                var keep = pair.Value.Outputs.Where(id => id != output.Id).ToArray();
                if (keep.Length == 0)
                    _nudges.Remove(pair.Key);
                else
                    _nudges[pair.Key] = (pair.Value.Stick, pair.Value.X, pair.Value.Y, keep);
            }

            CenterSticks(commands, output.Id);
        }

        return commands;
    }

    private void CenterSticks(List<OutputCommand> commands, string? only)
    {
        foreach (var id in _stickOutputs.Where(id => only == null || id == only).ToList())
        {
            commands.Add(OutputCommand.LeftStick(0, 0) with { OutputId = id });
            commands.Add(OutputCommand.RightStick(0, 0) with { OutputId = id });
            _stickOutputs.Remove(id);
        }
    }

    private void PublishLayout(MappingResult result)
    {
        result.ActiveLayoutId = BindingLayoutId();
        result.ActiveLayoutName = FindLayout(result.ActiveLayoutId)?.Name ?? "Base";
    }

    private void DispatchButtons(MappingResult result, TimeSpan now)
    {
        var rising = Rising();
        var falling = Falling();
        foreach (var id in rising)
        {
            foreach (var pair in _taps)
            {
                if (pair.Key != id)
                    pair.Value.Cancel = true;
            }
        }

        foreach (var control in falling.Where(control => !IsLayoutHold(control)))
            ReleaseControl(control, now, result);
        foreach (var control in falling.Where(IsLayoutHold))
            ReleaseControl(control, now, result);

        foreach (var control in rising.Where(IsLayoutHold))
            PressControl(control, now, result);
        foreach (var control in rising.Where(control => !IsLayoutHold(control)))
            PressControl(control, now, result);

        foreach (var pair in _scrollHolds.ToList())
        {
            if (now < pair.Value.Next)
                continue;
            result.Commands.Add(OutputCommand.Wheel(pair.Value.Binding.ScrollX, pair.Value.Binding.ScrollY) with { OutputId = pair.Value.Binding.OutputId });
            _scrollHolds[pair.Key] = (pair.Value.Binding, now + TimeSpan.FromMilliseconds(45));
        }
    }

    private void PressControl(ControlId control, TimeSpan now, MappingResult result)
    {
        if (BindingLayoutId() != Base.Id)
        {
            var reserved = Base.Get(control);
            if (reserved?.Kind is BindingKind.LayoutHold or BindingKind.LayoutToggle or BindingKind.ToggleKeyboard)
            {
                Immediate(control, reserved, now, result);
                return;
            }
        }

        if (control == ControlId.TouchpadClick)
        {
            var gesture = ActiveTouch.GetGesture(_touch.Zone, GestureKind.Click);
            if (gesture != null && gesture.Kind is BindingKind.Key or BindingKind.Mouse or BindingKind.Scroll or BindingKind.Pad)
            {
                Engage(control, gesture, result, now);
                return;
            }
        }

        var binding = Resolve(control);
        if (binding == null)
            return;
        if (binding.Tap != null && binding.Kind != BindingKind.LayoutHold)
        {
            _pending[control] = new PendingLong
            {
                Start = now,
                HoldMs = DelayFor(binding),
                Short = binding.Tap,
                Long = binding,
            };
            return;
        }

        if (binding.Advanced && ((binding.DownMacros?.Count ?? 0) > 0 || (binding.UpMacros?.Count ?? 0) > 0))
        {
            _edges[control] = binding;
            PlayMacros(control, binding.DownMacros, result);
            return;
        }

        Immediate(control, binding, now, result);
        if (UsesSteps(binding))
        {
            _edges[control] = binding;
            PlaySteps(control, binding.PressSteps, result.Commands);
        }
    }

    private void Immediate(ControlId control, Binding binding, TimeSpan now, MappingResult result)
    {
        switch (binding.Kind)
        {
            case BindingKind.LayoutHold:
                ArmHold(control, binding, now);
                return;
            case BindingKind.LayoutToggle:
                ToggleLayout(binding.LayoutId);
                return;
            case BindingKind.ToggleKeyboard:
                if (!result.ToggleDesktop)
                    result.ToggleKeyboard = true;
                return;
            default:
                Engage(control, binding, result, now);
                return;
        }
    }

    private void ReleaseControl(ControlId control, TimeSpan now, MappingResult result)
    {
        var commands = result.Commands;
        bool wasHold = _holds.RemoveAll(hold => hold.Control == control) > 0;
        wasHold |= _scales.Remove(control);
        if (_taps.Remove(control, out var tap))
        {
            if (wasHold && !tap.Cancel && tap.Tap.Kind != BindingKind.None &&
                (now - tap.Start).TotalMilliseconds <= tap.LimitMs)
            {
                Click(tap.Tap, commands);
            }
        }

        if (_pending.Remove(control, out var pending))
        {
            var elapsed = (now - pending.Start).TotalMilliseconds;
            EmitReleased(elapsed >= pending.HoldMs ? pending.Long : pending.Short, result);
        }

        bool managed = false;
        if (_edges.Remove(control, out var edge))
        {
            managed = edge.Advanced && ((edge.DownMacros?.Count ?? 0) > 0 || (edge.UpMacros?.Count ?? 0) > 0);
            if (managed)
                PlayMacros(control, edge.UpMacros, result);
            else
                PlaySteps(control, edge.ReleaseSteps, commands);
        }

        if (!managed)
        {
            ReleaseKeys(control, commands);
            ReleaseMouse(control, commands);
        }
        ReleasePad(control, commands);
        ReleaseNudge(control, commands);
        _scrollHolds.Remove(control);
    }

    private void EmitReleased(Binding? binding, MappingResult result)
    {
        if (binding == null || binding.Kind == BindingKind.None)
            return;
        switch (binding.Kind)
        {
            case BindingKind.LayoutToggle:
                ToggleLayout(binding.LayoutId);
                return;
            case BindingKind.ToggleKeyboard:
                if (!result.ToggleDesktop)
                    result.ToggleKeyboard = true;
                return;
            case BindingKind.Profile:
                result.SwitchProfileId = binding.ProfileId;
                return;
            default:
                Click(binding, result.Commands);
                return;
        }
    }

    private static bool UsesSteps(Binding binding) =>
        binding.Advanced && binding.Tap == null && ((binding.PressSteps?.Count ?? 0) > 0 || (binding.ReleaseSteps?.Count ?? 0) > 0);

    private void PlaySteps(ControlId control, List<EdgeStep>? steps, List<OutputCommand> commands)
    {
        if (steps == null)
            return;
        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case EdgeKind.KeyDown when step.Key is { ScanCode: not 0 }:
                    PressKey(control, step.Key, commands);
                    break;
                case EdgeKind.KeyUp when step.Key != null:
                    ReleaseOneKey(control, step.Key, commands);
                    break;
                case EdgeKind.MouseDown:
                    PressMouse(control, step.Mouse, commands);
                    break;
                case EdgeKind.MouseUp:
                    ReleaseOneMouse(control, step.Mouse, commands);
                    break;
            }
        }
    }

    private int DelayFor(Binding binding)
    {
        int ms = binding.OverrideDelay ? binding.TapMs : LongPressMs;
        if (ms < 100)
            ms = 100;
        if (ms > 1000)
            ms = 1000;
        return ms;
    }

    private void ArmHold(ControlId control, Binding binding, TimeSpan now)
    {
        if (string.IsNullOrEmpty(binding.LayoutId))
            return;
        var layout = FindLayout(binding.LayoutId);
        if (layout == null)
            return;

        if (!layout.AffectsBindings)
            _scales[control] = layout.PointerScale;
        else
            _holds.Add((control, layout.Id));

        if (binding.Tap != null && binding.Tap.Kind != BindingKind.None)
        {
            _taps[control] = new ArmedTap
            {
                Start = now,
                LimitMs = DelayFor(binding),
                Tap = binding.Tap,
            };
        }
    }

    private void ToggleLayout(string? layoutId)
    {
        if (string.IsNullOrEmpty(layoutId) || layoutId == Base.Id || FindLayout(layoutId) == null)
            return;
        _toggled = _toggled == layoutId ? null : layoutId;
    }

    private Binding? Resolve(ControlId control)
    {
        var activeId = BindingLayoutId();
        if (activeId != Base.Id)
        {
            var gate = Base.Get(control);
            if (gate != null && gate.Kind is BindingKind.LayoutHold or BindingKind.LayoutToggle && gate.LayoutId == activeId)
                return gate;
        }

        var layout = FindLayout(activeId);
        var binding = layout?.Get(control);
        if (binding == null)
            return null;
        if (binding.Kind != BindingKind.UseBase)
            return binding;
        return activeId == Base.Id ? null : Base.Get(control);
    }

    private void Engage(ControlId control, Binding binding, MappingResult result, TimeSpan now)
    {
        switch (binding.Kind)
        {
            case BindingKind.Key:
                foreach (var key in binding.Keys)
                    PressKey(control, key, result.Commands);
                break;
            case BindingKind.Mouse:
                PressMouse(control, binding.Mouse, result.Commands);
                break;
            case BindingKind.Scroll:
                result.Commands.Add(OutputCommand.Wheel(binding.ScrollX, binding.ScrollY) with { OutputId = binding.OutputId });
                _scrollHolds[control] = (binding, now + TimeSpan.FromMilliseconds(45));
                break;
            case BindingKind.Pad:
                foreach (var output in PadTargets(binding))
                    PressPad(control, binding.PadButton, output, result.Commands);
                break;
            case BindingKind.MouseMove:
                result.Commands.Add(OutputCommand.Move(binding.ScrollX, -binding.ScrollY));
                break;
            case BindingKind.StickNudge:
                HoldNudge(control, binding, result.Commands);
                break;
            case BindingKind.Pause:
            case BindingKind.Resume:
                result.Pause = true;
                break;
            case BindingKind.System:
                result.SystemAction = binding.Action;
                result.SystemPath = binding.Path;
                result.Volume = binding.Action == "media.volume-custom" ? binding.ScrollY : -1;
                break;
            case BindingKind.ToggleKeyboard:
                if (!result.ToggleDesktop)
                    result.ToggleKeyboard = true;
                break;
            case BindingKind.Profile:
                result.SwitchProfileId = binding.ProfileId;
                break;
        }
    }

    private void DispatchPointer(in ControllerFrame frame, double dt, TimeSpan now, MappingResult result)
    {
        double scale = PointerScale();
        AccumulateStick(ActiveLeft, frame.LeftX, frame.LeftY, dt, scale, true, result);
        AccumulateStick(ActiveRight, frame.RightX, frame.RightY, dt, scale, false, result);

        var touch = _touch.Update(Sample(frame.Touch0), Sample(frame.Touch1), ActiveTouch, now, dt);
        _fracX += touch.PointerDx * scale;
        _fracY += touch.PointerDy * scale;
        _wheelX += touch.ScrollX;
        _wheelY += touch.ScrollY;
        foreach (var ev in touch.Events)
        {
            var binding = ActiveTouch.GetGesture(ev.Zone, ev.Kind);
            if (binding != null)
                Click(binding, result.Commands);
        }

        var gyro = ActiveGyro;
        bool gyroOn = gyro.Enabled && (gyro.Activator == ControlId.None || _now.Contains(gyro.Activator));
        if (gyroOn)
        {
            double yaw = frame.GyroY / 1024.0;
            double pitch = frame.GyroX / 1024.0;
            if (Math.Abs(yaw) < gyro.Deadzone) yaw = 0;
            if (Math.Abs(pitch) < gyro.Deadzone) pitch = 0;
            if (gyro.InvertX) yaw = -yaw;
            if (gyro.InvertY) pitch = -pitch;
            _fracX += yaw * gyro.Sensitivity * dt * scale;
            _fracY += pitch * gyro.Sensitivity * dt * scale;
        }

        FlushPointer(result);
    }

    private void AccumulateStick(StickSettings stick, byte x, byte y, double dt, double scale, bool left, MappingResult result)
    {
        if (stick.Mode == StickMode.Off)
            return;
        var vector = StickMath.Process(x, y, stick.Deadzone, stick.Curve, stick.HorizontalScale, stick.VerticalScale);
        if (stick.Mode == StickMode.Gamepad)
        {
            int ax = Axis(vector.X);
            int ay = Axis(-vector.Y);
            var stickCommand = left ? OutputCommand.LeftStick(ax, ay) : OutputCommand.RightStick(ax, ay);
            result.Commands.Add(stickCommand with { OutputId = stick.OutputId });
            if (!string.IsNullOrEmpty(stick.OutputId))
                _stickOutputs.Add(stick.OutputId);
            return;
        }

        if (stick.Mode == StickMode.Mouse)
        {
            _fracX += vector.X * stick.Sensitivity * dt * scale;
            _fracY += vector.Y * stick.Sensitivity * dt * scale;
            return;
        }

        _wheelX += vector.X * stick.Sensitivity * dt;
        _wheelY += -vector.Y * stick.Sensitivity * dt;
    }

    private void FlushPointer(MappingResult result)
    {
        int mx = (int)Math.Truncate(_fracX);
        int my = (int)Math.Truncate(_fracY);
        _fracX -= mx;
        _fracY -= my;
        if (mx != 0 || my != 0)
            result.Commands.Add(OutputCommand.Move(mx, my));

        int wx = (int)Math.Truncate(_wheelX);
        int wy = (int)Math.Truncate(_wheelY);
        _wheelX -= wx;
        _wheelY -= wy;
        if (wx != 0 || wy != 0)
            result.Commands.Add(OutputCommand.Wheel(wx, wy));
    }

    private void HandleKeyboard(in ControllerFrame frame, double dt, TimeSpan now, MappingResult result)
    {
        var rising = Rising();
        if (!result.ToggleDesktop)
        {
            foreach (var control in rising)
            {
                if (Base.Get(control)?.Kind == BindingKind.ToggleKeyboard)
                    result.ToggleKeyboard = true;
            }
        }

        if (rising.Contains(ControlId.DPadLeft)) Keyboard.MoveSelection(-1, 0);
        if (rising.Contains(ControlId.DPadRight)) Keyboard.MoveSelection(1, 0);
        if (rising.Contains(ControlId.DPadUp)) Keyboard.MoveSelection(0, -1);
        if (rising.Contains(ControlId.DPadDown)) Keyboard.MoveSelection(0, 1);
        if (rising.Contains(ControlId.Cross)) Keyboard.TypeSelected(result.Commands);
        if (rising.Contains(ControlId.Circle)) Keyboard.Backspace(result.Commands);
        if (rising.Contains(ControlId.Square)) Keyboard.TypeSpace(result.Commands);
        if (rising.Contains(ControlId.Triangle)) Keyboard.TypeEnter(result.Commands);
        if (rising.Contains(ControlId.L1)) Keyboard.CycleSuggestion(1);
        if (rising.Contains(ControlId.R1)) Keyboard.AcceptSuggestion(result.Commands);

        var left = StickMath.Process(frame.LeftX, frame.LeftY, 0.45, 1, 1, 1);
        NudgeSelection(left, now);

        bool resize = frame.L2 >= FrameButtons.SoftThreshold || frame.L2Button;
        bool fade = !resize && (frame.R2 >= FrameButtons.SoftThreshold || frame.R2Button);
        var right = StickMath.Process(frame.RightX, frame.RightY, 0.18, 1, 1, 1);
        if (Math.Abs(right.X) > 0 || Math.Abs(right.Y) > 0)
            Keyboard.Adjust(right.X, right.Y, resize, fade, dt);

        if (frame.Touch0.Active && !_oskTouch)
        {
            Keyboard.TypeAt(frame.Touch0.X / 1919.0, frame.Touch0.Y / 1079.0, result.Commands);
            _oskTouch = true;
        }

        if (!frame.Touch0.Active)
            _oskTouch = false;
    }

    private void NudgeSelection(StickVector left, TimeSpan now)
    {
        int dx = Math.Abs(left.X) > 0.01 ? Math.Sign(left.X) : 0;
        int dy = Math.Abs(left.Y) > 0.01 ? Math.Sign(left.Y) : 0;
        if (dx == 0 && dy == 0)
        {
            _navHeld = false;
            return;
        }

        if (!_navHeld || now >= _navNext)
        {
            Keyboard.MoveSelection(dx, dy);
            _navNext = now + (_navHeld ? TimeSpan.FromMilliseconds(110) : TimeSpan.FromMilliseconds(280));
            _navHeld = true;
        }
    }

    private bool ChordDown()
    {
        if (Profile.ToggleChord == null || Profile.ToggleChord.Count < 2)
            return false;
        foreach (var name in Profile.ToggleChord)
        {
            if (!Enum.TryParse<ControlId>(name, out var id) || id == ControlId.None || !_now.Contains(id))
                return false;
        }

        return true;
    }

    private string BindingLayoutId()
    {
        if (_holds.Count > 0)
            return _holds[^1].LayoutId;
        return string.IsNullOrEmpty(_toggled) ? Base.Id : _toggled;
    }

    private double PointerScale()
    {
        double scale = FindLayout(BindingLayoutId())?.PointerScale ?? 1;
        foreach (var hold in _holds)
            scale = Math.Min(scale, FindLayout(hold.LayoutId)?.PointerScale ?? 1);
        foreach (var value in _scales.Values)
            scale = Math.Min(scale, value);
        return scale <= 0 ? 1 : scale;
    }

    private bool IsLayoutHold(ControlId control) => Base.Get(control)?.Kind == BindingKind.LayoutHold;

    private static bool TogglesPassthrough(Binding? binding)
    {
        if (binding == null)
            return false;
        if (binding.Kind is BindingKind.Pause or BindingKind.Resume)
            return true;
        return HasToggle(binding.DownMacros) || HasToggle(binding.UpMacros);

        static bool HasToggle(List<MacroStep>? steps) =>
            steps?.Any(step => step.Code is "app.pause" or "app.resume") == true;
    }

    private IReadOnlyList<Layout> ActiveLayouts => Layout?.Layouts ?? Profile.Layouts;
    private StickSettings ActiveLeft => Layout?.LeftStick ?? Profile.LeftStick;
    private StickSettings ActiveRight => Layout?.RightStick ?? Profile.RightStick;
    private TouchpadSettings ActiveTouch => Layout?.Touchpad ?? Profile.Touchpad;
    private GyroSettings ActiveGyro => Layout?.Gyro ?? Profile.Gyro;
    private Layout? FindLayout(string? id) =>
        string.IsNullOrEmpty(id) ? null : ActiveLayouts.FirstOrDefault(layout => layout.Id == id);

    private Layout Base =>
        (!string.IsNullOrEmpty(Profile.BaseLayoutId) ? FindLayout(Profile.BaseLayoutId) : null)
        ?? FindLayout("base")
        ?? ActiveLayouts.First();

    private List<ControlId> Rising()
    {
        var list = new List<ControlId>();
        foreach (var id in _now)
        {
            if (!_then.Contains(id))
                list.Add(id);
        }

        return list;
    }

    private List<ControlId> Falling()
    {
        var list = new List<ControlId>();
        foreach (var id in _then)
        {
            if (!_now.Contains(id))
                list.Add(id);
        }

        return list;
    }

    private void Copy()
    {
        _then.Clear();
        foreach (var id in _now)
            _then.Add(id);
    }

    private void PressKey(ControlId control, KeyStroke key, List<OutputCommand> commands)
    {
        if (!_keysByControl.TryGetValue(control, out var list))
            _keysByControl[control] = list = [];
        list.Add(key);
        var id = (key.ScanCode, key.Extended);
        _keyRefs.TryGetValue(id, out int count);
        _keyRefs[id] = count + 1;
        if (count == 0)
            commands.Add(OutputCommand.KeyDown(key));
    }

    private void ReleaseOneKey(ControlId control, KeyStroke key, List<OutputCommand> commands)
    {
        if (!_keysByControl.TryGetValue(control, out var list))
            return;
        int index = list.FindLastIndex(item => item.ScanCode == key.ScanCode && item.Extended == key.Extended);
        if (index < 0)
            return;
        list.RemoveAt(index);
        if (list.Count == 0)
            _keysByControl.Remove(control);
        DropKey(key, commands);
    }

    private void ReleaseKeys(ControlId control, List<OutputCommand> commands)
    {
        if (!_keysByControl.Remove(control, out var list))
            return;
        for (int i = list.Count - 1; i >= 0; i--)
            DropKey(list[i], commands);
    }

    private void DropKey(KeyStroke key, List<OutputCommand> commands)
    {
        var id = (key.ScanCode, key.Extended);
        if (!_keyRefs.TryGetValue(id, out int count) || count <= 0)
            return;
        count--;
        if (count == 0)
        {
            _keyRefs.Remove(id);
            commands.Add(OutputCommand.KeyUp(key));
        }
        else
        {
            _keyRefs[id] = count;
        }
    }

    private void PressMouse(ControlId control, MouseButton button, List<OutputCommand> commands)
    {
        if (!_mouseByControl.TryGetValue(control, out var list))
            _mouseByControl[control] = list = [];
        list.Add(button);
        _mouseRefs.TryGetValue(button, out int count);
        _mouseRefs[button] = count + 1;
        if (count == 0)
            commands.Add(OutputCommand.MouseDown(button));
    }

    private void ReleaseOneMouse(ControlId control, MouseButton button, List<OutputCommand> commands)
    {
        if (!_mouseByControl.TryGetValue(control, out var list))
            return;
        int index = list.LastIndexOf(button);
        if (index < 0)
            return;
        list.RemoveAt(index);
        if (list.Count == 0)
            _mouseByControl.Remove(control);
        DropMouse(button, commands);
    }

    private void ReleaseMouse(ControlId control, List<OutputCommand> commands)
    {
        if (!_mouseByControl.Remove(control, out var list))
            return;
        for (int i = list.Count - 1; i >= 0; i--)
            DropMouse(list[i], commands);
    }

    private void DropMouse(MouseButton button, List<OutputCommand> commands)
    {
        if (!_mouseRefs.TryGetValue(button, out int count) || count <= 0)
            return;
        count--;
        if (count == 0)
        {
            _mouseRefs.Remove(button);
            commands.Add(OutputCommand.MouseUp(button));
        }
        else
        {
            _mouseRefs[button] = count;
        }
    }

    private void PlayMacros(ControlId control, List<MacroStep>? steps, MappingResult result)
    {
        if (steps == null)
            return;
        foreach (var step in steps)
        {
            if (string.IsNullOrEmpty(step.Code))
                continue;
            var action = ButtonCatalog.Create(step.Code);
            ApplyMacro(action, step);
            if (action.Kind is BindingKind.Key or BindingKind.Mouse)
            {
                if (step.Both)
                {
                    PressAction(control, action, result.Commands);
                    ReleaseAction(control, action, result.Commands);
                }
                else if (step.Up)
                {
                    ReleaseAction(control, action, result.Commands);
                }
                else
                {
                    PressAction(control, action, result.Commands);
                }
            }
            else if (action.Kind is BindingKind.LayoutHold or BindingKind.LayoutToggle or BindingKind.ToggleKeyboard)
            {
                Immediate(control, action, _lastTime, result);
            }
            else
            {
                Engage(control, action, result, _lastTime);
            }
        }
    }

    private void PressAction(ControlId control, Binding action, List<OutputCommand> commands)
    {
        if (action.Kind == BindingKind.Key)
        {
            foreach (var key in action.Keys)
                PressKey(control, key, commands);
        }
        else if (action.Kind == BindingKind.Mouse)
        {
            PressMouse(control, action.Mouse, commands);
        }
    }

    private void ReleaseAction(ControlId control, Binding action, List<OutputCommand> commands)
    {
        if (action.Kind == BindingKind.Key)
        {
            foreach (var key in action.Keys)
                ReleaseOneKey(control, key, commands);
        }
        else if (action.Kind == BindingKind.Mouse)
        {
            ReleaseOneMouse(control, action.Mouse, commands);
        }
    }

    private static void ApplyMacro(Binding action, MacroStep step)
    {
        if (step.Code is "mouse.up" or "mouse.down" or "mouse.scroll-left" or "mouse.scroll-right" or "mouse.move" or "media.volume-custom")
        {
            action.ScrollX = step.ScrollX;
            action.ScrollY = step.ScrollY;
        }

        if (step.Code == "app.layer")
        {
            action.Kind = step.LayoutToggle ? BindingKind.LayoutToggle : BindingKind.LayoutHold;
            action.LayoutId = step.LayoutId ?? "";
            action.Action = step.Code;
        }

        if (!string.IsNullOrEmpty(step.ProfileId))
            action.ProfileId = step.ProfileId;
        if (!string.IsNullOrEmpty(step.Path))
            action.Path = step.Path;
        if (step.Targets is { Count: > 0 })
        {
            action.Targets = step.Targets;
            action.OutputId = step.Targets[0];
        }
    }

    private static IEnumerable<string> PadTargets(Binding binding)
    {
        if (binding.Targets is { Count: > 0 })
            return binding.Targets;
        return [binding.OutputId ?? ""];
    }

    private void HoldNudge(ControlId control, Binding binding, List<OutputCommand> commands)
    {
        int x = binding.Action.Contains("-left", StringComparison.Ordinal) ? -32767 : binding.Action.Contains("-right", StringComparison.Ordinal) ? 32767 : 0;
        int y = binding.Action.Contains("-up", StringComparison.Ordinal) ? 32767 : binding.Action.Contains("-down", StringComparison.Ordinal) ? -32767 : 0;
        var outputs = PadTargets(binding).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
        if (outputs.Length == 0)
            outputs = [""];
        _nudges[control] = (binding.ScrollX, x, y, outputs);
        foreach (var output in outputs)
            EmitNudge(binding.ScrollX, x, y, output, commands);
    }

    private void ReleaseNudge(ControlId control, List<OutputCommand> commands)
    {
        if (!_nudges.Remove(control, out var nudge))
            return;
        foreach (var output in nudge.Outputs)
            EmitNudge(nudge.Stick, 0, 0, output, commands);
    }

    private void ApplyNudges(List<OutputCommand> commands)
    {
        foreach (var nudge in _nudges.Values)
        {
            foreach (var output in nudge.Outputs)
                EmitNudge(nudge.Stick, nudge.X, nudge.Y, output, commands);
        }
    }

    private static void EmitNudge(int stick, int x, int y, string output, List<OutputCommand> commands)
    {
        var command = stick == 0 ? OutputCommand.LeftStick(x, y) : OutputCommand.RightStick(x, y);
        commands.Add(command with { OutputId = output });
    }

    private void PressPad(ControlId control, ControlId button, string outputId, List<OutputCommand> commands)
    {
        if (button == ControlId.None)
            return;
        var key = (outputId ?? "", button);
        if (!_padByControl.TryGetValue(control, out var held))
            _padByControl[control] = held = [];
        held.Add((button, key.Item1));
        _padRefs.TryGetValue(key, out int count);
        _padRefs[key] = count + 1;
        if (count == 0)
            commands.Add(OutputCommand.PadDown(button) with { OutputId = key.Item1 });
    }

    private void ReleasePad(ControlId control, List<OutputCommand> commands)
    {
        if (!_padByControl.Remove(control, out var held))
            return;
        foreach (var item in held)
        {
            var key = (item.Output, item.Button);
            if (!_padRefs.TryGetValue(key, out int count) || count <= 0)
                continue;
            count--;
            if (count == 0)
            {
                _padRefs.Remove(key);
                commands.Add(OutputCommand.PadUp(item.Button) with { OutputId = item.Output });
            }
            else
            {
                _padRefs[key] = count;
            }
        }
    }

    private static int Axis(double value) => (int)Math.Clamp(Math.Round(value * 32767), -32767, 32767);

    private static void Click(Binding binding, List<OutputCommand> commands)
    {
        int start = commands.Count;
        switch (binding.Kind)
        {
            case BindingKind.Key:
                foreach (var key in binding.Keys)
                    commands.Add(OutputCommand.KeyDown(key));
                for (int i = binding.Keys.Count - 1; i >= 0; i--)
                    commands.Add(OutputCommand.KeyUp(binding.Keys[i]));
                break;
            case BindingKind.Mouse:
                commands.Add(OutputCommand.MouseDown(binding.Mouse));
                commands.Add(OutputCommand.MouseUp(binding.Mouse));
                break;
            case BindingKind.Scroll:
                commands.Add(OutputCommand.Wheel(binding.ScrollX, binding.ScrollY));
                break;
            case BindingKind.Pad when binding.PadButton != ControlId.None:
                commands.Add(OutputCommand.PadDown(binding.PadButton));
                commands.Add(OutputCommand.PadUp(binding.PadButton));
                break;
        }

        for (int i = start; i < commands.Count; i++)
            commands[i] = commands[i] with { OutputId = binding.OutputId };
    }

    private static TouchSample Sample(in TouchContact contact) => new()
    {
        Active = contact.Active,
        X = contact.X,
        Y = contact.Y,
        Id = contact.Id,
    };

    private sealed class ArmedTap
    {
        public TimeSpan Start;
        public int LimitMs = 220;
        public bool Cancel;
        public Binding Tap = null!;
    }

    private sealed class PendingLong
    {
        public TimeSpan Start;
        public int HoldMs = 350;
        public Binding Short = null!;
        public Binding Long = null!;
    }
}
