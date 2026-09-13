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

    /// <summary>
    /// Limits and rates come from <see cref="ModelSpecs" />; this list declares identity only. The
    /// bare <c>opus</c> / <c>sonnet</c> / <c>haiku</c> aliases therefore report the same numbers as
    /// the concrete model each resolves to.
    /// </summary>
    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new ModelInfo
        {
            Id = "claude-fable-5", DisplayName = "Claude Fable 5",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-opus-5-1", DisplayName = "Claude Opus 5.1",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-opus-5", DisplayName = "Claude Opus 5",
            Capabilities = FullCaps, IsDefault = true,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-opus-4-8", DisplayName = "Claude Opus 4.8",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-opus-4-7", DisplayName = "Claude Opus 4.7",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-opus-4-6", DisplayName = "Claude Opus 4.6",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "opus", DisplayName = "Claude Opus (Default)",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-sonnet-5-1", DisplayName = "Claude Sonnet 5.1",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-5.1", DisplayName = "Claude 5.1",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-3-7-sonnet", DisplayName = "Claude Sonnet 3.7",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-3.7-sonnet", DisplayName = "Claude Sonnet 3.7 (Alt)",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-3-5-sonnet", DisplayName = "Claude Sonnet 3.5",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "sonnet", DisplayName = "Claude Sonnet",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-haiku-5-1", DisplayName = "Claude Haiku 5.1",
            Capabilities = LiteCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5",
            Capabilities = LiteCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-3-5-haiku", DisplayName = "Claude Haiku 3.5",
            Capabilities = LiteCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "haiku", DisplayName = "Claude Haiku",
            Capabilities = LiteCaps,
            SupportedEfforts = EffortLevels.Claude,
            Provider = "anthropic",
        }.WithSpec(),
    ];
}
