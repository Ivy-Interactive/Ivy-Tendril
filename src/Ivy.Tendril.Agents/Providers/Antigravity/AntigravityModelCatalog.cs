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
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
        new()
        {
            Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash",
            Capabilities = MidCaps, IsDefault = true,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
        new()
        {
            Id = "gemini-3.5-flash", DisplayName = "Gemini 3.5 Flash",
            Capabilities = MidCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
        new()
        {
            Id = "gemini-3.1-pro", DisplayName = "Gemini 3.1 Pro",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
        new()
        {
            Id = "claude-opus-5", DisplayName = "Claude Opus 5",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "anthropic",
        },
        new()
        {
            Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "anthropic",
        },
        new()
        {
            Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "anthropic",
        },
        new()
        {
            Id = "claude-opus-4-6", DisplayName = "Claude Opus 4.6",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "anthropic",
        },
        new()
        {
            Id = "gpt-oss-120b", DisplayName = "GPT-OSS 120B",
            Capabilities = MidCaps,
            ContextWindow = 128_000, MaxOutputTokens = 32_768,
            Provider = "openai",
        },
    ];

    protected override Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<ModelInfo>?>(null);
    }
}
