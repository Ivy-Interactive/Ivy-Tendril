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
                FileName = "pwsh",
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
            Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-NoProfile -Command \"{action}\"",
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
            Process.Start(new ProcessStartInfo
            {
                FileName = editorCommand,
                Arguments = $"\"{target}\"",
                UseShellExecute = true
            });
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