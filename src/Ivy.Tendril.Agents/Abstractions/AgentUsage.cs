namespace Ivy.Tendril.Agents.Abstractions;

public sealed record AgentUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheWriteTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public decimal? CostUsd { get; init; }
    public int? PremiumRequests { get; init; }
    public string? Model { get; init; }
    public IReadOnlyList<ModelUsageEntry>? ModelBreakdown { get; init; }
}

public sealed record ModelUsageEntry
{
    public required string Model { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheWriteTokens { get; init; }
    public decimal? CostUsd { get; init; }
}

public sealed record ModelPricing
{
    public required string Model { get; init; }
    public required decimal InputPerMillion { get; init; }
    public required decimal OutputPerMillion { get; init; }
    public decimal CacheWritePerMillion { get; init; }
    public decimal CacheReadPerMillion { get; init; }
}

public sealed record SessionCostResult
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }
    public string? Model { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheWriteTokens { get; init; }
    public decimal TotalCostUsd { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
