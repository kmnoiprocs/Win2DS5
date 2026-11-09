using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Win2DS5.App.Overlay;
using Win2DS5.App.Runtime;
using Win2DS5.App.Updates;
using Win2DS5.App.Theming;
using Win2DS5.App.Tray;
using Windows.UI;
using WinRT.Interop;

namespace Win2DS5.App;

public sealed partial class MainWindow : Window
{
    private const double RailStep = 46;

    private readonly TrayIcon _tray = new();
    private readonly KeyboardWindow _keyboard = new();
    private readonly ToastWindow _toast = new();
    private readonly DispatcherTimer _timer;
    private readonly List<Button> _rail = [];
    private static readonly SubclassProc SubclassDelegate = Subclass;
    private GCHandle _self;
    private bool _allowExit;
    private bool _trayReady;
    private bool _keyboardShown;
    private int _statusMode = -1;
    private bool? _desktopShown;
    private int _battery = -1;
    private Glow? _statusGlow;
    private IntPtr _windowIcon;
    private (byte R, byte G, byte B) _windowIconAccent;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1240, 820));
        AppWindow.Closing += OnClosing;
        Root.RequestedTheme = Theme.ElementTheme;
        BuildGlows();
        BuildRail();
        Theme.Changed += OnThemeChanged;
        OnThemeChanged();
        Profiles.ProfileChanged += () => Controls.Apply(App.Runtime.Capture());
        App.Runtime.ToastRequested += message => DispatcherQueue.TryEnqueue(() => _toast.Show(message));
        var updateAnnounced = false;
        InstalledCopy.Changed += () => DispatcherQueue.TryEnqueue(() =>
        {
            if (!InstalledCopy.Ready)
            {
                updateAnnounced = false;
                return;
            }

            if (updateAnnounced)
                return;
            updateAnnounced = true;
            _toast.Show("Update downloaded. It installs the next time Win2DS5 starts.");
        });
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        EnsureTray();
    }

    private void BuildGlows()
    {
        _ = new Glow(LogoGlow, LogoMark, 16, 0.8f);
        _ = new Glow(RailBarGlow, RailBar, 10);
        _ = new Glow(BatteryGlow, BatteryBar, 8, 0.8f);
        _statusGlow = new Glow(StatusGlow, StatusDot, 9, 1f, () => ((SolidColorBrush)StatusDot.Fill).Color);
        RailIndicator.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(260) };
        Motion.Fade(NoticePanel, 200);
    }

    private void EnsureTray()
    {
        if (_trayReady)
            return;
        _trayReady = true;
        var hwnd = WindowNative.GetWindowHandle(this);
        _self = GCHandle.Alloc(this);
        SetWindowSubclass(hwnd, SubclassDelegate, 1, (nuint)(nint)GCHandle.ToIntPtr(_self));
        _tray.Add(hwnd);
        _tray.OpenRequested += () => DispatcherQueue.TryEnqueue(ShowStudio);
        _tray.ToggleRequested += () => App.Runtime.ToggleDesktop();
        _tray.KeyboardRequested += () => App.Runtime.ToggleKeyboard();
        _tray.ExitRequested += () => DispatcherQueue.TryEnqueue(ExitApp);
    }

    private void BuildRail()
    {
        AddRail("Controls", "\uE7FC", Controls);
        AddRail("Touchpad", "\uE7C9", Touchpad);
        AddRail("Keyboard", "\uE765", Keyboard);
        AddRail("Profiles", "\uE716", Profiles);
        AddRail("Settings", "\uE713", Settings);
        SelectRail(_rail[0], animate: false);
    }

    private void AddRail(string label, string glyph, UIElement page)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Brush("MutedBrush") });
        var button = new Button
        {
            Content = content,
            Tag = page,
            Style = (Style)Application.Current.Resources["RailButtonStyle"],
        };
        button.Click += (_, _) => SelectRail(button, animate: true);
        _rail.Add(button);
        Rail.Children.Add(button);
    }

    private void SelectRail(Button selected, bool animate)
    {
        for (int i = 0; i < _rail.Count; i++)
        {
            var button = _rail[i];
            bool on = button == selected;
            var brush = Theme.Brush(on ? "AccentTextBrush" : "MutedBrush");
            if (button.Content is StackPanel panel)
            {
                ((FontIcon)panel.Children[0]).Foreground = brush;
                var text = (TextBlock)panel.Children[1];
                text.Foreground = on ? Theme.Brush("TextBrush") : brush;
                text.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }

            if (on)
                RailIndicator.Translation = new Vector3(0, (float)(i * RailStep), 0);
            if (button.Tag is not UIElement page)
                continue;
            if (on)
            {
                if (page.Visibility != Visibility.Visible || !animate)
                {
                    if (animate)
                        Motion.Enter(page);
                    else
                        page.Visibility = Visibility.Visible;
                }
            }
            else
            {
                page.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Refresh()
    {
        var snapshot = App.Runtime.Capture();
        if (snapshot.KeyboardOpen)
        {
            _keyboard.Apply(snapshot.Keyboard);
            _keyboardShown = true;
        }
        else if (_keyboardShown)
        {
            _keyboard.Dismiss();
            _keyboardShown = false;
        }

        if (!AppWindow.IsVisible)
            return;

        if (Controls.Visibility == Visibility.Visible)
            Controls.Apply(snapshot);
        if (Touchpad.Visibility == Visibility.Visible)
            Touchpad.Apply(snapshot);
        Settings.RefreshStatus(snapshot);
        RefreshMode(snapshot.DesktopEnabled);
        RefreshDevice(snapshot);

        var link = snapshot.Connected
            ? (snapshot.Bluetooth ? "Bluetooth" : "USB") + (snapshot.Edge ? "  ·  DualSense Edge" : "")
            : "No controller";
        var status = $"{link}  ·  {snapshot.LayerName} layer  ·  {(snapshot.DesktopEnabled ? "Desktop control" : "Passed to the game")}";
        if (StatusText.Text != status)
            StatusText.Text = status;
        if (NoticeText.Text != snapshot.Notice)
        {
            NoticeText.Text = snapshot.Notice;
            NoticePanel.Opacity = string.IsNullOrEmpty(snapshot.Notice) ? 0 : 1;
        }

        var elevate = snapshot.OfferElevation ? Visibility.Visible : Visibility.Collapsed;
        if (ElevateButton.Visibility != elevate)
            ElevateButton.Visibility = elevate;

        int mode = !snapshot.Connected ? 0 : snapshot.DesktopEnabled ? 2 : 1;
        if (mode != _statusMode)
        {
            _statusMode = mode;
            StatusDot.Fill = Theme.Brush(mode switch { 2 => "SuccessBrush", 1 => "AmberBrush", _ => "MutedBrush" });
            StatusGlow.Opacity = mode == 0 ? 0 : 1;
            _statusGlow?.Refresh();
        }
    }

    private void RefreshMode(bool desktop)
    {
        if (_desktopShown == desktop)
            return;
        _desktopShown = desktop;
        ModeCaption.Text = desktop ? "Desktop control" : "Passed to the game";
        ModeCaption.Foreground = Theme.Brush(desktop ? "AccentTextBrush" : "AmberBrush");
        ModeDot.Fill = Theme.Brush(desktop ? "AccentBrush" : "AmberBrush");
        PulseRing.Fill = ModeDot.Fill;
        ModeChip.Background = Theme.Brush(desktop ? "AccentSoftBrush" : "PanelAltBrush");
        ModeChip.BorderBrush = Theme.Brush(desktop ? "AccentHaloBrush" : "AmberHaloBrush");
        ToolTipService.SetToolTip(ModeChip, desktop ? "Hand the controller back to games" : "Take the controller for the desktop");
        if (desktop)
            PulseStory.Begin();
        else
            PulseStory.Stop();
    }

    private void RefreshDevice(RuntimeSnapshot snapshot)
    {
        int battery = snapshot.Connected ? snapshot.BatteryPercent + (snapshot.Charging ? 1000 : 0) : -1;
        var name = !snapshot.Connected ? "Not connected" : snapshot.Edge ? "DualSense Edge" : "DualSense";
        if (DeviceName.Text != name)
            DeviceName.Text = name;
        var glyph = !snapshot.Connected ? "\uE839" : snapshot.Bluetooth ? "\uE702" : "\uECF0";
        if (LinkGlyph.Glyph != glyph)
        {
            LinkGlyph.Glyph = glyph;
            LinkGlyph.Foreground = Theme.Brush(snapshot.Connected ? "AccentTextBrush" : "MutedBrush");
        }

        var profile = "Profile  ·  " + App.Runtime.Document.Active.Name;
        if (ProfileName.Text != profile)
            ProfileName.Text = profile;
        int key = HashCode.Combine(battery, (int)BatteryTrack.ActualWidth);
        if (key == _battery)
            return;
        _battery = key;
        double percent = snapshot.Connected ? Math.Clamp(snapshot.BatteryPercent, 0, 100) : 0;
        BatteryFill.Width = Math.Max(0, BatteryTrack.ActualWidth * percent / 100.0);
        BatteryText.Text = snapshot.Connected ? $"{snapshot.BatteryPercent}%" + (snapshot.Charging ? " ⚡" : "") : "--";
        BatteryBar.Fill = Theme.Brush(snapshot.Connected && percent <= 15 && !snapshot.Charging ? "AmberBrush" : "AccentGradientBrush");
    }

    private void OnModeChip(object sender, RoutedEventArgs e) => Task.Run(() => App.Runtime.ToggleDesktop());

    private void OnElevate(object sender, RoutedEventArgs e) => App.RelaunchElevated();

    public void ExitAfterReplacement()
    {
        _allowExit = true;
        _timer.Stop();
        _keyboard.Dismiss();
        try
        {
            _tray.Remove(WindowNative.GetWindowHandle(this));
        }
        catch
        {
            // The process is leaving either way.
        }

        Environment.Exit(0);
    }

    private void OnThemeButton(object sender, RoutedEventArgs e) => App.SetAppearance(!Theme.IsDark, Theme.Accent);

    private void OnThemeChanged()
    {
        var target = Theme.ElementTheme;
        Root.RequestedTheme = target == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Root.RequestedTheme = target;
        ThemeGlyph.Glyph = Theme.IsDark ? "\uE708" : "\uE706";
        ToolTipService.SetToolTip(ThemeButton, Theme.IsDark ? "Switch to light mode" : "Switch to dark mode");
        var bar = AppWindow.TitleBar;
        var text = Theme.Current.Text;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = text;
        bar.ButtonInactiveForegroundColor = Theme.Current.Muted;
        bar.ButtonHoverBackgroundColor = Theme.Current.Hover;
        bar.ButtonHoverForegroundColor = text;
        bar.ButtonPressedBackgroundColor = Theme.Alpha(Theme.Accent, 0x40);
        bar.ButtonPressedForegroundColor = text;
        _desktopShown = null;
        _statusMode = -1;
        _battery = -1;
        foreach (var button in _rail.Where(b => b.Tag is UIElement { Visibility: Visibility.Visible }))
            SelectRail(button, animate: false);
        _tray.SetAccent(Theme.Accent);
        ApplyWindowIcon();
        _keyboard.RefreshTheme();
        _toast.RefreshTheme();
    }

    private void ApplyWindowIcon()
    {
        var color = Theme.Accent;
        if (_windowIcon != IntPtr.Zero && _windowIconAccent == (color.R, color.G, color.B))
            return;
        _windowIconAccent = (color.R, color.G, color.B);
        var icon = LogoArt.CreateIcon(color.R, color.G, color.B, 256);
        AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(icon));
        if (_windowIcon != IntPtr.Zero)
            LogoArt.Destroy(_windowIcon);
        _windowIcon = icon;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowExit)
            return;
        args.Cancel = true;
        AppWindow.Hide();
        _toast.Show("Win2DS5 is still running in the tray.");
    }

    private void ShowStudio()
    {
        AppWindow.Show();
        Activate();
    }

    private void ExitApp()
    {
        _allowExit = true;
        _timer.Stop();
        try
        {
            _keyboard.Dismiss();
            _tray.Remove(WindowNative.GetWindowHandle(this));
            App.Runtime.Dispose();
        }
        catch
        {
            // Leaving the process is the part that matters.
        }

        Environment.Exit(0);
    }

    private static IntPtr Subclass(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (message == TrayIcon.CallbackMessage && GCHandle.FromIntPtr((IntPtr)data).Target is MainWindow window)
        {
            window._tray.Handle(hwnd, lParam);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
