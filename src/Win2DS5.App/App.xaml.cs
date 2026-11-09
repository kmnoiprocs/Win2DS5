using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Win2DS5.App.Runtime;
using Win2DS5.App.Updates;
using Win2DS5.App.Theming;
using Win2DS5.Core;
using Win2DS5.Platform;
using Windows.UI;

namespace Win2DS5.App;

public partial class App : Application
{
    private const string MutexName = @"Local\Win2DS5.SingleInstance";

    private static Mutex? _mutex;
    private static MainWindow? _window;
    private readonly ProfileDocument _document;

    public static DesktopRuntime Runtime { get; private set; } = null!;
    public static bool StartHidden { get; private set; }
    public static bool IsElevated => Elevation.IsElevated;

    public App()
    {
        if (InstalledCopy.HandleEarly(Environment.GetCommandLineArgs()))
        {
            _document = new ProfileDocument();
            return;
        }

        UnhandledException += (_, e) => WriteCrash(e.Exception);
        _document = ProfileStore.LoadOrCreate();
        RequestedTheme = IsDark(_document.Settings) ? ApplicationTheme.Dark : ApplicationTheme.Light;
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            WriteCrash(ex);
            throw;
        }
    }

    internal static void WriteCrash(Exception ex)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ProfileStore.DefaultPath)!, "crash.log");
            File.AppendAllText(path, DateTime.Now + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // Nothing else to do while crashing.
        }
    }

    public static void InstallPendingUpdate()
    {
        InstalledCopy.BeginApply();
        try
        {
            Runtime.Dispose();
        }
        catch
        {
            // The updater still replaces the files after this process leaves.
        }

        if (_window != null)
            _window.ExitAfterReplacement();
        Environment.Exit(0);
    }

    public static void RelaunchElevated()
    {
        if (IsElevated || _mutex == null || _window == null)
            return;

        try
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
        catch (ApplicationException)
        {
            return;
        }

        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            ReclaimMutex();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception)
        {
            ReclaimMutex();
            return;
        }
        catch (InvalidOperationException)
        {
            ReclaimMutex();
            return;
        }

        _window.ExitAfterReplacement();
    }

    private static void ReclaimMutex()
    {
        if (_mutex != null)
            return;
        _mutex = new Mutex(true, MutexName, out _);
    }

    public static void SetAppearance(bool dark, Color accent)
    {
        Theme.Apply(dark, accent);
        Runtime.Update(() =>
        {
            Runtime.Document.Settings.Theme = dark ? "Dark" : "Light";
            Runtime.Document.Settings.Accent = Theme.ToHex(Theme.Accent);
        });
        Runtime.Save();
        Runtime.SetLightbar(accent.R, accent.G, accent.B);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            Exit();
            return;
        }

        var settings = _document.Settings;
        if (settings.StartWithWindows)
            StartupRegistration.SetEnabled(true);
        var accent = Theme.Hex(settings.Accent);
        Theme.Apply(IsDark(settings), accent);
        Runtime = new DesktopRuntime(_document, ProfileStore.DefaultPath);
        Runtime.SetLightbar(accent.R, accent.G, accent.B);
        Runtime.Start();
        InstalledCopy.StartWatching(() => _document.Settings.CheckForUpdates);
        StartHidden = Environment.GetCommandLineArgs().Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase));
        _window = new MainWindow();
        if (StartHidden)
            _window.AppWindow.Hide();
        else
            _window.Activate();
    }

    private static bool IsDark(AppSettings settings) =>
        !string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
}
