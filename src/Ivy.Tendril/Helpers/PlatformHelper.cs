using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Ivy.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Helpers;

public static class PlatformHelper
{
    /// <summary>
    /// Returns true if condition evaluates to exit code 0, false otherwise.
    /// Fast-paths simple Test-Path condition expressions natively in C# without spawning a pwsh process.
    /// </summary>
    public static bool EvaluatePowerShellCondition(string condition, string workingDirectory, int timeoutMs = 5000, ILogger? logger = null)
    {
        if (TryEvaluateTestPathCondition(condition, workingDirectory, out var testPathResult))
        {
            return testPathResult;
        }

        try
        {
            var sanitizedCondition = SanitizeConditionPath(condition);
            var psi = new ProcessStartInfo
            {
                FileName = PathHelper.GetPwshPath(),
                Arguments = $"-NoProfile -Command \"if ({sanitizedCondition}) {{ exit 0 }} else {{ exit 1 }}\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                if (!proc.WaitForExitOrKill(timeoutMs))
                    return false;
                return proc.ExitCode == 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to evaluate PowerShell condition");
            return false;
        }
    }

    /// <summary>
    /// Attempts to evaluate simple Test-Path expressions natively in C# without launching PowerShell.
    /// </summary>
    public static bool TryEvaluateTestPathCondition(string? condition, string workingDirectory, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(condition)) return false;

        var sanitized = SanitizeConditionPath(condition).Trim();
        var match = System.Text.RegularExpressions.Regex.Match(
            sanitized,
            @"^(?i)Test-Path\s+(?:[""']([^""']+)[""']|([^\s""']+))\s*$"
        );
        if (!match.Success) return false;

        var rawPath = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
        if (string.IsNullOrEmpty(rawPath)) return false;

        string fullPath;
        if (rawPath.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var remainder = rawPath.Length > 1 ? rawPath[1..].TrimStart('/', '\\') : "";
            fullPath = Path.Combine(home, remainder);
        }
        else if (Path.IsPathRooted(rawPath))
        {
            fullPath = rawPath;
        }
        else
        {
            fullPath = Path.Combine(workingDirectory, rawPath);
        }

        fullPath = fullPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        try
        {
            if (fullPath.Contains('*') || fullPath.Contains('?'))
            {
                var dir = Path.GetDirectoryName(fullPath);
                var pattern = Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(dir)) dir = workingDirectory;

                if (Directory.Exists(dir))
                {
                    result = Directory.EnumerateFileSystemEntries(dir, pattern, SearchOption.TopDirectoryOnly).Any();
                    return true;
                }

                result = false;
                return true;
            }

            result = File.Exists(fullPath) || Directory.Exists(fullPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Strips leading slashes or backslashes from relative worktree/artifact paths inside Test-Path condition expressions
    /// so PowerShell evaluates them relative to the working directory instead of the drive root.
    /// Preserves genuine absolute paths (e.g. /Users/..., /home/..., C:\...).
    /// </summary>
    public static string SanitizeConditionPath(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return condition;

        return System.Text.RegularExpressions.Regex.Replace(
            condition,
            @"(?i)(Test-Path\s+[""']?)[\\/]+(?=(Worktrees|artifacts)[\\/])",
            "$1");
    }

    public static bool OpenInTerminal(string workingDirectory, ILogger? logger = null)
    {
        try
        {
            var psi = new ProcessStartInfo();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "wt.exe";
                psi.Arguments = $"-d \"{workingDirectory}\"";
                psi.UseShellExecute = true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi.FileName = "open";
                psi.Arguments = $"-a Terminal \"{workingDirectory}\"";
                psi.UseShellExecute = false;
            }
            else
            {
                psi.FileName = "xdg-open";
                psi.Arguments = $"\"{workingDirectory}\"";
                psi.UseShellExecute = false;
            }

            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex)
        {
            logger?.LogWarning(ex, "Failed to open terminal");
            return false;
        }
        catch (FileNotFoundException ex)
        {
            logger?.LogWarning(ex, "Failed to open terminal");
            return false;
        }
    }

    public static bool OpenInEditor(string editorCommand, string target)
    {
        try
        {
            var formattedTarget = Path.GetFullPath(target);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Try launching directly via UseShellExecute first (handles .cmd, .bat, AppPaths, and spaces)
                try
                {
                    using var shellProc = Process.Start(new ProcessStartInfo
                    {
                        FileName = editorCommand,
                        Arguments = $"\"{formattedTarget}\"",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    });
                    if (shellProc != null) return true;
                }
                catch
                {
                    // Fall back to cmd.exe /c if direct launch failed
                }

                using var check = Process.Start(new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = editorCommand,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                check?.WaitForExit(3000);
                if (check is null || check.ExitCode != 0)
                    return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {editorCommand} \"{formattedTarget}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (!Path.IsPathRooted(editorCommand))
                {
                    using var check = Process.Start(new ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = editorCommand,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    check?.WaitForExit(3000);
                    if (check is null || check.ExitCode != 0)
                        return false;
                }
                else if (!File.Exists(editorCommand))
                {
                    return false;
                }
            }

            // UseShellExecute = false prevents the OS from printing "The file X does not exist"
            // to the terminal before .NET gets a chance to catch the exception.
            var psi = new ProcessStartInfo
            {
                FileName = editorCommand,
                Arguments = $"\"{target}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            return true;
        }
        catch (Exception)
        {
            // On macOS, fall back to 'open' which opens with the default app
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = $"\"{target}\"",
                        UseShellExecute = false
                    });
                    return true;
                }
                catch { }
            }
            return false;
        }
    }

    public static bool OpenInFileManager(string folderPath, ILogger? logger = null)
    {
        try
        {
            var psi = new ProcessStartInfo();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "explorer.exe";
                psi.Arguments = $"\"{folderPath}\"";
                psi.UseShellExecute = true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi.FileName = "open";
                psi.Arguments = $"\"{folderPath}\"";
                psi.UseShellExecute = false;
            }
            else
            {
                psi.FileName = "xdg-open";
                psi.Arguments = $"\"{folderPath}\"";
                psi.UseShellExecute = false;
            }

            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex)
        {
            logger?.LogWarning(ex, "Failed to open file manager");
            return false;
        }
        catch (FileNotFoundException ex)
        {
            logger?.LogWarning(ex, "Failed to open file manager");
            return false;
        }
    }
}
