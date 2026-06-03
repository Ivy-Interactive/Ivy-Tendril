using System.IO;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Helpers;

public static class PathHelper
{
    /// <summary>
    /// Gets the file/folder name from a path, handling both Windows and Unix separators
    /// regardless of the current platform.
    /// </summary>
    public static string GetFileNameCrossPlatform(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var trimmed = path.TrimEnd('/', '\\');
        var lastSep = trimmed.LastIndexOfAny(['/', '\\']);
        return lastSep >= 0 ? trimmed[(lastSep + 1)..] : trimmed;
    }

    public static string? DefaultTendrilHomeOverride { get; set; }

    public static string GetDefaultTendrilHome()
    {
        if (!string.IsNullOrEmpty(DefaultTendrilHomeOverride))
            return DefaultTendrilHomeOverride;

        var envHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (!string.IsNullOrEmpty(envHome))
        {
            if (envHome.StartsWith("\"") && envHome.EndsWith("\""))
                envHome = envHome[1..^1];
            return envHome;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
    }

    public static string GetPwshPath()
    {
        var bundled = Path.Combine(System.AppContext.BaseDirectory, "PowerShell", OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        if (File.Exists(bundled))
        {
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var mode = File.GetUnixFileMode(bundled);
                    if (!mode.HasFlag(UnixFileMode.UserExecute))
                    {
                        File.SetUnixFileMode(bundled, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                    }
                }
                catch
                {
                    // Best-effort permission repair
                }
            }
            return bundled;
        }
        return "pwsh";
    }

    public static string? GetBundledDotnetPath()
    {
        var dir = Path.Combine(System.AppContext.BaseDirectory, "dotnet");
        var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (File.Exists(exe))
        {
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var mode = File.GetUnixFileMode(exe);
                    if (!mode.HasFlag(UnixFileMode.UserExecute))
                    {
                        File.SetUnixFileMode(exe, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                    }
                }
                catch
                {
                    // Best-effort permission repair
                }
            }
            return exe;
        }
        return null;
    }

    public static string GetDotnetPath()
    {
        return GetBundledDotnetPath() ?? "dotnet";
    }

    public static void AugmentPath()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var dirs = new HashSet<string>(pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var pathList = new List<string>();

        // 1. Prepend bundled tools directories to prioritize them
        var baseDir = System.AppContext.BaseDirectory;
        var bundledDotnetDir = Path.Combine(baseDir, "dotnet");
        if (Directory.Exists(bundledDotnetDir))
        {
            // Verify and auto-repair permissions on Unix
            _ = GetBundledDotnetPath();
            
            if (!dirs.Contains(bundledDotnetDir))
            {
                pathList.Add(bundledDotnetDir);
            }
        }

        var bundledPwshDir = Path.Combine(baseDir, "PowerShell");
        if (Directory.Exists(bundledPwshDir))
        {
            // Verify and auto-repair permissions on Unix
            _ = GetPwshPath();
            
            if (!dirs.Contains(bundledPwshDir))
            {
                pathList.Add(bundledPwshDir);
            }
        }

        // 2. Add existing PATH directories
        pathList.AddRange(dirs);

        // 3. For macOS/Linux, append common system search paths
        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var commonDirs = new[]
            {
                "/opt/homebrew/bin",
                "/opt/homebrew/sbin",
                "/usr/local/bin",
                "/usr/local/sbin",
                Path.Combine(home, ".dotnet", "tools"),
                Path.Combine(home, ".npm-global", "bin"),
                Path.Combine(home, ".local", "bin")
            };

            foreach (var dir in commonDirs)
            {
                if (Directory.Exists(dir) && !dirs.Contains(dir) && !pathList.Contains(dir))
                {
                    pathList.Add(dir);
                }
            }
        }

        var newPath = string.Join(separator, pathList);
        if (newPath != pathVar)
        {
            Environment.SetEnvironmentVariable("PATH", newPath);
        }
    }

    public static string ResolvePath(string raw)
    {
        var path = VariableExpansion.ExpandVariables(raw, "");

        if (path.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (path == "~") path = home;
            else if (path.StartsWith("~/") || path.StartsWith("~\\"))
                path = Path.Combine(home, path[2..]);
        }
        else if (path.StartsWith("$"))
        {
            var match = Regex.Match(path, @"^\$([A-Za-z_][A-Za-z0-9_]*)");
            if (match.Success)
            {
                var varName = match.Groups[1].Value;
                var varValue = Environment.GetEnvironmentVariable(varName);
                if (!string.IsNullOrEmpty(varValue))
                    path = varValue + path[match.Length..];
            }
        }

        return Path.GetFullPath(path);
    }
}
