using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Helpers;

/// <summary>
/// Cross-platform process spawning and management.
/// </summary>
public static class ProcessRunner
{
    public static Process StartProcess(AgentProcessSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveFileName(spec.FileName),
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardOutput = spec.RedirectStdout,
            RedirectStandardError = spec.RedirectStderr,
            RedirectStandardInput = spec.RedirectStdin,
            UseShellExecute = spec.UseShellExecute,
            CreateNoWindow = spec.CreateNoWindow,
        };

        if (spec.RedirectStdin)
        {
            psi.StandardInputEncoding = Encoding.UTF8;
        }
        if (spec.RedirectStdout)
        {
            psi.StandardOutputEncoding = Encoding.UTF8;
        }
        if (spec.RedirectStderr)
        {
            psi.StandardErrorEncoding = Encoding.UTF8;
        }

        foreach (var arg in spec.Arguments)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in spec.Environment)
            psi.Environment[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();

        return process;
    }

    public static async Task WriteStdinAndCloseAsync(Process process, string content, CancellationToken ct = default)
    {
        await process.StandardInput.WriteAsync(content.AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
        process.StandardInput.Close();
    }

    public static async IAsyncEnumerable<string> ReadLinesAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) yield break;
            yield return line;
        }
    }

    public static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        catch (SystemException)
        {
            // Access denied or process not found
        }
    }

    public static void SendInterrupt(Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, there's no clean SIGINT for child processes.
            // Kill the process tree as the safest alternative.
            KillProcessTree(process);
        }
        else
        {
            // Unix: send SIGINT
            try
            {
                Process.Start("kill", ["-INT", process.Id.ToString()]);
            }
            catch
            {
                KillProcessTree(process);
            }
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
