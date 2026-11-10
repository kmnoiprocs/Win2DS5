using System.Diagnostics;
using Win2DS5.App.Overlay;
using Win2DS5.App.Theming;
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
    public string LayoutName { get; init; } = "Base";
    public string ActiveLayoutId { get; init; } = "";
    public string Light { get; init; } = "";
    public string ShellColor { get; init; } = "";
    public string ShellImage { get; init; } = "";
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
    private readonly List<ExtraInput> _extras = [];
    private readonly Dictionary<string, AppliedMap> _applied = new(StringComparer.OrdinalIgnoreCase);
    private long _nextExtraScan;
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
    private List<ConfigWarning> _warnings = [];
    private List<string> _connectedKeys = [];
    private long _nextWarningScan;
    private volatile int _accentLight = 0x7EB6FF;
    private int _sentLight = -1;
    private string _liveLayoutId = "";
    private volatile bool _forceLight;

    public DesktopRuntime(ProfileDocument document, string path)
    {
        Document = document;
        _path = path;
        _session = new MappingSession(document.Active)
        {
            DesktopEnabled = document.Settings.DesktopEnabled,
        };
        _session.Keyboard.ApplySettings(document.Settings);
        UseProfile(_session, document.Settings.SelectedInputKey);
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            _session.Keyboard.AttachMemory(Path.Combine(folder, "words.json"));
        _announceGamePad = !document.Settings.DesktopEnabled && Elevation.IsElevated;
        _hidHideInstalled = _hide.IsInstalled();
        if (_hidHideInstalled)
            _hide.RecoverStale();
        else
            _notice = "HidHide is not installed. Games can also see the controller until it is.";
        RefreshWarnings(false);
    }

    public string? FirstWarning
    {
        get
        {
            var warnings = _warnings;
            return warnings.Count == 0 ? null : warnings[0].Message;
        }
    }

    public IReadOnlyList<ConfigWarning> Warnings => _warnings;

    public IReadOnlyList<string> ConnectedKeys => _connectedKeys;

    public void RefreshWarnings(bool toast)
    {
        long now = Environment.TickCount64;
        if (!toast && now < _nextWarningScan)
            return;
        _nextWarningScan = now + 700;
        try
        {
            _connectedKeys = DualSenseConnection.List().Select(item => item.Path).ToList();
        }
        catch (Exception)
        {
            _connectedKeys = [];
        }

        var current = ConfigWarnings.Collect(Document, _connectedKeys).ToList();
        if (toast)
        {
            foreach (var warning in current)
            {
                if (_warnings.All(existing => existing.Message != warning.Message))
                    Toast(warning.Message);
            }
        }

        _warnings = current;
    }

    public ProfileDocument Document { get; }
    public MappingSession Session => _session;

    public string ActiveInputKey =>
        string.IsNullOrEmpty(Document.Settings.SelectedInputKey)
            ? _device?.DevicePath ?? ""
            : Document.Settings.SelectedInputKey;

    public Profile Editing =>
        string.IsNullOrEmpty(ActiveInputKey) ? Document.Active : Document.ProfileFor(ActiveInputKey);

    public void AssignToActive(string profileId)
    {
        var key = ActiveInputKey;
        List<OutputCommand>? released = null;
        lock (_gate)
        {
            if (string.IsNullOrEmpty(key))
            {
                Document.ActiveId = profileId;
            }
            else
            {
                released = _session.ReleaseAll();
                Document.Assign(key, profileId);
                UseProfile(_session, key);
                foreach (var extra in _extras)
                {
                    if (!string.Equals(extra.Device.DevicePath, key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    released.AddRange(extra.Session.ReleaseAll());
                    UseProfile(extra.Session, key);
                }
            }
        }

        if (released != null)
            Emit(released);
        Save();
    }

    public string InputLabel
    {
        get
        {
            var key = ActiveInputKey;
            var match = DualSenseConnection.List().FirstOrDefault(item => item.Path == key);
            return string.IsNullOrEmpty(match.Path) ? "DualSense" : match.Name;
        }
    }

    public void SelectInput(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            return;
        List<OutputCommand>? released = null;
        lock (_gate)
        {
            Document.Settings.SelectedInputKey = devicePath;
            CloseExtra(devicePath);
            if (_device != null && !string.Equals(_device.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase))
            {
                _device.Dispose();
                _device = null;
            }

            released = _session.ReleaseAll();
            UseProfile(_session, devicePath);
        }

        if (released != null)
            Emit(released);
        Save();
    }

    public void AddOutput(OutputDeviceKind kind)
    {
        bool added = false;
        Update(() => added = OutputCatalog.TryAdd(Document.Outputs, kind));
        if (added)
            Save();
    }

    public void RemoveOutput(string id)
    {
        List<OutputCommand>? released = null;
        lock (_gate)
        {
            var output = Document.Outputs.FirstOrDefault(item => item.Id == id);
            if (output == null)
                return;
            released = _session.ReleaseOutput(output);
            foreach (var extra in _extras)
                released.AddRange(extra.Session.ReleaseOutput(output));
        }

        if (released != null)
            Emit(released);
        Update(() => Document.Outputs.RemoveAll(item => item.Id == id));
        Save();
    }
    public event Action<string>? ToastRequested;

    public void SetLightbar(byte r, byte g, byte b)
    {
        _accentLight = (r << 16) | (g << 8) | b;
        RefreshLights();
    }

    public void RefreshLights()
    {
        _nextLightbar = 0;
        _sentLight = -1;
        _forceLight = true;
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
            UseProfile(_session, ActiveInputKey);
        }

        if (released != null)
            Emit(released);
    }

    public void Save()
    {
        lock (_gate)
            ProfileStore.Save(_path, Document);
        RefreshWarnings(true);
    }

    public bool ApplyControllerMap(InputLayout layout, string profileId)
    {
        List<OutputCommand>? released = null;
        lock (_gate)
        {
            string key = ActiveInputKey;
            if (string.IsNullOrEmpty(key))
                key = _device?.DevicePath ?? "";
            if (key.Length == 0)
                return false;
            _applied[key] = new AppliedMap(profileId, layout);
            var session = SessionFor(key);
            if (session != null)
            {
                released = session.ReleaseAll();
                session.Profile = Document.ProfileFor(key);
                session.Layout = layout;
            }
        }

        if (released != null)
            Emit(released);
        return true;
    }

    public void SaveLayoutBindings(string layoutId, Layout draft)
    {
        List<OutputCommand>? released = null;
        lock (_gate)
        {
            var shared = Editing.GetLayout(layoutId);
            if (shared == null)
                return;
            released = _session.ReleaseAll();
            foreach (var extra in _extras)
                released.AddRange(extra.Session.ReleaseAll());
            shared.ReplaceBindings(draft);
            string profileId = Editing.Id;
            foreach (var applied in _applied.Values)
            {
                if (applied.ProfileId != profileId)
                    continue;
                applied.Layout.GetLayout(layoutId)?.ReplaceBindings(draft);
            }

            UseProfile(_session, ActiveInputKey);
        }

        if (released != null)
            Emit(released);
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
            CloseExtras();
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
        bool profileChanged = false;
        List<OutputCommand>? releasedPause = null;
        var extraLights = new List<(DualSenseConnection Device, int Rgb)>();
        lock (_gate)
        {
            _session.LongPressMs = Document.Settings.LongPressMs;
            FitKeyboardTravel();
            result = _session.Step(frame, now);
            _liveLayoutId = result.ActiveLayoutId;
            profileChanged |= ApplyProfileSwitch(_device.DevicePath, result.SwitchProfileId, _session);
            bool refreshExtras = _forceLight;
            _forceLight = false;
            foreach (var extra in _extras)
            {
                if (extra.Device.Disconnected || !extra.Device.TryRead(out var extraFrame))
                    continue;
                UseProfile(extra.Session, extra.Device.DevicePath);
                extra.Session.DesktopEnabled = _session.DesktopEnabled;
                extra.Session.Passthrough = _session.Passthrough;
                extra.Session.LongPressMs = Document.Settings.LongPressMs;
                var extraResult = extra.Session.Step(extraFrame, now);
                profileChanged |= ApplyProfileSwitch(extra.Device.DevicePath, extraResult.SwitchProfileId, extra.Session);
                if (extraResult.ToggleDesktop)
                    result.ToggleDesktop = true;
                if (extraResult.ToggleKeyboard)
                    result.ToggleKeyboard = true;
                if (extraResult.Pause)
                    result.Pause = true;
                result.Commands.AddRange(extraResult.Commands);
                if (_session.DesktopEnabled && (refreshExtras || extraResult.ActiveLayoutId != extra.LightLayout))
                {
                    extra.LightLayout = extraResult.ActiveLayoutId;
                    extraLights.Add((extra.Device, LightRgb(extraResult.ActiveLayoutId)));
                }
            }

            desktop = _session.DesktopEnabled;
            if (result.Pause)
            {
                _session.Passthrough = !_session.Passthrough;
                foreach (var extra in _extras)
                    extra.Session.Passthrough = _session.Passthrough;
                if (_session.Passthrough)
                {
                    releasedPause = _session.ReleaseAll();
                    foreach (var extra in _extras)
                        releasedPause.AddRange(extra.Session.ReleaseAll());
                }
            }
        }

        if (releasedPause != null)
            Emit(releasedPause);
        foreach (var (device, rgb) in extraLights)
            device.SetDesktopFeedback((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0, 0);
        if (profileChanged)
            Save();
        if (!string.IsNullOrEmpty(result.SystemAction))
            RunSystem(result);

        MaintainVirtualPad(desktop && !_session.Passthrough);
        if (result.ToggleDesktop)
            ToggleDesktop();
        else if (result.ToggleKeyboard)
            ToggleKeyboard();
        else if (desktop && !_session.Passthrough)
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

            SyncExtras();
            return;
        }

        DropDevice();
        var preferred = Document.Settings.SelectedInputKey;
        _device = string.IsNullOrWhiteSpace(preferred)
            ? DualSenseConnection.TryOpen()
            : DualSenseConnection.TryOpen(preferred) ?? DualSenseConnection.TryOpen();
        if (_device != null)
        {
            if (string.IsNullOrWhiteSpace(Document.Settings.SelectedInputKey))
                Document.Settings.SelectedInputKey = _device.DevicePath;
            UseProfile(_session, _device.DevicePath);
            _status = _device.IsBluetooth ? "DualSense connected over Bluetooth." : "DualSense connected over USB.";
            SyncExtras();
        }
    }

    private void SyncExtras()
    {
        if (_device == null || Environment.TickCount64 < _nextExtraScan)
            return;
        _nextExtraScan = Environment.TickCount64 + 700;
        var paths = DualSenseConnection.List()
            .Select(item => item.Path)
            .Where(path => path.Length > 0 && !string.Equals(path, _device.DevicePath, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = _extras.Count - 1; i >= 0; i--)
        {
            if (_extras[i].Device.Disconnected || !paths.Contains(_extras[i].Device.DevicePath))
            {
                _extras[i].Device.Dispose();
                _extras.RemoveAt(i);
            }
            else
            {
                paths.Remove(_extras[i].Device.DevicePath);
            }
        }

        foreach (var path in paths)
        {
            var opened = DualSenseConnection.TryOpen(path);
            if (opened == null)
                continue;
            opened.SetReadTimeout(5);
            var extraSession = new MappingSession(Document.ProfileFor(path))
            {
                DesktopEnabled = _session.DesktopEnabled,
            };
            _extras.Add(new ExtraInput(opened, extraSession));
        }
    }

    private void CloseExtra(string path)
    {
        for (int i = _extras.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(_extras[i].Device.DevicePath, path, StringComparison.OrdinalIgnoreCase))
                continue;
            _extras[i].Device.Dispose();
            _extras.RemoveAt(i);
        }
    }

    private void CloseExtras()
    {
        foreach (var extra in _extras)
            extra.Device.Dispose();
        _extras.Clear();
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

        CloseExtras();
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
            CloseExtras();
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
        var filtered = FilterOutputs(commands);
        _emitter.Apply(filtered);
        _pad.Apply(filtered);
    }

    private List<OutputCommand> FilterOutputs(IReadOnlyList<OutputCommand> commands)
    {
        var outputs = Document.Outputs;
        if (outputs.Count == 0)
            return commands.ToList();
        bool mouse = outputs.Any(output => output.Kind == OutputDeviceKind.Mouse);
        bool keyboard = outputs.Any(output => output.Kind == OutputDeviceKind.Keyboard);
        var filtered = new List<OutputCommand>(commands.Count);
        foreach (var command in commands)
        {
            bool allowed = command.Kind switch
            {
                OutputKind.KeyDown or OutputKind.KeyUp => keyboard && Targets(command, outputs),
                OutputKind.MouseDown or OutputKind.MouseUp or OutputKind.MouseMove or OutputKind.Scroll => mouse && Targets(command, outputs),
                OutputKind.PadDown or OutputKind.PadUp or OutputKind.LeftStick or OutputKind.RightStick => OutputCatalog.PadTarget(command, outputs) != null,
                _ => false,
            };
            if (allowed)
                filtered.Add(command);
        }

        return filtered;
    }

    private static bool Targets(OutputCommand command, IReadOnlyList<OutputDevice> outputs) =>
        string.IsNullOrEmpty(command.OutputId) || outputs.Any(output => output.Id == command.OutputId);

    private static void RunSystem(MappingResult result)
    {
        try
        {
            if (result.Volume >= 0)
            {
                SystemVolume.SetPercent(result.Volume);
                return;
            }

            switch (result.SystemAction)
            {
                case "windows.open" when result.SystemPath.Length > 0:
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.SystemPath) { UseShellExecute = true });
                    break;
                case "windows.sleep":
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true, UseShellExecute = false });
                    break;
                case "windows.restart":
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
                    break;
                case "windows.shutdown":
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
                    break;
            }
        }
        catch (Exception)
        {
            // The command could not be started.
        }
    }

    private void MaintainVirtualPad(bool desktop)
    {
        _padNeeded = desktop && Document.Outputs.Any(output => output.Kind is OutputDeviceKind.Xbox or OutputDeviceKind.DualShock4);
        if (!_padNeeded)
        {
            _pad.Disconnect();
            _padReady = false;
            if (_notice == VirtualGamepad.MissingDriver)
                _notice = "";
            return;
        }

        _pad.Sync(Document.Outputs);
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

    private void UseProfile(MappingSession session, string deviceKey)
    {
        session.Profile = Document.ProfileFor(deviceKey);
        if (!string.IsNullOrEmpty(deviceKey)
            && _applied.TryGetValue(deviceKey, out var applied)
            && applied.ProfileId == session.Profile.Id)
        {
            session.Layout = applied.Layout;
            return;
        }

        _applied.Remove(deviceKey);
        session.Layout = null;
    }

    private MappingSession? SessionFor(string deviceKey)
    {
        if (_device != null && string.Equals(_device.DevicePath, deviceKey, StringComparison.OrdinalIgnoreCase))
            return _session;
        return _extras.FirstOrDefault(item => string.Equals(item.Device.DevicePath, deviceKey, StringComparison.OrdinalIgnoreCase))?.Session;
    }

    private sealed class AppliedMap(string profileId, InputLayout layout)
    {
        public string ProfileId { get; } = profileId;
        public InputLayout Layout { get; } = layout;
    }

    private bool ApplyProfileSwitch(string deviceKey, string profileId, MappingSession session)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(deviceKey))
            return false;
        if (Document.Profiles.All(profile => profile.Id != profileId))
            return false;
        Emit(session.ReleaseAll());
        Document.Assign(deviceKey, profileId);
        UseProfile(session, deviceKey);
        return true;
    }

    private int LightRgb(string layoutId)
    {
        if (Theme.TryHex(LightHex(layoutId), out var color))
            return (color.R << 16) | (color.G << 8) | color.B;
        return _accentLight;
    }

    private string LightHex(string layoutId)
    {
        var layout = Document.LayoutLibrary.FirstOrDefault(item => item.Id == layoutId)
            ?? _session.Profile.GetLayout(layoutId)
            ?? _session.Profile.BaseLayout;
        return layout.Light ?? "";
    }

    private sealed class ExtraInput(DualSenseConnection device, MappingSession session)
    {
        public DualSenseConnection Device { get; } = device;
        public MappingSession Session { get; } = session;
        public string LightLayout { get; set; } = "";
    }

    private void MaintainFeedback()
    {
        if (_device == null || !_session.DesktopEnabled)
            return;
        long now = Environment.TickCount64;
        int light = LightRgb(_liveLayoutId);
        bool changed = light != _sentLight;
        byte r = (byte)(light >> 16), g = (byte)(light >> 8), b = (byte)light;
        if (_hapticOn && now >= _hapticUntil)
        {
            _hapticOn = false;
            _sentLight = light;
            _device.SetDesktopFeedback(r, g, b, 0, 0);
            _nextLightbar = now + 2000;
            return;
        }

        if (!changed && now < _nextLightbar)
            return;
        _sentLight = light;
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
                LayoutName = result.ActiveLayoutName,
                ActiveLayoutId = result.ActiveLayoutId,
                Light = LightHex(result.ActiveLayoutId),
                ShellColor = _session.Profile.ShellColor,
                ShellImage = _session.Profile.ShellImage,
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
                LayoutName = _session.Profile.BaseLayout.Name,
                ActiveLayoutId = _session.Profile.BaseLayout.Id,
                Light = _session.Profile.BaseLayout.Light,
                ShellColor = _session.Profile.ShellColor,
                ShellImage = _session.Profile.ShellImage,
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
            CloseExtras();
            _device?.Dispose();
        }
        catch
        {
            // Already gone.
        }

        _device = null;
    }

    public void Notify(string message) => Toast(message);

    private void Toast(string message) => ToastRequested?.Invoke(message);
}
