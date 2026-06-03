using System.Diagnostics;
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

    public static void AugmentPath(bool forceShellPath = false)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var dirs = new HashSet<string>(pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

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

        // 3. For macOS/Linux, append common system search paths and login shell paths
        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var commonDirs = new List<string>
            {
                "/opt/homebrew/bin",
                "/opt/homebrew/sbin",
                "/usr/local/bin",
                "/usr/local/sbin",
                Path.Combine(home, ".dotnet", "tools"),
                Path.Combine(home, ".npm-global", "bin"),
                Path.Combine(home, ".local", "bin")
            };

            if (forceShellPath)
            {
                var shellPath = GetLoginShellPath();
                if (!string.IsNullOrEmpty(shellPath))
                {
                    var shellDirs = shellPath.Split(':', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var dir in shellDirs)
                    {
                        if (!commonDirs.Contains(dir))
                        {
                            commonDirs.Add(dir);
                        }
                    }
                }
            }

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

    private static string? GetLoginShellPath()
    {
        // Try interactive login shell first (loads both profile and rc files)
        var path = RunShellForPath("-ilc");
        if (string.IsNullOrEmpty(path))
        {
            // Fallback to login shell (loads profile files)
            path = RunShellForPath("-lc");
        }
        return path;
    }

    private static string? RunShellForPath(string argsFlag)
    {
        try
        {
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell))
            {
                shell = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";
            }

            if (!File.Exists(shell))
            {
                shell = "/bin/zsh";
                if (!File.Exists(shell))
                {
                    shell = "/bin/bash";
                }
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"{argsFlag} \"echo ---PATH_START---; echo \\$PATH; echo ---PATH_END---\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = false, // Do not redirect to prevent buffer overflow hangs
                    RedirectStandardInput = true,  // Redirect to EOF so child processes don't wait for input
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            // Wait up to 2 seconds for the shell to print its PATH
            if (process.WaitForExit(TimeSpan.FromSeconds(2)))
            {
                var output = process.StandardOutput.ReadToEnd();
                var match = Regex.Match(output, @"---PATH_START---\r?\n(.*?)\r?\n---PATH_END---", RegexOptions.Singleline);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }
            else
            {
                try { process.Kill(); } catch { }
            }
        }
        catch
        {
            // Fallback
        }
        return null;
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
