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
            Id = "opus", DisplayName = "Claude Opus",
            Capabilities = FullCaps,
            ContextWindow = 200_000, MaxOutputTokens = 32_000,
            Provider = "anthropic", IsDefault = true,
            InputPerMillion = 10.00m, OutputPerMillion = 50.00m,
            CacheWritePerMillion = 12.50m, CacheReadPerMillion = 1.00m,
        },
        new()
        {
            Id = "sonnet", DisplayName = "Claude Sonnet",
            Capabilities = MidCaps,
            ContextWindow = 200_000, MaxOutputTokens = 16_000,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "haiku", DisplayName = "Claude Haiku",
            Capabilities = LiteCaps,
            ContextWindow = 200_000, MaxOutputTokens = 8_192,
            Provider = "anthropic",
            InputPerMillion = 1.00m, OutputPerMillion = 5.00m,
            CacheWritePerMillion = 1.25m, CacheReadPerMillion = 0.10m,
        },
    ];
}
