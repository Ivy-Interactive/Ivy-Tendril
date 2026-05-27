namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentProcess : IDisposable
{
    int ProcessId { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Task WriteStdinAsync(string content, CancellationToken ct = default);
    void CloseStdin();
    IAsyncEnumerable<string> ReadStdoutLinesAsync(CancellationToken ct = default);
    IAsyncEnumerable<string> ReadStderrLinesAsync(CancellationToken ct = default);
    Task WaitForExitAsync(CancellationToken ct = default);
    void Interrupt();
    void Kill();
}
