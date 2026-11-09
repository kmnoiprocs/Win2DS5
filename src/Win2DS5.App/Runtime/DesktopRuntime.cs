using System.Diagnostics;
using Win2DS5.App.Overlay;
using Win2DS5.Core;
using Win2DS5.DualSense;
using Win2DS5.Platform;

namespace Win2DS5.App.Runtime;

public sealed class RuntimeSnapshot
{
    public bool Connected { get; init; }
    public bool Bluetooth { get; init; }
    public bool Edge { get; init; }
    public bool DesktopEnabled { get; init; }
    public bool HidHideInstalled { get; init; }
    public bool KeyboardOpen { get; init; }
    public string LayerName { get; init; } = "Base";
    public int BatteryPercent { get; init; }
    public bool Charging { get; init; }
    public string Status { get; init; } = "Connect a DualSense.";
    public string Notice { get; init; } = "";
    public bool OfferElevation { get; init; }
    public bool VirtualPadNeeded { get; init; }
    public bool VirtualPadReady { get; init; }
    public ControlId[] Pressed { get; init; } = [];
    public bool TouchActive { get; init; }
    public double TouchX { get; init; }
    public double TouchY { get; init; }
    public bool Touch1Active { get; init; }
    public double Touch1X { get; init; }
    public double Touch1Y { get; init; }
    public double LeftX { get; init; }
    public double LeftY { get; init; }
    public double RightX { get; init; }
    public double RightY { get; init; }
    public double L2 { get; init; }
    public double R2 { get; init; }
    public KeyboardPose Keyboard { get; init; } = new();
}

public sealed class KeyboardPose
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double Opacity { get; init; }
    public int Presses { get; init; }
    public int Row { get; init; }
    public int Column { get; init; }
    public int Generation { get; init; }
    public bool SuggestionFocus { get; init; }
    public int SuggestionIndex { get; init; }
    public string Hint { get; init; } = "";
    public List<string> Suggestions { get; init; } = [];
    public List<List<KeyCapView>> Rows { get; init; } = [];
}

public sealed class KeyCapView
{
    public string Label { get; init; } = "";
    public string Super { get; init; } = "";
    public double Span { get; init; } = 1;
    public bool Selected { get; init; }
    public bool Latched { get; init; }
    public bool EmphasizeSuper { get; init; }
}

public sealed class DesktopRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly object _modeGate = new();
    private readonly string _path;
    private readonly MappingSession _session;
    private readonly SendInputEmitter _emitter = new();
    private readonly VirtualGamepad _pad = new();
    private readonly HidHideClient _hide = new();
    private readonly List<string> _lastIds = [];
    private DualSenseConnection? _device;
    private Thread? _thread;
    private volatile bool _stop;
    private bool _cloakApplied;
    private bool _pendingPulse;
    private bool _hapticOn;
    private long _nextCloakAttempt;
    private long _nextLightbar;
    private long _hapticUntil;
    private string _status = "Connect a DualSense.";
    private string _notice = "";
    private string _blockedNotice = "";
    private bool _offerElevation;
    private bool _padNeeded;
    private bool _padReady;
    private bool _announceGamePad;
    private long _nextGateCheck;
    private RuntimeSnapshot _snapshot = new();
    private bool _hidHideInstalled;
    private volatile int _lightbar = 0x7EB6FF;

    public DesktopRuntime(ProfileDocument document, string path)
    {
        Document = document;
        _path = path;
        _session = new MappingSession(document.Active)
        {
            DesktopEnabled = document.Settings.DesktopEnabled,
        };
        _session.Keyboard.ApplySettings(document.Settings);
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            _session.Keyboard.AttachMemory(Path.Combine(folder, "words.json"));
        _announceGamePad = !document.Settings.DesktopEnabled && Elevation.IsElevated;
        _hidHideInstalled = _hide.IsInstalled();
        if (_hidHideInstalled)
            _hide.RecoverStale();
        else
            _notice = "HidHide is not installed. Games can also see the controller until it is.";
    }

    public ProfileDocument Document { get; }
    public MappingSession Session => _session;
    public event Action<string>? ToastRequested;

    public void SetLightbar(byte r, byte g, byte b)
    {
        _lightbar = (r << 16) | (g << 8) | b;
        _nextLightbar = 0;
    }

    public void Start()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "Win2DS5 Input",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public RuntimeSnapshot Capture()
    {
        lock (_gate)
            return _snapshot;
    }

    public void Update(Action action, bool releaseInputs = false)
    {
        List<OutputCommand>? released = null;
        lock (_gate)
        {
            if (releaseInputs)
                released = _session.ReleaseAll();
            action();
            _session.Profile = Document.Active;
        }

        if (released != null)
            Emit(released);
    }

    public void Save()
    {
        lock (_gate)
            ProfileStore.Save(_path, Document);
    }

    public void ToggleDesktop()
    {
        lock (_modeGate)
        {
            bool enable;
            lock (_gate)
            {
                Emit(_session.ReleaseAll());
                enable = !_session.DesktopEnabled;
                _session.DesktopEnabled = enable;
                Document.Settings.DesktopEnabled = enable;
                if (!enable)
                    _session.Keyboard.IsOpen = false;
                ProfileStore.Save(_path, Document);
            }

            if (enable)
            {
                _cloakApplied = false;
                _pendingPulse = true;
                _nextCloakAttempt = 0;
                ApplyCloak();
                Toast("Desktop control");
            }
            else
            {
                HandBack();
                Toast("Passed to the game");
            }
        }
    }

    public void SetDesktop(bool enabled)
    {
        bool current;
        lock (_gate)
            current = _session.DesktopEnabled;
        if (current != enabled)
            ToggleDesktop();
    }

    public void ToggleKeyboard()
    {
        bool open;
        lock (_gate)
        {
            if (!_session.DesktopEnabled)
                return;
            Emit(_session.ReleaseAll());
            _session.Keyboard.IsOpen = !_session.Keyboard.IsOpen;
            open = _session.Keyboard.IsOpen;
            if (open)
                PlaceKeyboardNearPointer();
            else
            {
                _session.Keyboard.WriteSettings(Document.Settings);
                ProfileStore.Save(_path, Document);
            }
        }

        Toast(open ? "Keyboard" : "Keyboard dismissed");
    }

    private void PlaceKeyboardNearPointer()
    {
        var keyboard = _session.Keyboard;
        var cursor = ScreenMetrics.Cursor();
        var work = ScreenMetrics.WorkAreaAt(cursor);
        double scale = ScreenMetrics.ScaleAt(cursor.X, cursor.Y);
        int width = Math.Max(1, (int)Math.Round(keyboard.Width * scale));
        int height = Math.Max(1, (int)Math.Round(keyboard.Height * scale));
        var spot = KeyboardPlacement.NearPointer(cursor, work, width, height);
        keyboard.Left = spot.X;
        keyboard.Top = spot.Y;
    }

    private void FitKeyboardTravel()
    {
        var keyboard = _session.Keyboard;
        var screen = ScreenMetrics.Virtual();
        double scale = ScreenMetrics.ScaleAt((int)Math.Round(keyboard.Left), (int)Math.Round(keyboard.Top));
        keyboard.SetTravel(
            screen.X,
            screen.Y,
            screen.Right,
            screen.Bottom,
            keyboard.Width * scale,
            keyboard.Height * scale);
    }

    public void SetKeyboard(bool open)
    {
        bool current;
        lock (_gate)
            current = _session.Keyboard.IsOpen;
        if (current != open)
            ToggleKeyboard();
    }

    public void Dispose()
    {
        _stop = true;
        try
        {
            _device?.Dispose();
        }
        catch
        {
            // The read thread unblocks when the stream closes.
        }

        _thread?.Join(2000);
        _emitter.ReleaseAll();
        _pad.Dispose();
        if (_cloakApplied)
            _hide.TryUncloak(out _);
        _device?.Dispose();
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        while (!_stop)
        {
            try
            {
                Tick(clock.Elapsed);
            }
            catch (Exception ex)
            {
                _status = ex.Message;
                Thread.Sleep(200);
            }
        }
    }

    private void Tick(TimeSpan now)
    {
        UpdateInputGate();
        EnsureDevice();
        if (_device == null || _device.Disconnected)
        {
            PublishDisconnected();
            Thread.Sleep(250);
            return;
        }

        if (!_device.TryRead(out var frame))
        {
            if (_device.Disconnected)
                DropDevice();
            return;
        }

        if (_pendingPulse && _session.DesktopEnabled)
        {
            _pendingPulse = false;
            _hapticOn = true;
            _hapticUntil = Environment.TickCount64 + 90;
            _nextLightbar = 0;
        }

        MappingResult result;
        bool desktop;
        lock (_gate)
        {
            _session.LongPressMs = Document.Settings.LongPressMs;
            FitKeyboardTravel();
            result = _session.Step(frame, now);
            desktop = _session.DesktopEnabled;
        }

        MaintainVirtualPad(desktop);
        if (result.ToggleDesktop)
            ToggleDesktop();
        else if (result.ToggleKeyboard)
            ToggleKeyboard();
        else if (desktop)
            Emit(result.Commands);

        if (desktop)
            MaintainFeedback();

        Publish(frame, result);
    }

    private void EnsureDevice()
    {
        if (_device is { Disconnected: false, IsOpen: true })
        {
            if (_announceGamePad)
            {
                _announceGamePad = false;
                var ids = DualSenseDevices.InstanceIds().ToList();
                DropDevice();
                if (ids.Count > 0)
                    DeviceCycler.TryCycle(ids, out _, out _);
                return;
            }

            if (_session.DesktopEnabled && !_cloakApplied && Environment.TickCount64 >= _nextCloakAttempt)
            {
                _nextCloakAttempt = Environment.TickCount64 + 3000;
                lock (_modeGate)
                    ApplyCloak();
            }

            return;
        }

        DropDevice();
        _device = DualSenseConnection.TryOpen();
        if (_device != null)
            _status = _device.IsBluetooth ? "DualSense connected over Bluetooth." : "DualSense connected over USB.";
    }

    private void ApplyCloak()
    {
        _hidHideInstalled = _hide.IsInstalled();
        if (!_hidHideInstalled)
        {
            _notice = "HidHide is not installed. Games can also see the controller until it is.";
            _cloakApplied = true;
            _pendingPulse = true;
            return;
        }

        if (!_hide.TryPrepare(out var prepareError))
        {
            _notice = prepareError;
            _cloakApplied = true;
            _pendingPulse = true;
            return;
        }

        var ids = DualSenseDevices.InstanceIds().ToList();
        if (ids.Count == 0 && _device != null)
            ids.Add(DevicePaths.ToInstanceId(_device.DevicePath));
        if (ids.Count == 0)
        {
            _notice = "The controller is not visible to Windows yet.";
            return;
        }

        _lastIds.Clear();
        _lastIds.AddRange(ids);
        if (!_hide.TryCloak(ids, out var cloakError))
        {
            _notice = cloakError;
            _cloakApplied = true;
            _pendingPulse = true;
            return;
        }

        _device?.Dispose();
        _device = null;
        if (!DeviceCycler.TryCycle(ids, out var cycleError, out _) && !string.IsNullOrEmpty(cycleError))
        {
            _notice = cycleError;
            _offerElevation = !Elevation.IsElevated;
        }
        else
        {
            _notice = "";
            _blockedNotice = "";
            _offerElevation = false;
        }
        _cloakApplied = true;
        _pendingPulse = true;
        _nextLightbar = 0;
    }

    private void HandBack()
    {
        try
        {
            _device?.Dispose();
        }
        catch
        {
            // Closing the handle is part of giving the pad back.
        }

        _device = null;
        _cloakApplied = false;
        _hapticOn = false;
        _pad.Disconnect();
        if (!_hide.TryUncloak(out var error))
            _notice = error;
        var ids = DualSenseDevices.InstanceIds().ToList();
        if (ids.Count == 0)
            ids = _lastIds.ToList();
        if (!DeviceCycler.TryCycle(ids, out var cycleError, out bool restarted) && !string.IsNullOrEmpty(cycleError))
        {
            _notice = cycleError;
            _offerElevation = !Elevation.IsElevated;
        }
        else if (!string.IsNullOrEmpty(error))
        {
            _offerElevation = false;
        }
        else if (!restarted)
        {
            _notice = "Restart the game so it can see the controller.";
            _offerElevation = !Elevation.IsElevated;
        }
        else
        {
            _notice = "";
            _blockedNotice = "";
            _offerElevation = false;
        }
    }

    private void UpdateInputGate()
    {
        if (Environment.TickCount64 < _nextGateCheck)
            return;
        _nextGateCheck = Environment.TickCount64 + 400;
        if (Elevation.IsElevated)
            return;

        string blocked = "";
        bool offer = false;
        if (_session.DesktopEnabled)
        {
            if (Elevation.SecureDesktopActive())
                blocked = "Administrator approval is open. The controller can't move on that screen.";
            else if (Elevation.ForegroundIsAboveUs())
            {
                blocked = "This game is running as administrator, so the controller can't click it.";
                offer = true;
            }
            else if (Elevation.ForegroundIsUs() && _blockedNotice.StartsWith("This game", StringComparison.Ordinal))
            {
                blocked = _blockedNotice;
                offer = true;
            }
        }

        if (blocked == _blockedNotice)
            return;
        if (string.IsNullOrEmpty(blocked))
        {
            if (_notice == _blockedNotice)
            {
                _notice = "";
                _offerElevation = false;
            }
        }
        else
        {
            _notice = blocked;
            _offerElevation = offer;
        }

        _blockedNotice = blocked;
    }

    private void Emit(IReadOnlyList<OutputCommand> commands)
    {
        _emitter.Apply(commands);
        _pad.Apply(commands);
    }

    private void MaintainVirtualPad(bool desktop)
    {
        _padNeeded = desktop && ProfileUsesPad();
        if (!_padNeeded)
        {
            _pad.Disconnect();
            _padReady = false;
            if (_notice == VirtualGamepad.MissingDriver)
                _notice = "";
            return;
        }

        _pad.EnsureConnected();
        _padReady = _pad.IsAvailable;
        if (!_padReady)
        {
            if (string.IsNullOrEmpty(_notice) || _notice == VirtualGamepad.MissingDriver)
                _notice = VirtualGamepad.MissingDriver;
        }
        else if (_notice == VirtualGamepad.MissingDriver)
        {
            _notice = "";
        }
    }

    private bool ProfileUsesPad()
    {
        var profile = _session.Profile;
        if (profile.LeftStick.Mode == StickMode.Gamepad || profile.RightStick.Mode == StickMode.Gamepad)
            return true;
        foreach (var layer in profile.Layers)
        {
            foreach (var binding in layer.Bindings.Values)
            {
                if (ContainsPad(binding))
                    return true;
            }
        }

        foreach (var gesture in profile.Touchpad.Gestures.Values)
        {
            if (ContainsPad(gesture))
                return true;
        }

        return false;
    }

    private static bool ContainsPad(Binding? binding)
    {
        if (binding == null)
            return false;
        if (binding.Kind == BindingKind.Pad)
            return true;
        return ContainsPad(binding.Tap);
    }

    private void MaintainFeedback()
    {
        if (_device == null || !_session.DesktopEnabled)
            return;
        long now = Environment.TickCount64;
        int light = _lightbar;
        byte r = (byte)(light >> 16), g = (byte)(light >> 8), b = (byte)light;
        if (_hapticOn && now >= _hapticUntil)
        {
            _hapticOn = false;
            _device.SetDesktopFeedback(r, g, b, 0, 0);
            _nextLightbar = now + 2000;
            return;
        }

        if (now < _nextLightbar)
            return;
        _nextLightbar = now + 2000;
        byte motor = _hapticOn ? (byte)56 : (byte)0;
        _device.SetDesktopFeedback(r, g, b, motor, motor);
    }

    private void Publish(in ControllerFrame frame, MappingResult result)
    {
        var pressed = new HashSet<ControlId>();
        FrameButtons.Collect(frame, pressed);
        lock (_gate)
        {
            _snapshot = new RuntimeSnapshot
            {
                Connected = true,
                Bluetooth = frame.Bluetooth || (_device?.IsBluetooth ?? false),
                Edge = frame.IsEdge,
                DesktopEnabled = _session.DesktopEnabled,
                HidHideInstalled = _hidHideInstalled,
                KeyboardOpen = _session.Keyboard.IsOpen,
                LayerName = result.ActiveLayerName,
                BatteryPercent = frame.BatteryPercent,
                Charging = frame.Charging,
                Status = _status,
                Notice = _notice,
                OfferElevation = _offerElevation,
                VirtualPadNeeded = _padNeeded,
                VirtualPadReady = _padReady,
                Pressed = pressed.ToArray(),
                TouchActive = frame.Touch0.Active,
                TouchX = frame.Touch0.X / 1919.0,
                TouchY = frame.Touch0.Y / 1079.0,
                Touch1Active = frame.Touch1.Active,
                Touch1X = frame.Touch1.X / 1919.0,
                Touch1Y = frame.Touch1.Y / 1079.0,
                LeftX = (frame.LeftX - 127.5) / 127.5,
                LeftY = (frame.LeftY - 127.5) / 127.5,
                RightX = (frame.RightX - 127.5) / 127.5,
                RightY = (frame.RightY - 127.5) / 127.5,
                L2 = frame.L2 / 255.0,
                R2 = frame.R2 / 255.0,
                Keyboard = Pose(),
            };
        }
    }

    private void PublishDisconnected()
    {
        lock (_gate)
        {
            _snapshot = new RuntimeSnapshot
            {
                Connected = false,
                DesktopEnabled = _session.DesktopEnabled,
                HidHideInstalled = _hidHideInstalled,
                KeyboardOpen = _session.Keyboard.IsOpen,
                LayerName = _session.Profile.GetLayer("base")?.Name ?? "Base",
                Status = _device == null ? "Connect a DualSense." : _status,
                Notice = _notice,
                OfferElevation = _offerElevation,
                VirtualPadNeeded = _padNeeded,
                VirtualPadReady = _padReady,
                Keyboard = Pose(),
            };
        }
    }

    private KeyboardPose Pose()
    {
        var keyboard = _session.Keyboard;
        var rows = new List<List<KeyCapView>>();
        var current = keyboard.Current;
        for (int r = 0; r < current.Count; r++)
        {
            var row = new List<KeyCapView>();
            for (int c = 0; c < current[r].Count; c++)
            {
                var cell = current[r][c];
                keyboard.Describe(cell, out var label, out var super, out var latched, out var emphasize);
                row.Add(new KeyCapView
                {
                    Label = label,
                    Super = super,
                    Span = cell.Span,
                    Selected = keyboard.Row == r && keyboard.Column == c,
                    Latched = latched,
                    EmphasizeSuper = emphasize,
                });
            }

            rows.Add(row);
        }

        return new KeyboardPose
        {
            Left = keyboard.Left,
            Top = keyboard.Top,
            Width = keyboard.Width,
            Height = keyboard.Height,
            Opacity = keyboard.Opacity,
            Presses = keyboard.Presses,
            Row = keyboard.Row,
            Column = keyboard.Column,
            Generation = keyboard.LayoutGeneration,
            SuggestionFocus = keyboard.Row < 0,
            SuggestionIndex = keyboard.SuggestionIndex,
            Hint = KeyboardModel.ControlsHint,
            Suggestions = keyboard.Suggestions.ToList(),
            Rows = rows,
        };
    }

    private void DropDevice()
    {
        try
        {
            _device?.Dispose();
        }
        catch
        {
            // Already gone.
        }

        _device = null;
    }

    private void Toast(string message) => ToastRequested?.Invoke(message);
}
