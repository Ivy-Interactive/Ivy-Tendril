namespace Ivy.Tendril.Agents.Abstractions;

public sealed record ModelInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ModelCapabilities Capabilities { get; init; }
    public int? ContextWindow { get; init; }
    public int? MaxOutputTokens { get; init; }
    public string? Provider { get; init; }
    public bool IsDefault { get; init; }

    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
    public decimal CacheWritePerMillion { get; init; }
    public decimal CacheReadPerMillion { get; init; }
    public string? PricingSource { get; init; }
}

[Flags]
public enum ModelCapabilities
{
    None = 0,
    Reasoning = 1 << 0,
    ImageInput = 1 << 1,
    CodeGeneration = 1 << 2,
    ExtendedThinking = 1 << 3,
    ToolUse = 1 << 4,
    Streaming = 1 << 5,
}

public sealed record ModelCatalogResult
{
    public required string AgentId { get; init; }
    public required IReadOnlyList<ModelInfo> Models { get; init; }
    public required ModelCatalogSource Source { get; init; }
    public DateTimeOffset? RetrievedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public enum ModelCatalogSource
{
    Static,
    Dynamic,
    Fallback,
    Cached,
}
