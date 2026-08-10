using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ivy.Tendril.Agents.Helpers;

/// <summary>
/// Runs CLI commands for health checks with timeout support.
/// </summary>
public static class HealthCheckRunner
{
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);

        ProcessStartInfo psi;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var resolvedFileName = ResolveFileName(fileName);
            var isCmdExe = fileName.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
                           fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);

            if (isCmdExe)
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                foreach (var arg in arguments)
                    psi.ArgumentList.Add(arg);
            }
            else
            {
                var escapedArgs = string.Join(" ", arguments.Select(a => a.Contains(' ') || a.Contains('&') || a.Contains('|') ? $"\"{a}\"" : a));
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/S /c \"\"{resolvedFileName}\" {escapedArgs}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
            }
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);
        }

        psi.Environment["CI"] = "true";
        psi.Environment["TERM"] = "dumb";

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;
        }

        if (ct.IsCancellationRequested)
            return (-1, string.Empty, "Cancelled");

        using var process = new Process();
        process.StartInfo = psi;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, $"Failed to start process: {ex.Message}");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout.Value);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            ProcessRunner.KillProcessTree(process);
            return (-1, stdout.ToString().Trim(), "Timed out");
        }
    }

    private static string ResolveFileName(string fileName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return fileName;

        if (Path.HasExtension(fileName) || Path.IsPathRooted(fileName))
            return fileName;

        return BinaryResolver.FindOnPath(fileName) ?? fileName;
    }
}
