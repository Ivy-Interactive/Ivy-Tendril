using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var userEnvHome = Environment.GetEnvironmentVariable("TENDRIL_HOME", EnvironmentVariableTarget.User)?.Trim();
                if (!string.IsNullOrEmpty(userEnvHome))
                {
                    if (userEnvHome.StartsWith("\"") && userEnvHome.EndsWith("\""))
                        userEnvHome = userEnvHome[1..^1];
                    if (File.Exists(Path.Combine(userEnvHome, "config.yaml")))
                    {
                        Environment.SetEnvironmentVariable("TENDRIL_HOME", userEnvHome);
                        return userEnvHome;
                    }
                }
            }
            catch { }
        }

        try
        {
            var pointerFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril_location");
            if (File.Exists(pointerFile))
            {
                var location = File.ReadAllText(pointerFile).Trim();
                if (!string.IsNullOrEmpty(location) && File.Exists(Path.Combine(location, "config.yaml")))
                {
                    Environment.SetEnvironmentVariable("TENDRIL_HOME", location);
                    return location;
                }
            }
        }
        catch { }

        if (!string.IsNullOrEmpty(envHome))
            return envHome;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
    }

    public static string GetPwshPath()
    {
        var baseDir = System.AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "PowerShell", OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");

        // On macOS inside an app bundle, look in Contents/Resources
        if (OperatingSystem.IsMacOS() && baseDir.Contains(".app/Contents/MacOS"))
        {
            var macOsBundled = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "PowerShell", "pwsh"));
            if (File.Exists(macOsBundled))
            {
                bundled = macOsBundled;
            }
        }

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
        var baseDir = System.AppContext.BaseDirectory;
        var dir = Path.Combine(baseDir, "dotnet");

        // On macOS inside an app bundle, look in Contents/Resources
        if (OperatingSystem.IsMacOS() && baseDir.Contains(".app/Contents/MacOS"))
        {
            var macOsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "dotnet"));
            if (Directory.Exists(macOsDir))
            {
                dir = macOsDir;
            }
        }

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

    public static string? GetBundledIvyAgentPath()
    {
        var baseDir = System.AppContext.BaseDirectory;
        var exe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "ivy-agent.exe" : "ivy-agent");

        if (!File.Exists(exe))
        {
            var binExe = Path.Combine(baseDir, "bin", OperatingSystem.IsWindows() ? "ivy-agent.exe" : "ivy-agent");
            if (File.Exists(binExe)) exe = binExe;
        }

        // On macOS inside an app bundle, look in Contents/Resources
        if (!File.Exists(exe) && OperatingSystem.IsMacOS() && baseDir.Contains(".app/Contents/MacOS"))
        {
            var macOsExe = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", OperatingSystem.IsWindows() ? "ivy-agent.exe" : "ivy-agent"));
            if (File.Exists(macOsExe))
            {
                exe = macOsExe;
            }
            else
            {
                var macOsBinExe = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "bin", OperatingSystem.IsWindows() ? "ivy-agent.exe" : "ivy-agent"));
                if (File.Exists(macOsBinExe))
                {
                    exe = macOsBinExe;
                }
            }
        }

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

    /// <summary>
    /// Gets the resolved path for a configuration or resource file, checking Contents/Resources
    /// on macOS inside an app bundle first, and falling back to System.AppContext.BaseDirectory.
    /// </summary>
    public static string GetResourcePath(string fileName)
    {
        var baseDir = System.AppContext.BaseDirectory;
        var defaultPath = Path.Combine(baseDir, fileName);

        if (OperatingSystem.IsMacOS() && baseDir.Contains(".app/Contents/MacOS"))
        {
            var macOsResourcePath = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", fileName));
            if (File.Exists(macOsResourcePath))
            {
                return macOsResourcePath;
            }
        }

        return defaultPath;
    }

    public static void AugmentPath(bool forceShellPath = false)
    {
        Ivy.Helpers.CrashLog.Write($"[PathHelper] AugmentPath starting. forceShellPath={forceShellPath}");
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Ivy.Helpers.CrashLog.Write($"[PathHelper] AugmentPath current PATH: '{pathVar}'");
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var dirs = new HashSet<string>(pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var pathList = new List<string>();

        // 1. Prepend bundled tools directories to prioritize them
        var baseDir = System.AppContext.BaseDirectory;
        var bundledDotnetDir = Path.Combine(baseDir, "dotnet");
        var bundledPwshDir = Path.Combine(baseDir, "PowerShell");

        // On macOS inside an app bundle, look in Contents/Resources
        if (OperatingSystem.IsMacOS() && baseDir.Contains(".app/Contents/MacOS"))
        {
            var macOsResourcesDir = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources"));
            var macOsDotnetDir = Path.Combine(macOsResourcesDir, "dotnet");
            var macOsPwshDir = Path.Combine(macOsResourcesDir, "PowerShell");

            if (Directory.Exists(macOsDotnetDir)) bundledDotnetDir = macOsDotnetDir;
            if (Directory.Exists(macOsPwshDir)) bundledPwshDir = macOsPwshDir;
        }

        var bundledIvyAgentPath = GetBundledIvyAgentPath();
        if (bundledIvyAgentPath != null)
        {
            var ivyAgentDir = Path.GetDirectoryName(bundledIvyAgentPath);
            if (!string.IsNullOrEmpty(ivyAgentDir) && !dirs.Contains(ivyAgentDir))
            {
                pathList.Add(ivyAgentDir);
            }
        }

        if (Directory.Exists(bundledDotnetDir))
        {
            // Verify and auto-repair permissions on Unix
            _ = GetBundledDotnetPath();

            if (!dirs.Contains(bundledDotnetDir))
            {
                pathList.Add(bundledDotnetDir);
            }
        }

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

        // 3. For macOS/Linux, append common system search paths and login shell environment variables
        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var commonDirs = new List<string>
            {
                "/opt/homebrew/bin",
                "/opt/homebrew/sbin",
                "/usr/local/bin",
                "/usr/local/sbin",
                Path.Combine(home, ".npm-global", "bin"),
                Path.Combine(home, ".local", "bin"),
                // Legacy `dotnet tool install -g` shim directory. Kept last so the installer's
                // CLI symlink locations above always win over a stale .NET tool copy of tendril.
                Path.Combine(home, ".dotnet", "tools")
            };

            if (forceShellPath)
            {
                var shellEnv = GetLoginShellEnv();
                if (shellEnv != null)
                {
                    foreach (var kvp in shellEnv)
                    {
                        var key = kvp.Key;
                        var val = kvp.Value;
                        if (string.Equals(key, "PATH", StringComparison.OrdinalIgnoreCase))
                        {
                            var shellDirs = val.Split(':', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var dir in shellDirs)
                            {
                                if (!commonDirs.Contains(dir))
                                {
                                    commonDirs.Add(dir);
                                }
                            }
                        }
                        else
                        {
                            var existingVal = Environment.GetEnvironmentVariable(key);
                            if (string.IsNullOrEmpty(existingVal))
                            {
                                Environment.SetEnvironmentVariable(key, val);
                                var logValue = IsSecretKey(key) ? "[SECRET REDACTED]" : val;
                                Ivy.Helpers.CrashLog.Write($"[PathHelper] Imported environment variable from shell: {key}={logValue}");
                            }
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
        Ivy.Helpers.CrashLog.Write($"[PathHelper] AugmentPath new PATH: '{newPath}'");
        if (newPath != pathVar)
        {
            Environment.SetEnvironmentVariable("PATH", newPath);
            Ivy.Helpers.CrashLog.Write("[PathHelper] AugmentPath updated PATH environment variable successfully.");
        }
    }

    public static void EnsureCliSymlink()
    {
        if (OperatingSystem.IsWindows())
        {
            EnsureWindowsCliSetup();
            return;
        }

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            // Only run if we are inside a packaged macOS App bundle (.app)
            if (OperatingSystem.IsMacOS() && !exePath.Contains(".app/Contents/MacOS/"))
            {
                return;
            }

            // Target locations in order of preference
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new List<string>
            {
                "/usr/local/bin",
                Path.Combine(home, ".local", "bin")
            };

            foreach (var binDir in candidates)
            {
                try
                {
                    if (!Directory.Exists(binDir))
                    {
                        Directory.CreateDirectory(binDir);
                    }

                    var symlinkPath = Path.Combine(binDir, "tendril");

                    if (File.Exists(symlinkPath))
                    {
                        try
                        {
                            var target = File.ResolveLinkTarget(symlinkPath, true);
                            if (target != null && string.Equals(target.FullName, exePath, StringComparison.Ordinal))
                            {
                                // Symlink is already pointing to the correct path
                                return;
                            }
                        }
                        catch
                        {
                            // Ignore resolve error, recreate it
                        }

                        File.Delete(symlinkPath);
                    }

                    File.CreateSymbolicLink(symlinkPath, exePath);
                    Ivy.Helpers.CrashLog.Write($"[PathHelper] Created CLI symlink at {symlinkPath} -> {exePath}");
                    break; // Successfully created symlink, no need to try other locations
                }
                catch (Exception ex)
                {
                    // Fall back to next directory
                    Ivy.Helpers.CrashLog.Write($"[PathHelper] Failed to create symlink in {binDir}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Ivy.Helpers.CrashLog.Write($"[PathHelper] EnsureCliSymlink failed: {ex}");
        }
    }

    public static void EnsureWindowsCliSetup()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            var exeName = Path.GetFileName(exePath);
            if (!string.Equals(exeName, "Ivy.Tendril.exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(exeName, "Ivy.Tendril", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(exeName, "tendril.exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var appDir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(appDir) || !Directory.Exists(appDir)) return;

            // 1. Create tendril.cmd in the app directory alongside Ivy.Tendril.exe
            var cmdInAppDir = Path.Combine(appDir, "tendril.cmd");
            var cmdContent = $"@echo off\r\n\"%~dp0{exeName}\" %*\r\n";
            try
            {
                if (!File.Exists(cmdInAppDir) || File.ReadAllText(cmdInAppDir) != cmdContent)
                {
                    File.WriteAllText(cmdInAppDir, cmdContent, Encoding.ASCII);
                    Ivy.Helpers.CrashLog.Write($"[PathHelper] Created tendril.cmd at {cmdInAppDir}");
                }
            }
            catch (Exception ex)
            {
                Ivy.Helpers.CrashLog.Write($"[PathHelper] Failed to create tendril.cmd in {appDir}: {ex.Message}");
            }

            // 2. Create tendril.cmd in %USERPROFILE%\.local\bin for global command access
            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var localBinDir = Path.Combine(userProfile, ".local", "bin");
                if (!Directory.Exists(localBinDir))
                {
                    Directory.CreateDirectory(localBinDir);
                }

                var localBinCmd = Path.Combine(localBinDir, "tendril.cmd");
                var localBinCmdContent = $"@echo off\r\n\"{exePath}\" %*\r\n";
                if (!File.Exists(localBinCmd) || File.ReadAllText(localBinCmd) != localBinCmdContent)
                {
                    File.WriteAllText(localBinCmd, localBinCmdContent, Encoding.ASCII);
                    Ivy.Helpers.CrashLog.Write($"[PathHelper] Created tendril.cmd at {localBinCmd}");
                }
            }
            catch (Exception ex)
            {
                Ivy.Helpers.CrashLog.Write($"[PathHelper] Failed to create tendril.cmd in .local/bin: {ex.Message}");
            }

            // 3. Ensure appDir is present in User PATH environment variable
            try
            {
                var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
                var pathDirs = userPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!pathDirs.Any(d => string.Equals(d, appDir, StringComparison.OrdinalIgnoreCase)))
                {
                    var newPath = string.IsNullOrEmpty(userPath) ? appDir : $"{userPath}{Path.PathSeparator}{appDir}";
                    Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
                    Ivy.Helpers.CrashLog.Write($"[PathHelper] Added {appDir} to Windows User PATH.");
                }
            }
            catch (Exception ex)
            {
                Ivy.Helpers.CrashLog.Write($"[PathHelper] Failed to update User PATH: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Ivy.Helpers.CrashLog.Write($"[PathHelper] EnsureWindowsCliSetup failed: {ex}");
        }
    }

    private static bool IsSecretKey(string key)
    {
        var normalized = key.ToUpperInvariant();
        return normalized.Contains("KEY") ||
               normalized.Contains("SECRET") ||
               normalized.Contains("TOKEN") ||
               normalized.Contains("PASSWORD") ||
               normalized.Contains("AUTH") ||
               normalized.Contains("CREDENTIAL");
    }

    private static Dictionary<string, string>? GetLoginShellEnv()
    {
        Ivy.Helpers.CrashLog.Write("[PathHelper] GetLoginShellEnv starting");
        var env = RunShellForEnv("-ilc");
        if (env == null || env.Count == 0)
        {
            env = RunShellForEnv("-lc");
        }
        return env;
    }

    private static Dictionary<string, string>? RunShellForEnv(string argsFlag)
    {
        try
        {
            var shell = Environment.GetEnvironmentVariable("SHELL");
            Ivy.Helpers.CrashLog.Write($"[PathHelper] RunShellForEnv: SHELL env var is '{shell ?? "null"}'");
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
            Ivy.Helpers.CrashLog.Write($"[PathHelper] RunShellForEnv: using shell '{shell}'");

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,  // Redirect to EOF so child processes don't wait for input
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(argsFlag);
            psi.ArgumentList.Add("echo ---ENV_START---; env; echo ---ENV_END---");

            using var process = new Process
            {
                StartInfo = psi
            };

            process.ErrorDataReceived += (_, _) => { }; // Discard standard error asynchronously

            Ivy.Helpers.CrashLog.Write($"[PathHelper] RunShellForEnv: starting process with args: {string.Join(" ", process.StartInfo.ArgumentList)}");
            process.Start();
            process.BeginErrorReadLine();

            if (process.WaitForExit(TimeSpan.FromSeconds(2)))
            {
                var output = process.StandardOutput.ReadToEnd();
                var match = Regex.Match(output, @"---ENV_START---\r?\n(.*?)\r?\n---ENV_END---", RegexOptions.Singleline);
                if (match.Success)
                {
                    var res = new Dictionary<string, string>();
                    var lines = match.Groups[1].Value.Split('\n');
                    foreach (var line in lines)
                    {
                        var idx = line.IndexOf('=');
                        if (idx > 0)
                        {
                            var key = line[..idx].Trim();
                            var val = line[(idx + 1)..].Trim('\r', '\n');
                            if (!string.IsNullOrEmpty(key))
                            {
                                res[key] = val;
                            }
                        }
                    }
                    Ivy.Helpers.CrashLog.Write($"[PathHelper] RunShellForEnv: parsed {res.Count} variables successfully");
                    return res;
                }
                else
                {
                    Ivy.Helpers.CrashLog.Write("[PathHelper] RunShellForEnv: regex did not match markers");
                }
            }
            else
            {
                Ivy.Helpers.CrashLog.Write("[PathHelper] RunShellForEnv: process timed out waiting for exit");
                try { process.Kill(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Ivy.Helpers.CrashLog.Write($"[PathHelper] RunShellForEnv: exception occurred: {ex}");
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

    /// <summary>
    /// Extracts a local filesystem path from a file:/// URI, handling cross-platform differences
    /// between Windows and Unix/macOS, stripping line number/anchor suffixes, decoding percent-encoded
    /// characters, and normalizing paths.
    /// </summary>
    public static string? ExtractPathFromFileUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        var trimmed = uri.Trim();
        string rawPath;
        if (trimmed.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            rawPath = trimmed["file:///".Length..];
        }
        else if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            rawPath = trimmed["file://".Length..];
        }
        else
        {
            return null;
        }

        if (rawPath.StartsWith("localhost/", StringComparison.OrdinalIgnoreCase))
        {
            rawPath = rawPath["localhost".Length..];
        }

        // Remove trailing anchor or line number suffix (such as #L\d+, #\d+, :\d+, #L\d+-\d+, etc.)
        rawPath = Regex.Replace(rawPath, @"(?:(?:#L?|:)\d+(?:-\d+)?|#.*)$", "", RegexOptions.IgnoreCase);

        // Decode percent-encoded characters
        rawPath = Uri.UnescapeDataString(rawPath);

        // Strip extraneous leading slashes before a Windows drive letter (e.g., "/C:/..." or "///C:/...")
        rawPath = Regex.Replace(rawPath, @"^/+(?=[a-zA-Z]:)", "");

        // If not starting with a Windows drive letter (e.g. "C:"), ensure Unix absolute path starts with '/'
        var isWindowsDrive = Regex.IsMatch(rawPath, @"^[a-zA-Z]:");
        if (!isWindowsDrive && !rawPath.StartsWith('/'))
        {
            rawPath = "/" + rawPath;
        }

        return rawPath;
    }
}
