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
            Id = "claude-opus-4-7", DisplayName = "Claude Opus 4.7", Alias = "opus",
            Capabilities = FullCaps,
            ContextWindow = 200_000, MaxOutputTokens = 32_000,
            Provider = "anthropic", IsDefault = true,
            InputPerMillion = 10.00m, OutputPerMillion = 50.00m,
            CacheWritePerMillion = 12.50m, CacheReadPerMillion = 1.00m,
        },
        new()
        {
            Id = "claude-opus-4-6", DisplayName = "Claude Opus 4.6",
            Capabilities = FullCaps,
            ContextWindow = 200_000, MaxOutputTokens = 32_000,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-opus-4-5", DisplayName = "Claude Opus 4.5", Alias = null,
            Capabilities = FullCaps,
            ContextWindow = 200_000, MaxOutputTokens = 32_000,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6", Alias = "sonnet",
            Capabilities = MidCaps,
            ContextWindow = 200_000, MaxOutputTokens = 16_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-sonnet-4-5", DisplayName = "Claude Sonnet 4.5", Alias = null,
            Capabilities = MidCaps,
            ContextWindow = 200_000, MaxOutputTokens = 16_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5", Alias = "haiku",
            Capabilities = LiteCaps,
            ContextWindow = 200_000, MaxOutputTokens = 8_192,
            Provider = "anthropic",
            InputPerMillion = 1.00m, OutputPerMillion = 5.00m,
            CacheWritePerMillion = 1.25m, CacheReadPerMillion = 0.10m,
        },
        new()
        {
            Id = "claude-opus-4-1", DisplayName = "Claude Opus 4.1",
            Capabilities = FullCaps,
            ContextWindow = 200_000, MaxOutputTokens = 32_000,
            Provider = "anthropic",
            InputPerMillion = 15.00m, OutputPerMillion = 75.00m,
            CacheWritePerMillion = 18.75m, CacheReadPerMillion = 1.50m,
        },
        new()
        {
            Id = "claude-opus-4", DisplayName = "Claude Opus 4",
            Capabilities = FullCaps,
            ContextWindow = 200_000, MaxOutputTokens = 32_000,
            Provider = "anthropic",
            InputPerMillion = 15.00m, OutputPerMillion = 75.00m,
            CacheWritePerMillion = 18.75m, CacheReadPerMillion = 1.50m,
        },
        new()
        {
            Id = "claude-sonnet-4", DisplayName = "Claude Sonnet 4",
            Capabilities = MidCaps,
            ContextWindow = 200_000, MaxOutputTokens = 16_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-haiku-4", DisplayName = "Claude Haiku 4",
            Capabilities = LiteCaps,
            ContextWindow = 200_000, MaxOutputTokens = 8_192,
            Provider = "anthropic",
            InputPerMillion = 0.80m, OutputPerMillion = 4.00m,
            CacheWritePerMillion = 1.00m, CacheReadPerMillion = 0.08m,
        },
    ];
}
