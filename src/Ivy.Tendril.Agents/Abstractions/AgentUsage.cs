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

    /// <summary>
    /// Where these rates came from — e.g. <c>"Static catalog (claude)"</c> for the hardcoded
    /// per-provider catalogs, or the models.dev URL when the entry was enriched from there.
    /// Surfaced in the cost breakdown sheet so the price list is attributable.
    /// </summary>
    public string? Source { get; init; }
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

public sealed record AgentUsageWindow
{
    /// <summary>Window length as the agent reports it. 300 = 5h, 10080 = 7d, 43200 = 30d.</summary>
    public required int WindowMinutes { get; init; }
    public double? UsedPercent { get; init; }
    public long? TotalTokens { get; init; }
    public decimal? CostUsd { get; init; }
    public DateTimeOffset? ResetsAt { get; init; }
}

public sealed record AgentUsageSnapshot
{
    public required string AgentId { get; init; }
    public required IReadOnlyList<AgentUsageWindow> Windows { get; init; }
    /// <summary>When the numbers were last true. For Codex this is the last agent run, not now.</summary>
    public DateTimeOffset? CapturedAt { get; init; }
    public string? Note { get; init; }
}

