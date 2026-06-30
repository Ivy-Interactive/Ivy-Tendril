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

    public static async Task<(bool Success, string? Error)> TryCheckCommand(string fileName, string arguments, int timeoutMs = 10_000)
    {
        try
        {
            return await Task.Run(async () =>
            {
                var psi = MakeStartInfo(fileName, arguments);
                var proc = Process.Start(psi);
                if (proc is null) return (false, "Failed to start process.");
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();

                var exited = proc.WaitForExitOrKill(timeoutMs);
                if (!exited)
                {
                    return (false, $"Process timed out after {timeoutMs}ms.");
                }

                var stdout = await outTask;
                var stderr = await errTask;

                if (proc.ExitCode == 0)
                {
                    return (true, (string?)null);
                }

                var errorMsg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                var details = string.IsNullOrWhiteSpace(errorMsg) ? $"Exit code {proc.ExitCode}" : $"Exit code {proc.ExitCode}: {errorMsg.Trim()}";
                return (false, details);
            });
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<bool> CheckPowerShell()
    {
        var (success, _) = await CheckPowerShellWithDetails();
        return success;
    }

    public static async Task<(bool Success, string? Error)> CheckPowerShellWithDetails()
    {
        var errors = new List<string>();

        var bundledPath = PathHelper.GetPwshPath();
        var hasBundled = bundledPath != "pwsh";
        if (hasBundled)
        {
            var (success, err) = await TryCheckCommand(bundledPath, "-Version");
            if (success) return (true, null);
            errors.Add($"bundled: {err ?? "unknown error"}");
        }

        var (pwshSuccess, pwshErr) = await TryCheckCommand("pwsh", "-Version");
        if (pwshSuccess) return (true, null);
        errors.Add($"system pwsh: {pwshErr ?? "unknown error"}");

        var (legacySuccess, legacyErr) = await TryCheckCommand("powershell", "-NoProfile -Command 1");
        if (legacySuccess) return (true, null);
        errors.Add($"system powershell: {legacyErr ?? "unknown error"}");

        return (false, string.Join("; ", errors));
    }

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

            var isPull = Directory.Exists(destinationPath);
            var psi = MakeStartInfo("git", isPull 
                ? $"-C \"{destinationPath}\" pull" 
                : $"clone \"{url}\" \"{destinationPath}\"");

            using var process = Process.Start(psi);
            if (process is null) return false;

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            await Task.WhenAll(outTask, errTask);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
