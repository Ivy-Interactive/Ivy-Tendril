namespace Ivy.Tendril.Agents.Abstractions;

public sealed record ModelInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ModelCapabilities Capabilities { get; init; }
    public IReadOnlyList<EffortOption>? SupportedEfforts { get; init; }
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

public static class EffortLevels
{
    public static readonly EffortOption None = new("none", "None");
    public static readonly EffortOption Low = new("low", "Low");
    public static readonly EffortOption Medium = new("medium", "Medium");
    public static readonly EffortOption High = new("high", "High");
    public static readonly EffortOption ExtraHigh = new("xhigh", "Extra High");
    public static readonly EffortOption Max = new("max", "Max");

    public static readonly IReadOnlyList<EffortOption> Claude = [Low, Medium, High, ExtraHigh, Max];
    public static readonly IReadOnlyList<EffortOption> Codex = [None, Low, Medium, High, ExtraHigh];
    public static readonly IReadOnlyList<EffortOption> Copilot = [Low, Medium, High, ExtraHigh];
    public static readonly IReadOnlyList<EffortOption> Antigravity = [Low, Medium, High];
    public static readonly IReadOnlyList<EffortOption> Gemini = [Low, Medium, High];
    public static readonly IReadOnlyList<EffortOption> OpenCode = [Low, Medium, High, ExtraHigh, Max];
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
