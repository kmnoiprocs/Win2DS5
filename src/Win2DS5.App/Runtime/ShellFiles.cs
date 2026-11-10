namespace Win2DS5.App.Runtime;

public static class ShellFiles
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Win2DS5",
        "shells");

    public static string Existing(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        string path = Path.Combine(Folder, Path.GetFileName(name));
        return File.Exists(path) ? path : "";
    }

    public static void Delete(string? name)
    {
        string path = Existing(name);
        if (path.Length > 0)
            File.Delete(path);
    }
}
