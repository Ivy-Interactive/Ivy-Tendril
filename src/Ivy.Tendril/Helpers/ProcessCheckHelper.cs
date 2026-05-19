using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Helpers;

public static class ProcessCheckHelper
{
    public static ProcessStartInfo MakeStartInfo(string fileName, string arguments) => new()
    {
        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : fileName,
        Arguments = OperatingSystem.IsWindows() ? $"/S /c \"{fileName} {arguments}\"" : arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    public static async Task<bool> CheckCommand(string fileName, string arguments, int timeoutMs = 10_000)
    {
        try
        {
            return await Task.Run(() =>
            {
                var proc = Process.Start(MakeStartInfo(fileName, arguments));
                if (proc is null) return false;
                _ = proc.StandardOutput.ReadToEndAsync();
                _ = proc.StandardError.ReadToEndAsync();
                proc.WaitForExitOrKill(timeoutMs);
                return proc.ExitCode == 0;
            });
        }
        catch
        {
            return false;
        }
    }

    public static async Task<HealthCheckStatus> CheckHealth(string fileName, string arguments, int timeoutMs = 30_000)
    {
        try
        {
            return await Task.Run(() =>
            {
                var proc = Process.Start(MakeStartInfo(fileName, arguments));
                if (proc is null) return HealthCheckStatus.CheckFailed;
                _ = proc.StandardOutput.ReadToEndAsync();
                _ = proc.StandardError.ReadToEndAsync();
                var exited = proc.WaitForExitOrKill(timeoutMs);
                if (!exited) return HealthCheckStatus.CheckFailed;
                return proc.ExitCode == 0
                    ? HealthCheckStatus.Authenticated
                    : HealthCheckStatus.NotAuthenticated;
            });
        }
        catch
        {
            return HealthCheckStatus.CheckFailed;
        }
    }

    public static async Task<bool> CheckPowerShell()
        => await CheckCommand("pwsh", "-Version") || await CheckCommand("powershell", "-Version");

    public static Task<HealthCheckStatus> CheckFileAuth(string filePath, long minSize = 1)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath)) return HealthCheckStatus.NotAuthenticated;
                var fi = new FileInfo(filePath);
                return fi.Length > minSize
                    ? HealthCheckStatus.Authenticated
                    : HealthCheckStatus.NotAuthenticated;
            }
            catch
            {
                return HealthCheckStatus.CheckFailed;
            }
        });
    }

    public static async Task<bool> CloneRepositoryAsync(string url, string destinationPath)
    {
        try
        {
            if (url.Contains('\'') || url.Contains('"')) return false;

            var cmd = Directory.Exists(destinationPath)
                ? $"git -C '{destinationPath}' pull"
                : $"git clone '{url}' '{destinationPath}'";

            var psi = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-NoProfile -Command \"{cmd}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
