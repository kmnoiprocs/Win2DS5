namespace Win2DS5.Core;

public static class UpdateFeed
{
    public const string Owner = "kmnoiprocs";
    public const string Repository = "Win2DS5";
    public const string InfoName = "release.json";
    public const string PackageName = "Win2DS5-win-x64.zip";
    public const string SetupName = "Win2DS5-Setup.exe";

    public static string InfoUrl =>
        $"https://github.com/{Owner}/{Repository}/releases/latest/download/{InfoName}";

    public static string PackageUrl =>
        $"https://github.com/{Owner}/{Repository}/releases/latest/download/{PackageName}";

    public static string PageUrl =>
        $"https://github.com/{Owner}/{Repository}/releases/latest";
}

public static class AppRelease
{
    public static bool IsNewer(string? remote, string? local)
    {
        if (!TryParse(remote, out var published) || !TryParse(local, out var current))
            return false;
        return published > current;
    }

    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];
        var cut = text.IndexOfAny(['+', '-']);
        if (cut >= 0)
            text = text[..cut];
        return Version.TryParse(text, out version!);
    }
}

public sealed class ReleaseInfo
{
    public string Version { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class InstallMarker
{
    public const string FileName = "install.json";

    public bool Installed { get; set; } = true;
    public string DesktopShortcut { get; set; } = "";
    public string StartMenuShortcut { get; set; } = "";
}

public sealed class PendingUpdate
{
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public static class InstallRegistration
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Win2DS5";
}
