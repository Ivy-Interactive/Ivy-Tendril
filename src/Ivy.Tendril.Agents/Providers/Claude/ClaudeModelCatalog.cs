using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Claude;

public sealed class ClaudeModelCatalog : CachedModelCatalogProvider
{
    private static readonly ModelCapabilities FullCaps =
        ModelCapabilities.Reasoning |
        ModelCapabilities.ImageInput |
        ModelCapabilities.CodeGeneration |
        ModelCapabilities.ExtendedThinking |
        ModelCapabilities.ToolUse |
        ModelCapabilities.Streaming;

    private static readonly ModelCapabilities MidCaps =
        ModelCapabilities.Reasoning |
        ModelCapabilities.ImageInput |
        ModelCapabilities.CodeGeneration |
        ModelCapabilities.ToolUse |
        ModelCapabilities.Streaming;

    private static readonly ModelCapabilities LiteCaps =
        ModelCapabilities.CodeGeneration |
        ModelCapabilities.ToolUse |
        ModelCapabilities.Streaming;

    public override string AgentId => Abstractions.AgentId.Claude;

    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new()
        {
            Id = "claude-fable-5", DisplayName = "Claude Fable 5",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 10.00m, OutputPerMillion = 50.00m,
            CacheWritePerMillion = 12.50m, CacheReadPerMillion = 1.00m,
        },
        new()
        {
            Id = "claude-opus-5-1", DisplayName = "Claude Opus 5.1",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-opus-5", DisplayName = "Claude Opus 5",
            Capabilities = FullCaps, IsDefault = true,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-opus-4-8", DisplayName = "Claude Opus 4.8",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-opus-4-7", DisplayName = "Claude Opus 4.7",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-opus-4-6", DisplayName = "Claude Opus 4.6",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "opus", DisplayName = "Claude Opus (Default)",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-sonnet-5-1", DisplayName = "Claude Sonnet 5.1",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-5.1", DisplayName = "Claude 5.1",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-3-7-sonnet", DisplayName = "Claude Sonnet 3.7",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-3.7-sonnet", DisplayName = "Claude Sonnet 3.7 (Alt)",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-3-5-sonnet", DisplayName = "Claude Sonnet 3.5",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "sonnet", DisplayName = "Claude Sonnet",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 128_000,
            Provider = "anthropic",
            InputPerMillion = 2.00m, OutputPerMillion = 10.00m,
            CacheWritePerMillion = 2.50m, CacheReadPerMillion = 0.20m,
        },
        new()
        {
            Id = "claude-haiku-5-1", DisplayName = "Claude Haiku 5.1",
            Capabilities = LiteCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 200_000, MaxOutputTokens = 64_000,
            Provider = "anthropic",
            InputPerMillion = 1.00m, OutputPerMillion = 5.00m,
            CacheWritePerMillion = 1.25m, CacheReadPerMillion = 0.10m,
        },
        new()
        {
            Id = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5",
            Capabilities = LiteCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 200_000, MaxOutputTokens = 64_000,
            Provider = "anthropic",
            InputPerMillion = 1.00m, OutputPerMillion = 5.00m,
            CacheWritePerMillion = 1.25m, CacheReadPerMillion = 0.10m,
        },
        new()
        {
            Id = "claude-3-5-haiku", DisplayName = "Claude Haiku 3.5",
            Capabilities = LiteCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 200_000, MaxOutputTokens = 64_000,
            Provider = "anthropic",
            InputPerMillion = 1.00m, OutputPerMillion = 5.00m,
            CacheWritePerMillion = 1.25m, CacheReadPerMillion = 0.10m,
        },
        new()
        {
            Id = "haiku", DisplayName = "Claude Haiku",
            Capabilities = LiteCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 200_000, MaxOutputTokens = 64_000,
            Provider = "anthropic",
            InputPerMillion = 1.00m, OutputPerMillion = 5.00m,
            CacheWritePerMillion = 1.25m, CacheReadPerMillion = 0.10m,
        },
    ];
}
