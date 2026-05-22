namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentRunner
{
    Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default);
    Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default);
    IReadOnlyList<IAgentSession> ActiveSessions { get; }
    IObservable<IAgentSession> Sessions { get; }
    Task StopAllAsync(CancellationToken ct = default);
    IReadOnlyList<string> RegisteredAgents { get; }
    IAgentCli GetCli(string agentId);
    IEventParser GetParser(string agentId);
    IAgentHealthCheck GetHealthCheck(string agentId);
    IAgentDescriptor GetDescriptor(string agentId);
    IFailureAnalyzer? GetFailureAnalyzer(string agentId);
    ISessionCostParser? GetCostParser(string agentId);
    IAgentPty? GetPty(string agentId);
}
