using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityModelCatalog : CachedModelCatalogProvider
{
    /// <summary>
    /// The Antigravity harness truncates a response below the anthropic models' own 128k output
    /// limit, so those rows cap <see cref="ModelInfo.MaxOutputTokens" /> here rather than in
    /// <see cref="ModelSpecs" />: it is a property of this provider, not of the model.
    /// </summary>
    private const int AnthropicOutputCap = 65_536;

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
        new ModelInfo
        {
            Id = "gemini-3.8-flash", DisplayName = "Gemini 3.8 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Antigravity,
            Provider = "google",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash",
            Capabilities = MidCaps, IsDefault = true,
            SupportedEfforts = EffortLevels.Antigravity,
            Provider = "google",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Antigravity,
            Provider = "google",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gemini-3.1-pro", DisplayName = "Gemini 3.1 Pro",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Antigravity,
            Provider = "google",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-fable-5", DisplayName = "Claude Fable 5",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(AnthropicOutputCap),
        new ModelInfo
        {
            Id = "claude-opus-5-1", DisplayName = "Claude Opus 5.1",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(AnthropicOutputCap),
        new ModelInfo
        {
            Id = "claude-opus-5", DisplayName = "Claude Opus 5",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(AnthropicOutputCap),
        new ModelInfo
        {
            Id = "claude-opus-4-6", DisplayName = "Claude Opus 4.6",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(AnthropicOutputCap),
        new ModelInfo
        {
            Id = "claude-sonnet-5-1", DisplayName = "Claude Sonnet 5.1",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(AnthropicOutputCap),
        new ModelInfo
        {
            Id = "claude-5.1", DisplayName = "Claude 5.1",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(AnthropicOutputCap),
        new ModelInfo
        {
            Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(AnthropicOutputCap),
        new ModelInfo
        {
            Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(AnthropicOutputCap),
        new ModelInfo
        {
            Id = "gpt-oss-120b", DisplayName = "GPT-OSS 120B",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Antigravity,
            Provider = "openai",
        }.WithSpec(),
    ];

    protected override Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<ModelInfo>?>(null);
    }
}
