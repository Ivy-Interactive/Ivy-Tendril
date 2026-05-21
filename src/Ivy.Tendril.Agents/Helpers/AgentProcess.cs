using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Helpers;

public sealed class AgentProcess : IAgentProcess
{
    private readonly Process _process;

    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;

    private AgentProcess(Process process)
    {
        _process = process;
    }

    public static AgentProcess Start(AgentProcessSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardOutput = spec.RedirectStdout,
            RedirectStandardError = spec.RedirectStderr,
            RedirectStandardInput = spec.RedirectStdin,
            UseShellExecute = spec.UseShellExecute,
            CreateNoWindow = spec.CreateNoWindow,
        };

        if (spec.RedirectStdin)
            psi.StandardInputEncoding = Encoding.UTF8;
        if (spec.RedirectStdout)
            psi.StandardOutputEncoding = Encoding.UTF8;
        if (spec.RedirectStderr)
            psi.StandardErrorEncoding = Encoding.UTF8;

        foreach (var arg in spec.Arguments)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in spec.Environment)
            psi.Environment[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();

        return new AgentProcess(process);
    }

    public async Task WriteStdinAsync(string content, CancellationToken ct = default)
    {
        await _process.StandardInput.WriteAsync(content.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    public void CloseStdin()
    {
        _process.StandardInput.Close();
    }

    public async IAsyncEnumerable<string> ReadStdoutLinesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await _process.StandardOutput.ReadLineAsync(ct);
            if (line is null) yield break;
            yield return line;
        }
    }

    public async IAsyncEnumerable<string> ReadStderrLinesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await _process.StandardError.ReadLineAsync(ct);
            if (line is null) yield break;
            yield return line;
        }
    }

    public Task WaitForExitAsync(CancellationToken ct = default)
        => _process.WaitForExitAsync(ct);

    public void Interrupt()
        => ProcessRunner.SendInterrupt(_process);

    public void Kill()
        => ProcessRunner.KillProcessTree(_process);

    public void Dispose()
        => _process.Dispose();
}
