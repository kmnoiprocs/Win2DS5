using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Win2DS5.Core;
using Win2DS5.Platform;

namespace Win2DS5.App.Updates;

public sealed record UpdateCheck(string Message, bool Ready);

public static class InstalledCopy
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _watching;

    static InstalledCopy()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Win2DS5");
    }

    public static bool IsInstalled => File.Exists(MarkerPath);

    public static bool Ready { get; private set; }

    public static string Status { get; private set; } = Describe();

    public static event Action? Changed;

    public static string CurrentVersion
    {
        get
        {
            var info = typeof(InstalledCopy).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info))
                info = typeof(InstalledCopy).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
    }

    public static string MarkerPath => Path.Combine(AppContext.BaseDirectory, InstallMarker.FileName);

    public static string UpdateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Win2DS5",
        "update");

    public static bool HandleEarly(string[] args)
    {
        if (args.Any(arg => arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Uninstall();
            return true;
        }

        return false;
    }

    public static async Task<bool> RunAtStartupAsync(Action<string> status, bool check)
    {
        void OnChanged() => status(Status);
        Changed += OnChanged;
        try
        {
            if (PendingReady())
            {
                status("Installing the downloaded build…");
                return true;
            }

            if (!check || !IsInstalled)
                return false;

            var result = await CheckAsync();
            if (result.Ready)
            {
                status("Installing the new build…");
                return true;
            }

            status(result.Message);
            return false;
        }
        finally
        {
            Changed -= OnChanged;
        }
    }

    public static void StartWatching(Func<bool> enabled)
    {
        if (Interlocked.Exchange(ref _watching, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromHours(6));
                try
                {
                    if (IsInstalled && enabled())
                        await CheckAsync();
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
            }
        });
    }

    public static async Task<UpdateCheck> CheckAsync()
    {
        await Gate.WaitAsync();
        try
        {
            return await CheckCore();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            Log(ex.Message);
            Ready = PendingReady();
            var check = new UpdateCheck(
                Ready
                    ? Status
                    : "Couldn't reach GitHub to look for a new build.",
                Ready);
            Publish(check.Message);
            return check;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static void BeginApply()
    {
        Directory.CreateDirectory(UpdateDirectory);
        var script = Path.Combine(UpdateDirectory, "apply.ps1");
        File.WriteAllText(script, ApplyScript, new UTF8Encoding(false));
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.Environment["WIN2DS5_ZIP"] = PackagePath;
        start.Environment["WIN2DS5_DIR"] = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        start.Environment["WIN2DS5_PID"] = Environment.ProcessId.ToString();
        start.Environment["WIN2DS5_LOG"] = LogPath;
        Process.Start(start);
    }

    private static async Task<UpdateCheck> CheckCore()
    {
        Publish("Checking GitHub for a published build…");
        using var infoResponse = await Http.GetAsync(UpdateFeed.InfoUrl);
        if (infoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Ready = false;
            var missing = new UpdateCheck("No build has been published yet.", false);
            Publish(missing.Message);
            return missing;
        }

        infoResponse.EnsureSuccessStatusCode();
        var info = JsonSerializer.Deserialize<ReleaseInfo>(await infoResponse.Content.ReadAsStringAsync(), ProfileStore.Options)
            ?? new ReleaseInfo();
        if (!AppRelease.IsNewer(info.Version, CurrentVersion))
        {
            Ready = PendingReady();
            var current = new UpdateCheck($"Win2DS5 {CurrentVersion} is the newest published build.", Ready);
            Publish(current.Message);
            return current;
        }

        if (!IsInstalled)
        {
            Ready = false;
            var dev = new UpdateCheck(
                $"Version {info.Version} is published. This development copy does not replace itself. Install Win2DS5 on the other computer to receive it.",
                false);
            Publish(dev.Message);
            return dev;
        }

        if (PendingMatches(info))
        {
            Ready = true;
            var staged = new UpdateCheck(ReadyMessage(info.Version), true);
            Publish(staged.Message);
            return staged;
        }

        Publish($"Downloading {info.Version}…");
        Directory.CreateDirectory(UpdateDirectory);
        var partial = PackagePath + ".partial";
        await using (var download = await Http.GetStreamAsync(UpdateFeed.PackageUrl))
        await using (var file = File.Create(partial))
            await download.CopyToAsync(file);

        var hash = HashFile(partial);
        if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            Ready = false;
            var bad = new UpdateCheck("The download did not match the published checksum.", false);
            Publish(bad.Message);
            return bad;
        }

        File.Move(partial, PackagePath, true);
        var pending = new PendingUpdate { Version = info.Version, Sha256 = hash };
        await File.WriteAllTextAsync(PendingPath, JsonSerializer.Serialize(pending, ProfileStore.Options));
        TryDelete(FailedPath);
        Ready = true;
        var done = new UpdateCheck(ReadyMessage(info.Version), true);
        Publish(done.Message);
        return done;
    }

    public static bool PendingReady()
    {
        try
        {
            if (!IsInstalled || !File.Exists(PendingPath) || !File.Exists(PackagePath))
                return false;
            var pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(PendingPath), ProfileStore.Options);
            if (pending == null || !AppRelease.IsNewer(pending.Version, CurrentVersion))
                return false;
            var hash = HashFile(PackagePath);
            return hash.Equals(pending.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Uninstall()
    {
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        InstallMarker? marker = null;
        try
        {
            if (File.Exists(MarkerPath))
                marker = JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(MarkerPath), ProfileStore.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            marker = null;
        }

        TryUnpin(marker?.StartMenuShortcut);
        TryUnpin(marker?.DesktopShortcut);
        TryDelete(marker?.DesktopShortcut);
        TryDelete(marker?.StartMenuShortcut);
        TryDelete(ShortcutPath(Environment.SpecialFolder.DesktopDirectory));
        TryDelete(ShortcutPath(Environment.SpecialFolder.StartMenu, "Programs"));
        try
        {
            StartupRegistration.SetEnabled(false);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }

        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(InstallRegistration.UninstallKey, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log(ex.Message);
        }

        var script = Path.Combine(Path.GetTempPath(), "win2ds5-uninstall.cmd");
        File.WriteAllText(script, "@echo off\r\nping 127.0.0.1 -n 3 >nul\r\nrmdir /s /q \"" + installDir + "\"\r\ndel \"%~f0\"\r\n", new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo
        {
            FileName = script,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        MessageBox("Win2DS5 was removed. Your profiles are still in %AppData%\\Win2DS5 if you install it again.", "Win2DS5");
        Environment.Exit(0);
    }

    private static bool PendingMatches(ReleaseInfo info)
    {
        try
        {
            if (!File.Exists(PendingPath) || !File.Exists(PackagePath))
                return false;
            var pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(PendingPath), ProfileStore.Options);
            if (pending == null || !string.Equals(pending.Version, info.Version, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!pending.Sha256.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                return false;
            var hash = HashFile(PackagePath);
            return hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Publish(string message)
    {
        Status = message;
        Changed?.Invoke();
    }

    private static string Describe()
    {
        if (File.Exists(FailedPath))
            return "The last update did not install. Win2DS5 is still this version. Check now tries again.";
        return File.Exists(Path.Combine(AppContext.BaseDirectory, InstallMarker.FileName))
            ? "This copy checks GitHub for a published build before it opens."
            : "This is a development copy. It does not replace itself. Computers where Win2DS5 was installed pick up new builds on their own.";
    }

    private static string ReadyMessage(string version) =>
        $"Version {version} is downloaded. Restart Win2DS5 to install it.";

    private static string PackagePath => Path.Combine(UpdateDirectory, "package.zip");

    private static string PendingPath => Path.Combine(UpdateDirectory, "pending.json");

    private static string FailedPath => Path.Combine(UpdateDirectory, "pending.json.failed");

    private static string LogPath => Path.Combine(UpdateDirectory, "update.log");

    private static string ShortcutPath(Environment.SpecialFolder root, string? extra = null)
    {
        var folder = Environment.GetFolderPath(root);
        if (!string.IsNullOrEmpty(extra))
            folder = Path.Combine(folder, extra);
        return Path.Combine(folder, "Win2DS5.lnk");
    }

    private static void TryUnpin(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut) || !File.Exists(shortcut))
            return;
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            dynamic folder = shell.NameSpace(Path.GetDirectoryName(shortcut));
            dynamic item = folder.ParseName(Path.GetFileName(shortcut));
            if (item == null)
                return;
            foreach (dynamic verb in item.Verbs())
            {
                string name = ((string)verb.Name).Replace("&", "", StringComparison.Ordinal);
                if (name.Contains("unpin", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("taskbar", StringComparison.OrdinalIgnoreCase))
                {
                    verb.DoIt();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(ex.Message);
        }
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(UpdateDirectory);
            File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
        }
        catch (IOException)
        {
            // The update can still proceed without a log line.
        }
    }

    private static void MessageBox(string text, string caption)
    {
        MessageBoxW(IntPtr.Zero, text, caption, 0);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    private const string ApplyScript = """
        $ErrorActionPreference = 'Stop'
        $log = $env:WIN2DS5_LOG
        $temp = $null
        function Write-Log($message) {
          if ($log) { Add-Content -LiteralPath $log -Value ((Get-Date).ToString('s') + ' ' + $message) }
        }
        try {
          $zip = $env:WIN2DS5_ZIP
          $dir = $env:WIN2DS5_DIR.TrimEnd('\')
          $wait = 0
          [void][int]::TryParse($env:WIN2DS5_PID, [ref]$wait)
          Write-Log "waiting for $wait"
          if ($wait -gt 0) {
            try { Wait-Process -Id $wait -Timeout 45 -ErrorAction Stop } catch { }
          }
          Start-Sleep -Seconds 1
          Add-Type -AssemblyName System.IO.Compression.FileSystem
          $temp = Join-Path $env:TEMP ('win2ds5-' + [guid]::NewGuid().ToString('N'))
          New-Item -ItemType Directory -Path $temp | Out-Null
          [IO.Compression.ZipFile]::ExtractToDirectory($zip, $temp)
          $kept = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
          Get-ChildItem -LiteralPath $temp -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($temp.Length).TrimStart('\')
            if ($rel.Contains('..')) { throw "Unexpected path in the update." }
            [void]$kept.Add($rel)
            $dest = Join-Path $dir $rel
            $parent = Split-Path -Parent $dest
            if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
            $copied = $false
            for ($i = 0; $i -lt 12 -and -not $copied; $i++) {
              try {
                Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
                $copied = $true
              } catch {
                Start-Sleep -Milliseconds 500
              }
            }
            if (-not $copied) { throw "Could not replace $rel" }
          }
          Get-ChildItem -LiteralPath $dir -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($dir.Length).TrimStart('\')
            if ($rel -eq 'install.json') { return }
            if (-not $kept.Contains($rel)) {
              try { Remove-Item -LiteralPath $_.FullName -Force } catch { }
            }
          }
          $exe = Join-Path $dir 'Win2DS5.exe'
          Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
          $pending = Join-Path (Split-Path -Parent $zip) 'pending.json'
          Remove-Item -LiteralPath $pending -Force -ErrorAction SilentlyContinue
          Write-Log "starting $exe"
          Start-Process -FilePath $exe
          Write-Log 'done'
        } catch {
          Write-Log $_
          $pending = Join-Path $env:LOCALAPPDATA 'Win2DS5\update\pending.json'
          if (Test-Path -LiteralPath $pending) {
            Move-Item -LiteralPath $pending -Destination ($pending + '.failed') -Force
          }
          $shell = New-Object -ComObject WScript.Shell
          [void]$shell.Popup('Win2DS5 could not finish the update. Starting the copy that is installed now.', 20, 'Win2DS5', 48)
          $exe = Join-Path $env:WIN2DS5_DIR.TrimEnd('\') 'Win2DS5.exe'
          if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe }
          exit 1
        } finally {
          if ($temp -and (Test-Path -LiteralPath $temp)) {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
          }
        }
        """;
}
