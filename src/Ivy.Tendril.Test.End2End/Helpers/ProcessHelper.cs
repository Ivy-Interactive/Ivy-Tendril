using System.Diagnostics;

namespace Ivy.Tendril.Test.End2End.Helpers;

public static class ProcessHelper
{
    public record ProcessResult(int ExitCode, string Output, string Error);

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        int timeoutMs = 30_000,
        string? workingDirectory = null,
        IDictionary<string, string>? environmentVariables = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (workingDirectory != null)
            psi.WorkingDirectory = workingDirectory;

        if (environmentVariables != null)
            foreach (var (key, value) in environmentVariables)
                psi.Environment[key] = value;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Process '{fileName} {arguments}' timed out after {timeoutMs}ms");
        }

        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }
}
