namespace Baccarat.Infrastructure;

// Where the app keeps its files. Some macOS launches (sandboxed or without HOME) report an empty per-user folder,
// which used to turn into the read-only path "/BaccaratWorkspace": fall back to a folder that can be written.
public static class AppDataLocation
{
    public static string Root { get; } = Resolve();

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string Resolve()
    {
        var candidates = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.GetTempPath()
        };
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathRooted(candidate) || candidate == Path.GetPathRoot(candidate)) continue;
            try
            {
                var folder = Path.Combine(candidate, "BaccaratWorkspace");
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".write-test-" + Environment.ProcessId);
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return Path.GetTempPath();
    }
}
