using System.Diagnostics;

namespace Ivy.Tendril.Helpers;

public static class GitHubCliHelper
{
    public static async Task<bool> CloneRepositoryAsync(string url, string destinationPath)
    {
        try
        {
            var shell = "pwsh";

            // Validate arguments somewhat to prevent injection
            if (url.Contains('\'') || url.Contains('"')) return false;

            string cmd;
            if (Directory.Exists(destinationPath))
            {
                cmd = $"git -C '{destinationPath}' pull";
            }
            else
            {
                cmd = $"git clone '{url}' '{destinationPath}'";
            }

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"-NoProfile -Command \"{cmd}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
