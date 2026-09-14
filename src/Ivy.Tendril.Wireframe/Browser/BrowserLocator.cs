using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Ivy.Tendril.Wireframe.Browser;

public sealed record BrowserInfo(string Path, string Name);

/// <summary>
/// Finds an installed Chromium-family browser to drive over CDP.
///
/// Chrome is preferred over Edge because Chrome's headless is the reference
/// implementation; Edge is near-identical but adds enterprise-policy behaviour that is
/// miserable to debug remotely. On Windows, Edge is effectively guaranteed to be present,
/// so the "nothing found" path is rare -- but when it happens the error must list
/// everything that was searched, or it becomes an unanswerable support question.
/// </summary>
public static class BrowserLocator
{
    public static BrowserInfo Locate(string? explicitPath = null)
    {
        var searched = new List<string>();

        var overridePath = explicitPath
            ?? Environment.GetEnvironmentVariable("WIREFRAME_BROWSER");

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (File.Exists(overridePath)) return new BrowserInfo(overridePath, "custom");
            throw new FileNotFoundException(
                $"The browser path '{overridePath}' does not exist.", overridePath);
        }

        foreach (var (path, name) in Candidates())
        {
            searched.Add(path);
            if (File.Exists(path)) return new BrowserInfo(path, name);
        }

        throw new InvalidOperationException(
            "No Chrome, Edge or Chromium installation was found. Searched:\n  " +
            string.Join("\n  ", searched.Distinct()) +
            "\n\nPass --browser <path> or set WIREFRAME_BROWSER.");
    }

    private static IEnumerable<(string Path, string Name)> Candidates()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var exe in new[] { "chrome.exe", "msedge.exe" })
            {
                var fromRegistry = FromAppPaths(exe);
                if (fromRegistry is not null)
                    yield return (fromRegistry, exe == "chrome.exe" ? "Chrome" : "Edge");
            }

            var pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            yield return (Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), "Chrome");
            yield return (Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"), "Chrome");
            yield return (Path.Combine(local, @"Google\Chrome\Application\chrome.exe"), "Chrome");
            yield return (Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), "Edge");
            yield return (Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"), "Edge");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return ("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "Chrome");
            yield return ($"{home}/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "Chrome");
            yield return ("/Applications/Chromium.app/Contents/MacOS/Chromium", "Chromium");
            yield return ("/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge", "Edge");
        }
        else
        {
            foreach (var name in new[]
            {
                "google-chrome-stable", "google-chrome", "chromium", "chromium-browser",
                "microsoft-edge-stable", "microsoft-edge",
            })
            {
                var onPath = FromPath(name);
                if (onPath is not null) yield return (onPath, name);
            }

            yield return ("/opt/google/chrome/chrome", "Chrome");
            yield return ("/snap/bin/chromium", "Chromium");
            yield return ("/usr/bin/chromium", "Chromium");
        }
    }

    /// <summary>Windows records browser locations under App Paths, which survives
    /// non-default install directories.</summary>
    private static string? FromAppPaths(string exe)
    {
        if (!OperatingSystem.IsWindows()) return null;

        const string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = root.OpenSubKey(subKey + exe);
                if (key?.GetValue(null) is string path && File.Exists(path)) return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Registry unavailable; fall through to the well-known paths.
            }
        }
        return null;
    }

    private static string? FromPath(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry; skip it.
            }
        }
        return null;
    }
}
