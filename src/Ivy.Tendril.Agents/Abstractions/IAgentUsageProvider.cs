namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentUsageProvider
{
    string AgentId { get; }
    Task<AgentUsageSnapshot?> GetUsageAsync(CancellationToken ct = default);
}
