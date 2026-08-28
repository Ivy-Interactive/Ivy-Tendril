using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityModelCatalog : CachedModelCatalogProvider
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

    public override string AgentId => Abstractions.AgentId.Antigravity;

    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new()
        {
            Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Antigravity,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
        new()
        {
            Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash",
            Capabilities = MidCaps, IsDefault = true,
            SupportedEfforts = EffortLevels.Antigravity,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
        new()
        {
            Id = "gemini-3.5-flash", DisplayName = "Gemini 3.5 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Antigravity,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.0375m,
        },
        new()
        {
            Id = "gemini-3.1-pro", DisplayName = "Gemini 3.1 Pro",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Antigravity,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
            InputPerMillion = 1.25m, OutputPerMillion = 10.00m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0.315m,
        },
        new()
        {
            Id = "claude-opus-5", DisplayName = "Claude Opus 5",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-opus-4-6", DisplayName = "Claude Opus 4.6",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "anthropic",
            InputPerMillion = 5.00m, OutputPerMillion = 25.00m,
            CacheWritePerMillion = 6.25m, CacheReadPerMillion = 0.50m,
        },
        new()
        {
            Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "anthropic",
            InputPerMillion = 3.00m, OutputPerMillion = 15.00m,
            CacheWritePerMillion = 3.75m, CacheReadPerMillion = 0.30m,
        },
        new()
        {
            Id = "gpt-oss-120b", DisplayName = "GPT-OSS 120B",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Antigravity,
            ContextWindow = 128_000, MaxOutputTokens = 32_768,
            Provider = "openai",
            InputPerMillion = 0.15m, OutputPerMillion = 0.60m,
            CacheWritePerMillion = 0m, CacheReadPerMillion = 0m,
        },
    ];

    protected override Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<ModelInfo>?>(null);
    }
}
