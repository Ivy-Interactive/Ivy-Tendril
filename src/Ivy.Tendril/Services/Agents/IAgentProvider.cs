using System.Diagnostics;

namespace Ivy.Tendril.Services.Agents;

public interface IAgentProvider
{
    string Name { get; }
    ProcessStartInfo BuildProcessStart(AgentInvocation invocation);
    string? ExtractResult(IReadOnlyList<string> outputLines);
}

public record AgentInvocation(
    string PromptContent,
    string WorkingDirectory,
    string Model,
    string Effort,
    string SessionId,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> ExtraArgs);
