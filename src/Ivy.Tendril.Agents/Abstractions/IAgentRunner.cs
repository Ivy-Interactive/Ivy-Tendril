namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentRunner
{
    Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default);
    Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default);
    IReadOnlyList<IAgentSession> ActiveSessions { get; }
    IObservable<IAgentSession> Sessions { get; }
    Task StopAllAsync(CancellationToken ct = default);
    IReadOnlyList<string> RegisteredAgents { get; }
    IAgentHealthCheck GetHealthCheck(string agentId);
    IAgentDescriptor GetDescriptor(string agentId);
}
