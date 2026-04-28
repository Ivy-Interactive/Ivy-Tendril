using System.Diagnostics;
using Ivy.Helpers;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal static class ProcessHelper
{
    internal static ProcessStartInfo MakeStartInfo(string fileName, string arguments) => new()
    {
        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : fileName,
        Arguments = OperatingSystem.IsWindows() ? $"/S /c \"{fileName} {arguments}\"" : arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    internal static async Task<bool> CheckCommand(string fileName, string arguments)
    {
        try
        {
            return await Task.Run(() =>
            {
                var proc = Process.Start(MakeStartInfo(fileName, arguments));
                if (proc is null) return false;
                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                proc.WaitForExitOrKill(10000);
                return proc.ExitCode == 0;
            });
        }
        catch
        {
            return false;
        }
    }

    internal static async Task<HealthResult> CheckHealth(string fileName, string arguments)
    {
        try
        {
            return await Task.Run(() =>
            {
                var proc = Process.Start(MakeStartInfo(fileName, arguments));
                if (proc is null) return HealthResult.CheckFailed;
                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                var exited = proc.WaitForExitOrKill(30000);
                if (!exited) return HealthResult.CheckFailed;
                return proc.ExitCode == 0 ? HealthResult.Authenticated : HealthResult.NotAuthenticated;
            });
        }
        catch
        {
            return HealthResult.CheckFailed;
        }
    }
}

internal enum HealthResult { Authenticated, NotAuthenticated, CheckFailed }
