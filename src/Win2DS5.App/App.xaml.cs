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
    public static MainWindow Window => _window!;
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

    public static void ResetToStock()
    {
        try
        {
            Runtime?.Dispose();
        }
        catch
        {
            // Profiles are rewritten on disk either way.
        }

        ShellFiles.Clear();
        ProfileStore.ResetToStock();
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
        }
        catch (ApplicationException)
        {
        }

        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (_window != null)
            _window.ExitAfterReplacement();
        Environment.Exit(0);
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

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
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
        StartHidden = Environment.GetCommandLineArgs().Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase));
        UpdateSplash? splash = null;
        bool install = false;
        try
        {
            bool check = settings.CheckForUpdates && InstalledCopy.IsInstalled;
            if (check || InstalledCopy.PendingReady())
            {
                splash = new UpdateSplash();
                splash.Activate();
                install = await InstalledCopy.RunAtStartupAsync(splash.Set, check);
            }
        }
        catch (Exception ex)
        {
            WriteCrash(ex);
            install = false;
        }

        if (install)
        {
            try
            {
                splash?.Set("Installing the new build…");
            }
            catch (Exception ex)
            {
                WriteCrash(ex);
            }

            await Task.Delay(400);
            InstalledCopy.BeginApply();
            Environment.Exit(0);
            return;
        }

        try
        {
            if (splash != null)
                await OnWindowThread(splash, OpenStudio);
            else
                OpenStudio();
        }
        catch (Exception ex)
        {
            WriteCrash(ex);
            if (_window == null)
                OpenStudio();
        }

        if (splash != null)
        {
            await Task.Delay(700);
            try
            {
                await OnWindowThread(splash, splash.Close);
            }
            catch (Exception ex)
            {
                WriteCrash(ex);
            }
        }
    }

    private void OpenStudio()
    {
        if (_window != null)
            return;
        var accent = Theme.Accent;
        Runtime = new DesktopRuntime(_document, ProfileStore.DefaultPath);
        Runtime.SetLightbar(accent.R, accent.G, accent.B);
        Runtime.Start();
        InstalledCopy.StartWatching(() => _document.Settings.CheckForUpdates);
        _window = new MainWindow();
        if (StartHidden)
            _window.AppWindow.Hide();
        else
            _window.Activate();
    }

    private static Task OnWindowThread(Window window, Action action)
    {
        var queue = window.DispatcherQueue;
        if (queue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(() =>
        {
            try
            {
                action();
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }))
        {
            done.TrySetException(new InvalidOperationException("Win2DS5 lost its window."));
        }

        return done.Task;
    }

    private static bool IsDark(AppSettings settings) =>
        !string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
}
