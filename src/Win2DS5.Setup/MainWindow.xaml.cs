using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Win2DS5.Setup;

public partial class MainWindow : Window
{
    private int _step;
    private bool _busy;
    private InstallResult? _result;

    public MainWindow()
    {
        InitializeComponent();
        FolderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Win2DS5");
        if (!SetupEngine.HasPayload)
        {
            FolderError.Text = "This setup file does not contain Win2DS5. Build it with scripts\\Publish-Release.ps1.";
            FolderError.Visibility = Visibility.Visible;
            NextButton.IsEnabled = false;
        }

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        };
        Closing += (_, e) =>
        {
            if (_busy)
                e.Cancel = true;
        };
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Install Win2DS5",
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) == true)
            FolderBox.Text = dialog.FolderName;
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
            Show(0);
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            if (!SetupEngine.TryNormalize(FolderBox.Text, out var path, out var error))
            {
                FolderError.Text = error;
                FolderError.Visibility = Visibility.Visible;
                return;
            }

            FolderBox.Text = path;
            FolderError.Visibility = Visibility.Collapsed;
            PrepareDrivers();
            Show(1);
            return;
        }

        if (_step == 1)
        {
            Show(2);
            await RunInstall();
            return;
        }

        if (_result is { Succeeded: true, CanLaunch: true } && LaunchBox.IsChecked == true && _result.ExePath.Length > 0)
            SetupEngine.Launch(_result.ExePath);
        Close();
    }

    private void PrepareDrivers()
    {
        bool hid = DriverSetup.HidHideInstalled();
        bool pad = DriverSetup.ViGEmInstalled();
        HidHideState.Text = hid
            ? "Already installed. Leave this off unless you want to run its setup again."
            : "Not installed.";
        ViGEmState.Text = pad
            ? "Already installed. Leave this off unless you want to run its setup again."
            : "Not installed.";
        HidHideBox.IsChecked = !hid;
        ViGEmBox.IsChecked = !pad;
    }

    private async Task RunInstall()
    {
        _busy = true;
        BackButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        Log.Clear();
        var progress = new Progress<string>(line =>
        {
            Log.AppendText(line + Environment.NewLine);
            Log.ScrollToEnd();
        });
        _result = await SetupEngine.InstallAsync(new InstallRequest
        {
            Directory = FolderBox.Text.Trim(),
            DesktopShortcut = DesktopBox.IsChecked == true,
            StartMenuShortcut = StartBox.IsChecked == true,
            PinTaskbar = TaskbarBox.IsChecked == true,
            HidHide = HidHideBox.IsChecked == true,
            ViGEmBus = ViGEmBox.IsChecked == true,
        }, progress, CancellationToken.None);
        _busy = false;
        DoneSummary.Text = _result.Summary;
        DoneDetails.Text = _result.Details;
        LaunchBox.Visibility = _result.CanLaunch ? Visibility.Visible : Visibility.Collapsed;
        LaunchBox.IsChecked = _result.CanLaunch;
        Progress.IsIndeterminate = false;
        Progress.Value = 100;
        Show(3);
    }

    private void Show(int step)
    {
        _step = step;
        PlacePage.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        DriverPage.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ProgressPage.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        DonePage.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = step == 1;
        NextButton.IsEnabled = true;
        NextButton.Content = step switch
        {
            0 => "Continue",
            1 => "Install",
            _ => "Close",
        };
        Heading.Text = step switch
        {
            0 => "Install Win2DS5",
            1 => "Recommended drivers",
            2 => "Installing",
            _ => _result?.Succeeded == true ? "Installed" : "Setup stopped",
        };
        Subheading.Text = step switch
        {
            0 => "Choose where it goes, and which icons you want.",
            1 => "HidHide hides the real controller from games. ViGEmBus creates the virtual Xbox pad.",
            2 => "This can take a few minutes if a driver is downloading.",
            _ => "Version " + SetupEngine.Version,
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
