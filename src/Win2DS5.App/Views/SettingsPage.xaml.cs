using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Win32;
using Win2DS5.App.Theming;
using Win2DS5.App.Updates;
using Win2DS5.Core;
using Win2DS5.Platform;
using Windows.UI;

namespace Win2DS5.App.Views;

public sealed partial class SettingsPage : UserControl
{
    private readonly List<CheckBox> _checks = [];
    private readonly List<(Color Color, Ellipse Ring, Grid Glow)> _swatches = [];
    private bool _loading = true;
    private string _chordProfile = "";
    private bool? _hideShown;
    private Glow? _hideGlow;

    public SettingsPage()
    {
        InitializeComponent();
        BuildSwatches();
        _hideGlow = new Glow(HideGlow, HideDot, 9, 1f, () => ((SolidColorBrush)HideDot.Fill).Color);
        Theme.Changed += SyncAppearance;
        Loaded += (_, _) => LoadPage();
        InstalledCopy.Changed += () => DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded)
                return;
            UpdateStatus.Text = InstalledCopy.Status;
            RestartUpdateButton.Visibility = InstalledCopy.Ready ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    public void RefreshStatus(Runtime.RuntimeSnapshot snapshot)
    {
        if (_chordProfile != App.Runtime.Editing.Id)
            LoadChord();
        bool showDriver = snapshot.VirtualPadNeeded && !snapshot.VirtualPadReady;
        VirtualStatus.Text = showDriver
            ? "ViGEmBus is not installed. Desktop mode cannot send controller buttons to a game until it is."
            : "";
        VirtualStatus.Visibility = showDriver ? Visibility.Visible : Visibility.Collapsed;
        VirtualLink.Visibility = showDriver ? Visibility.Visible : Visibility.Collapsed;
        if (_hideShown == snapshot.HidHideInstalled)
            return;
        _hideShown = snapshot.HidHideInstalled;
        HideStatus.Text = snapshot.HidHideInstalled
            ? "HidHide is installed. Desktop mode can hide the controller from games."
            : "HidHide is not installed. Mapping still works, and games can see the controller too.";
        HideDot.Fill = Theme.Brush(snapshot.HidHideInstalled ? "SuccessBrush" : "AmberBrush");
        HideLink.Visibility = snapshot.HidHideInstalled ? Visibility.Collapsed : Visibility.Visible;
        _hideGlow?.Refresh();
    }

    private void BuildSwatches()
    {
        foreach (var (name, color) in Theme.Accents)
        {
            var root = new Grid { Width = 34, Height = 34, Background = new SolidColorBrush(Colors.Transparent) };
            var glow = new Grid { Opacity = 0, Margin = new Thickness(5) };
            Motion.Fade(glow, 160);
            var fill = new Ellipse { Width = 24, Height = 24, Fill = new SolidColorBrush(color) };
            var ring = new Ellipse { StrokeThickness = 2, Stroke = Theme.Brush("TextBrush"), Opacity = 0 };
            Motion.Fade(ring, 160);
            root.Children.Add(glow);
            root.Children.Add(fill);
            root.Children.Add(ring);
            var glowFill = new Ellipse { Fill = new SolidColorBrush(color) };
            glow.Children.Add(glowFill);
            _ = new Glow(glow, glowFill, 12, 1f, () => color);
            Motion.Lift(root, 1.12f, 0.92f);
            ToolTipService.SetToolTip(root, name);
            root.Tapped += (_, _) => App.SetAppearance(Theme.IsDark, color);
            _swatches.Add((color, ring, glow));
            Swatches.Children.Add(root);
        }
    }

    private void SyncAppearance()
    {
        _loading = true;
        DarkToggle.IsOn = Theme.IsDark;
        ModeGlyph.Glyph = Theme.IsDark ? "\uE708" : "\uE706";
        _loading = false;
        bool preset = false;
        foreach (var (color, ring, glow) in _swatches)
        {
            bool on = Theme.Same(color, Theme.Accent);
            preset |= on;
            ring.Opacity = on ? 1 : 0;
            glow.Opacity = on ? 0.9 : 0;
        }

        var name = Theme.Accents.FirstOrDefault(a => Theme.Same(a.Color, Theme.Accent)).Name;
        AccentName.Text = preset ? $"{name}  ·  {Theme.ToHex(Theme.Accent)}" : $"Custom  ·  {Theme.ToHex(Theme.Accent)}";
        CustomButton.BorderBrush = Theme.Brush(preset ? "LineBrush" : "AccentBrush");
        Picker.Color = Theme.Accent;
    }

    private void OnDarkToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || DarkToggle.IsOn == Theme.IsDark)
            return;
        App.SetAppearance(DarkToggle.IsOn, Theme.Accent);
    }

    private void OnUseCustom(object sender, RoutedEventArgs e)
    {
        CustomFlyout.Hide();
        App.SetAppearance(Theme.IsDark, Picker.Color);
    }

    private void LoadPage()
    {
        _loading = true;
        SyncAppearance();
        _loading = true;
        StartupToggle.IsOn = App.Runtime.Document.Settings.StartWithWindows || StartupRegistration.IsEnabled();
        int delay = Math.Clamp(App.Runtime.Document.Settings.LongPressMs, 100, 1000);
        LongPressSlider.Value = delay;
        LongPressValue.Text = $"{delay} ms";
        if (App.IsElevated)
        {
            AdminCaption.Text = "Win2DS5 is running as administrator, so it can control games that are too, and pass-through can reconnect the controller.";
            AdminButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            AdminCaption.Text = "A game running as administrator ignores this app's mouse. Restarting as administrator fixes that, and pass-through can reconnect the controller for a game that is already open. The approval window itself still ignores the controller. Windows keeps that screen private.";
            AdminButton.Visibility = Visibility.Visible;
        }
        LoadChord();

        SteamWarning.Text = Process.GetProcessesByName("steam").Length > 0
            ? "Steam is running. If Steam Input is enabled for the DualSense, it can take the controller before Win2DS5."
            : "";
        SteamWarning.Visibility = SteamWarning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        GameBarWarning.Text = GameBarOn()
            ? "XBOX Game Bar controller shortcuts are on. It can open over the desktop when you press the PS button."
            : "";
        GameBarWarning.Visibility = GameBarWarning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        VersionText.Text = "Version " + InstalledCopy.CurrentVersion;
        UpdateStatus.Text = InstalledCopy.Status;
        UpdateToggle.IsOn = App.Runtime.Document.Settings.CheckForUpdates;
        UpdateAuto.Visibility = InstalledCopy.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        RestartUpdateButton.Visibility = InstalledCopy.Ready ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
    }

    private void OnRestartAdmin(object sender, RoutedEventArgs e) => App.RelaunchElevated();

    private void OnLongPressDelay(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int ms = (int)Math.Round(LongPressSlider.Value);
        LongPressValue.Text = $"{ms} ms";
        if (_loading)
            return;
        App.Runtime.Update(() => App.Runtime.Document.Settings.LongPressMs = ms);
        App.Runtime.Session.LongPressMs = ms;
        App.Runtime.Save();
    }

    private void OnUpdates(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        bool enabled = UpdateToggle.IsOn;
        App.Runtime.Update(() => App.Runtime.Document.Settings.CheckForUpdates = enabled);
        App.Runtime.Save();
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Checking for a published build…";
        try
        {
            var result = await InstalledCopy.CheckAsync();
            UpdateStatus.Text = result.Message;
            RestartUpdateButton.Visibility = result.Ready ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnRestartUpdate(object sender, RoutedEventArgs e) => App.InstallPendingUpdate();

    private void OnStartup(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        bool enabled = StartupToggle.IsOn;
        StartupRegistration.SetEnabled(enabled);
        App.Runtime.Update(() => App.Runtime.Document.Settings.StartWithWindows = enabled);
        App.Runtime.Save();
    }

    private void OnSaveChord(object sender, RoutedEventArgs e)
    {
        var names = _checks.Where(box => box.IsChecked == true).Select(box => (string)box.Tag).ToList();
        if (names.Count < 2)
        {
            ChordWarning.Text = "Keep at least two controls in the chord.";
            return;
        }

        ChordWarning.Text = "";
        App.Runtime.Update(() => App.Runtime.Editing.ToggleChord = names);
        App.Runtime.Save();
    }

    private void LoadChord()
    {
        bool loading = _loading;
        _loading = true;
        _chordProfile = App.Runtime.Editing.Id;
        ChordList.Children.Clear();
        _checks.Clear();
        var selected = App.Runtime.Editing.ToggleChord.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var control in ControlNames.Bindable)
        {
            var box = new CheckBox
            {
                Content = ControlNames.Display(control),
                Tag = control.ToString(),
                IsChecked = selected.Contains(control.ToString()),
            };
            _checks.Add(box);
            ChordList.Children.Add(box);
        }

        _loading = loading;
    }

    private static bool GameBarOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
            return key?.GetValue("AppCaptureEnabled") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }
}
