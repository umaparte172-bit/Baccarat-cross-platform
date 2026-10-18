namespace Baccarat.Infrastructure.Automation;

public static class BrowserLocator
{
    public const string MissingBrowserMessage = "No supported browser was found. On macOS, install Google Chrome (or Microsoft Edge, Brave or Chromium) into Applications, then try again. Safari is not supported by this browser controller.";

    public static string? FindInstalled() => Candidates().FirstOrDefault(File.Exists);

    public static IEnumerable<string> MacCandidates(string userDirectory)
    {
        foreach (var directory in new[] { "/Applications", Path.Combine(userDirectory, "Applications") })
            foreach (var name in new[] { "Google Chrome", "Google Chrome for Testing", "Microsoft Edge", "Chromium", "Brave Browser", "Google Chrome Canary", "Vivaldi" })
                yield return Path.Combine(directory, name + ".app", "Contents", "MacOS", name);
    }

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsMacOS())
            return MacCandidates(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (OperatingSystem.IsWindows())
            return new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google/Chrome/Application/chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google/Chrome/Application/chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google/Chrome/Application/chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft/Edge/Application/msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft/Edge/Application/msedge.exe") };
        return new[] { "/usr/bin/google-chrome", "/usr/bin/chromium" };
    }
}
