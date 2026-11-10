using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using Win2DS5.Core;

namespace Win2DS5.Setup;

internal sealed class InstallRequest
{
    public required string Directory { get; init; }
    public bool DesktopShortcut { get; init; }
    public bool StartMenuShortcut { get; init; }
    public bool PinTaskbar { get; init; }
    public bool HidHide { get; init; }
    public bool ViGEmBus { get; init; }
}

internal sealed class InstallResult
{
    public bool Succeeded { get; init; }
    public string Summary { get; init; } = "";
    public string Details { get; init; } = "";
    public bool CanLaunch { get; init; }
    public bool NeedsRestart { get; init; }
    public string RestartMessage { get; init; } = "";
    public string ExePath { get; init; } = "";
}

internal static class SetupEngine
{
    public const string PayloadName = "Win2DS5.Setup.payload.zip";

    public static bool HasPayload => OpenPayload() != null;

    public static string Version
    {
        get
        {
            var info = typeof(SetupEngine).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
    }

    public static bool TryNormalize(string text, out string path, out string error)
    {
        path = "";
        error = "";
        text = text.Trim().Trim('"');
        if (text.Length == 0)
        {
            error = "Choose a folder.";
            return false;
        }

        if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "That path has a character Windows does not allow.";
            return false;
        }

        try
        {
            if (!Path.IsPathRooted(text))
            {
                error = @"Choose a full path, such as C:\Program Files\Win2DS5.";
                return false;
            }

            path = Path.GetFullPath(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "That folder path is not valid.";
            return false;
        }

        var root = Path.GetPathRoot(path);
        if (root != null && string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            error = "Choose a folder for Win2DS5, not the whole drive.";
            return false;
        }

        return true;
    }

    public static async Task<InstallResult> InstallAsync(InstallRequest request, IProgress<string> log, CancellationToken cancellation)
    {
        var notes = new List<string>();
        var payload = OpenPayload();
        if (payload == null)
        {
            return Failed("This setup file does not contain Win2DS5. Build it with scripts\\Publish-Release.ps1.");
        }

        var stage = Path.Combine(Path.GetTempPath(), "Win2DS5-payload-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(Path.GetTempPath(), "Win2DS5-payload.zip");
        try
        {
            log.Report("Preparing files…");
            await using (payload)
            await using (var output = File.Create(zipPath))
                await payload.CopyToAsync(output, cancellation);
            Directory.CreateDirectory(stage);
            ZipFile.ExtractToDirectory(zipPath, stage, true);
            cancellation.ThrowIfCancellationRequested();

            if (AppIsRunningFrom(request.Directory))
                return Failed("Win2DS5 is open in that folder. Close it, then run the installer again.");

            log.Report("Copying Win2DS5…");
            Directory.CreateDirectory(request.Directory);
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
            {
                cancellation.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(stage, file);
                if (relative.Contains("..", StringComparison.Ordinal))
                    continue;
                var destination = Path.Combine(request.Directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
                count++;
            }

            var exe = Path.Combine(request.Directory, "Win2DS5.exe");
            if (!File.Exists(exe))
                return Failed("The published files did not include Win2DS5.exe.");

            string desktop = "";
            string startMenu = "";
            if (request.DesktopShortcut)
            {
                desktop = CreateShortcut(exe, Environment.SpecialFolder.DesktopDirectory, null);
                notes.Add("Desktop shortcut created.");
            }

            if (request.StartMenuShortcut)
            {
                startMenu = CreateShortcut(exe, Environment.SpecialFolder.StartMenu, "Programs");
                notes.Add("Start menu shortcut created.");
            }

            var marker = new InstallMarker
            {
                Installed = true,
                DesktopShortcut = desktop,
                StartMenuShortcut = startMenu,
            };
            await File.WriteAllTextAsync(
                Path.Combine(request.Directory, InstallMarker.FileName),
                JsonSerializer.Serialize(marker, ProfileStore.Options),
                cancellation);

            WriteUninstallKey(exe, request.Directory, count);
            log.Report("Shortcuts are in place.");

            if (request.PinTaskbar)
            {
                var pinTarget = startMenu.Length > 0 ? startMenu : desktop.Length > 0 ? desktop : CreateShortcut(exe, Environment.SpecialFolder.StartMenu, "Programs");
                if (TryPin(pinTarget))
                    notes.Add("Pinned to the taskbar.");
                else
                    notes.Add("Windows did not allow the installer to pin Win2DS5. Open the Start menu, right-click Win2DS5, and choose Pin to taskbar.");
            }

            var hid = request.HidHide
                ? await RunDriver("https://api.github.com/repos/nefarius/HidHide/releases/latest", "HidHide", notes, log, cancellation)
                : new DriverOutcome(false, false, "");
            var vigem = request.ViGEmBus
                ? await RunDriver("https://api.github.com/repos/nefarius/ViGEmBus/releases/latest", "ViGEmBus", notes, log, cancellation)
                : new DriverOutcome(false, false, "");
            bool hidRestart = hid.Ok || hid.Reboot;
            bool restart = hidRestart || vigem.Reboot;
            string restartMessage = hidRestart
                ? "HidHide is installed. Restart this computer before it can hide the controller from games."
                : "ViGEmBus is installed. Restart this computer before the virtual controller can be used.";
            if (restart)
                notes.Add(restartMessage);
            notes.Add("New builds published to GitHub install themselves the next time Win2DS5 starts.");
            log.Report("Finished.");
            return new InstallResult
            {
                Succeeded = true,
                Summary = restart ? "Win2DS5 is installed. A restart is needed." : "Win2DS5 is installed.",
                Details = string.Join(Environment.NewLine + Environment.NewLine, notes),
                CanLaunch = true,
                NeedsRestart = restart,
                RestartMessage = restart ? restartMessage : "",
                ExePath = exe,
            };
        }
        catch (OperationCanceledException)
        {
            return Failed("Install cancelled.");
        }
        catch (Exception ex)
        {
            return Failed(ex.Message);
        }
        finally
        {
            TryDeleteTree(stage);
            TryDeleteFile(zipPath);
        }
    }

    public static void Launch(string exe)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = true,
        });
    }

    public static void RestartWindows()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/r /t 0",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static async Task<DriverOutcome> RunDriver(string api, string label, List<string> notes, IProgress<string> log, CancellationToken cancellation)
    {
        try
        {
            var outcome = await DriverSetup.InstallAsync(api, log, label, cancellation);
            notes.Add(outcome.Message);
            return outcome;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or JsonException)
        {
            notes.Add($"{label} could not be installed ({ex.Message}). You can download it later from the Win2DS5 settings page.");
            return new DriverOutcome(false, false, "");
        }
    }

    private static InstallResult Failed(string message) => new()
    {
        Succeeded = false,
        Summary = "Win2DS5 was not installed.",
        Details = message,
    };

    private static Stream? OpenPayload() =>
        typeof(SetupEngine).Assembly.GetManifestResourceStream(PayloadName);

    private static bool AppIsRunningFrom(string directory)
    {
        try
        {
            var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Process.GetProcessesByName("Win2DS5").Any(process =>
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    return path != null && path.StartsWith(full, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            });
        }
        catch
        {
            return false;
        }
    }

    private static string CreateShortcut(string exe, Environment.SpecialFolder root, string? extra)
    {
        var folder = Environment.GetFolderPath(root);
        if (!string.IsNullOrEmpty(extra))
            folder = Path.Combine(folder, extra);
        Directory.CreateDirectory(folder);
        var linkPath = Path.Combine(folder, "Win2DS5.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(linkPath);
        link.TargetPath = exe;
        link.WorkingDirectory = Path.GetDirectoryName(exe);
        link.IconLocation = exe + ",0";
        link.Description = "Win2DS5";
        link.Save();
        return linkPath;
    }

    private static bool TryPin(string shortcut)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            dynamic folder = shell.NameSpace(Path.GetDirectoryName(shortcut));
            dynamic item = folder.ParseName(Path.GetFileName(shortcut));
            if (item == null)
                return false;
            foreach (dynamic verb in item.Verbs())
            {
                string name = ((string)verb.Name).Replace("&", "", StringComparison.Ordinal);
                if (name.Contains("pin", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("taskbar", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("unpin", StringComparison.OrdinalIgnoreCase))
                {
                    verb.DoIt();
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static void WriteUninstallKey(string exe, string directory, int fileCount)
    {
        using var key = Registry.LocalMachine.CreateSubKey(InstallRegistration.UninstallKey);
        key.SetValue("DisplayName", "Win2DS5");
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "Win2DS5");
        key.SetValue("InstallLocation", directory);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                bytes += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // Size in Apps & Features can be approximate.
            }
        }

        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, bytes / 1024), RegistryValueKind.DWord);
        _ = fileCount;
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
