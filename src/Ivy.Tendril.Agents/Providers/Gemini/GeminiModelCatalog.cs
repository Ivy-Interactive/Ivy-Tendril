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
            Id = "gemini-3.8-flash", DisplayName = "Gemini 3.8 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Gemini,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
        new()
        {
            Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Gemini,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google", IsDefault = true,
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
        new()
        {
            Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Gemini,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
        new()
        {
            Id = "gemini-3.1-pro", DisplayName = "Gemini 3.1 Pro",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Gemini,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 1.25m, OutputPerMillion = 10.00m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.315m,
        },
        new()
        {
            Id = "gemini-3-pro-preview", DisplayName = "Gemini 3 Pro",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Gemini,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 1.25m, OutputPerMillion = 10.00m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.315m,
        },
        new()
        {
            Id = "gemini-3-flash-preview", DisplayName = "Gemini 3 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Gemini,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
    ];
}
