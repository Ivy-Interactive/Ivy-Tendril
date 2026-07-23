using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvySessionCostParser : ISessionCostParser
{
    private readonly OpenCodeSessionCostParser _inner = new();

    public string AgentId => Abstractions.AgentId.Ivy;

    public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
    {
        var result = _inner.Parse(filePath, pricing);
        return new SessionCostResult
        {
            SessionId = result.SessionId,
            AgentId = AgentId,
            Model = result.Model,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            CacheReadTokens = result.CacheReadTokens,
            CacheWriteTokens = result.CacheWriteTokens,
            TotalCostUsd = result.TotalCostUsd,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
        };
    }

    public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
        => _inner.DiscoverSessionFiles(projectPath);
}
