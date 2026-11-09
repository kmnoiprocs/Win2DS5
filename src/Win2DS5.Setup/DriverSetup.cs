using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;

namespace Win2DS5.Setup;

internal static class DriverSetup
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    static DriverSetup()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Win2DS5-Setup");
    }

    public static bool HidHideInstalled() =>
        ServiceExists("HidHide") || DriverFileExists("HidHide.sys");

    public static bool ViGEmInstalled() =>
        ServiceExists("ViGEmBus") || DriverFileExists("ViGEmBus.sys");

    public static async Task<DriverOutcome> InstallAsync(string api, IProgress<string> log, string label, CancellationToken cancellation)
    {
        log.Report($"Looking up the latest {label} release…");
        using var response = await Http.GetAsync(api, cancellation);
        response.EnsureSuccessStatusCode();
        var url = ChooseExe(await response.Content.ReadAsStringAsync(cancellation));
        if (url == null)
            return new DriverOutcome(false, false, $"The latest {label} release did not include an installer.");

        var folder = Path.Combine(Path.GetTempPath(), "Win2DS5-setup");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, label + "-setup.exe");
        log.Report($"Downloading {label}…");
        await using (var download = await Http.GetStreamAsync(url, cancellation))
        await using (var output = File.Create(file))
            await download.CopyToAsync(output, cancellation);

        log.Report($"Installing {label}…");
        var code = await Run(file, "/exenoui /qn /norestart", cancellation);
        if (!Succeeded(code))
        {
            log.Report($"{label} needs its own setup window.");
            code = await Run(file, "", cancellation);
        }

        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // The temp installer can be cleaned up later.
        }

        return new DriverOutcome(Succeeded(code), code is 3010 or 1641, ExitMessage(label, code));
    }

    private static string? ChooseExe(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("assets", out var assets))
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && name.Contains("x64", StringComparison.OrdinalIgnoreCase))
                return asset.GetProperty("browser_download_url").GetString();
        }

        return null;
    }

    private static async Task<int> Run(string file, string arguments, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(file)!,
        };
        var process = Process.Start(start) ?? throw new InvalidOperationException("The driver setup did not start.");
        await process.WaitForExitAsync(cancellation);
        return process.ExitCode;
    }

    private static bool Succeeded(int code) => code is 0 or 3010 or 1641;

    private static string ExitMessage(string label, int code) => code switch
    {
        0 => $"{label} installed.",
        3010 or 1641 => $"{label} installed. Restart Windows before using it.",
        1223 => $"{label} setup was cancelled.",
        _ => $"{label} setup finished with code {code}.",
    };

    private static bool ServiceExists(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key != null;
    }

    private static bool DriverFileExists(string file) =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", file));
}

internal readonly record struct DriverOutcome(bool Ok, bool Reboot, string Message);
