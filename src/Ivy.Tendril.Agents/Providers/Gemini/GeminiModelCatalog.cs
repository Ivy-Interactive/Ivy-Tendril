using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiModelCatalog : CachedModelCatalogProvider
{
    private static readonly ModelCapabilities FullCaps =
        ModelCapabilities.Reasoning |
        ModelCapabilities.ImageInput |
        ModelCapabilities.CodeGeneration |
        ModelCapabilities.ToolUse |
        ModelCapabilities.Streaming;

    private static readonly ModelCapabilities MidCaps =
        ModelCapabilities.CodeGeneration |
        ModelCapabilities.ToolUse |
        ModelCapabilities.Streaming;

    public override string AgentId => Abstractions.AgentId.Gemini;

    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new()
        {
            Id = "gemini-2.5-pro", DisplayName = "Gemini 2.5 Pro",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google", IsDefault = true,
            InputPerMillion = 1.25m, OutputPerMillion = 10.00m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.315m,
        },
        new()
        {
            Id = "gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash",
            Capabilities = MidCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
        new()
        {
            Id = "gemini-2.5-flash-lite", DisplayName = "Gemini 2.5 Flash Lite",
            Capabilities = MidCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.075m, OutputPerMillion = 0.30m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.01875m,
        },
        new()
        {
            Id = "gemini-3-pro-preview", DisplayName = "Gemini 3 Pro",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 1.25m, OutputPerMillion = 10.00m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.315m,
        },
        new()
        {
            Id = "gemini-3-flash-preview", DisplayName = "Gemini 3 Flash",
            Capabilities = MidCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
    ];
}
