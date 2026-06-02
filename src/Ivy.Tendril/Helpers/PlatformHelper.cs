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
    /// </summary>
    public static bool EvaluatePowerShellCondition(string condition, string workingDirectory, int timeoutMs = 5000, ILogger? logger = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = PathHelper.GetPwshPath(),
                Arguments = $"-NoProfile -Command \"if ({condition}) {{ exit 0 }} else {{ exit 1 }}\"",
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
    /// Launches a PowerShell action. Returns false if pwsh is not found or the launch fails.
    /// </summary>
    public static bool RunPowerShellAction(string action, string workingDirectory, ILogger? logger = null)
    {
        try
        {
            var pwshPath = PathHelper.GetPwshPath();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // On macOS, create a temporary .command script and open it in Terminal.app
                // This ensures the terminal window is visible and interactive
                var scriptPath = Path.Combine(Path.GetTempPath(), $"tendril-action-{Guid.NewGuid():N}.command");
                var scriptContent = $"#!/bin/bash\ncd \"{workingDirectory}\"\n\"{pwshPath}\" -NoExit -NoProfile -Command \"{action.Replace("\"", "\\\"")}\"\nrm \"{scriptPath}\"\n";
                File.WriteAllText(scriptPath, scriptContent);

                // Make the script executable
                var chmodPsi = new ProcessStartInfo("chmod", $"+x \"{scriptPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var chmodProc = Process.Start(chmodPsi))
                {
                    chmodProc?.WaitForExit();
                }

                // Open the script in Terminal.app
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-a Terminal \"{scriptPath}\"",
                    UseShellExecute = true
                });
                return true;
            }

            // Windows and Linux: run pwsh directly
            Process.Start(new ProcessStartInfo
            {
                FileName = pwshPath,
                Arguments = $"-NoExit -NoProfile -Command \"{action}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });
            return true;
        }
        catch (Win32Exception ex)
        {
            logger?.LogWarning(ex, "Failed to run PowerShell action");
            return false;
        }
        catch (FileNotFoundException ex)
        {
            logger?.LogWarning(ex, "Failed to run PowerShell action");
            return false;
        }
    }

    public static bool OpenInTerminal(string workingDirectory, ILogger? logger = null)
    {
        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = true };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "wt.exe";
                psi.Arguments = $"-d \"{workingDirectory}\"";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi.FileName = "open";
                psi.Arguments = $"-a Terminal \"{workingDirectory}\"";
            }
            else
            {
                psi.FileName = "xdg-open";
                psi.Arguments = workingDirectory;
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Verify the command exists via 'where' before launching.
                // cmd.exe /c always succeeds even if the inner command is invalid.
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
                    Arguments = $"/c \"{editorCommand} \"{target}\" > NUL 2>&1\"",
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
                        UseShellExecute = true
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
            var psi = new ProcessStartInfo { UseShellExecute = true };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "explorer.exe";
                psi.Arguments = folderPath;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi.FileName = "open";
                psi.Arguments = folderPath;
            }
            else
            {
                psi.FileName = "xdg-open";
                psi.Arguments = folderPath;
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