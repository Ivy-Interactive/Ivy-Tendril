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
        new ModelInfo
        {
            Id = "gemini-3.8-flash", DisplayName = "Gemini 3.8 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Gemini,
            Provider = "google",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Gemini,
            Provider = "google", IsDefault = true,
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Gemini,
            Provider = "google",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gemini-3.1-pro", DisplayName = "Gemini 3.1 Pro",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Gemini,
            Provider = "google",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gemini-3-pro-preview", DisplayName = "Gemini 3 Pro",
            Capabilities = FullCaps,
            SupportedEfforts = EffortLevels.Gemini,
            Provider = "google",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gemini-3-flash-preview", DisplayName = "Gemini 3 Flash",
            Capabilities = MidCaps,
            SupportedEfforts = EffortLevels.Gemini,
            Provider = "google",
        }.WithSpec(),
    ];
}
