using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeModelCatalog : CachedModelCatalogProvider
{
    private static readonly ModelCapabilities DefaultCaps =
        ModelCapabilities.CodeGeneration | ModelCapabilities.ToolUse | ModelCapabilities.Streaming;

    public override string AgentId => Abstractions.AgentId.OpenCode;

    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new ModelInfo
        {
            Id = "moonshotai/Kimi-K3", DisplayName = "Kimi k3",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "moonshot", IsDefault = true,
        }.WithSpec(),
        new ModelInfo
        {
            Id = "kimi-k2", DisplayName = "Kimi k2",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "moonshot",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "deepseek-v3", DisplayName = "DeepSeek V3",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "deepseek",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "deepseek-r1", DisplayName = "DeepSeek R1",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "deepseek",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-fable-5", DisplayName = "Claude Fable 5",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-opus-5-1", DisplayName = "Claude Opus 5.1",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-opus-5", DisplayName = "Claude Opus 5",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-opus-4-7", DisplayName = "Claude Opus 4.7",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-sonnet-5-1", DisplayName = "Claude Sonnet 5.1",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-5.1", DisplayName = "Claude 5.1",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "claude-sonnet-4-6", DisplayName = "Claude Sonnet 4.6",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.Claude, Provider = "anthropic",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-5.5", DisplayName = "GPT-5.5",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "default", DisplayName = "OpenCode Default",
            Capabilities = DefaultCaps, SupportedEfforts = EffortLevels.OpenCode, Provider = "opencode",
        },
    ];

    protected override async Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "opencode", ["models"], TimeSpan.FromSeconds(15), ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;

        return await ParseModelsListAsync(stdout, ct);
    }

    internal static Task<IReadOnlyList<ModelInfo>?> ParseModelsListAsync(string output, CancellationToken ct = default)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return Task.FromResult<IReadOnlyList<ModelInfo>?>(null);

        var results = new List<ModelInfo>();
        var first = true;

        foreach (var id in lines)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;

            var provider = ExtractProvider(id);
            var spec = ModelSpecs.Find(id);

            results.Add(new ModelInfo
            {
                Id = id,
                DisplayName = id,
                Capabilities = DefaultCaps,
                SupportedEfforts = provider == "anthropic" || id.Contains("claude", StringComparison.OrdinalIgnoreCase)
                    ? EffortLevels.Claude
                    : EffortLevels.OpenCode,
                Provider = provider,
                IsDefault = first,
                ContextWindow = spec?.ContextWindow,
                MaxOutputTokens = spec?.MaxOutputTokens,
                InputPerMillion = spec?.InputPerMillion ?? 0m,
                OutputPerMillion = spec?.OutputPerMillion ?? 0m,
                CacheReadPerMillion = spec?.CacheReadPerMillion ?? 0m,
                CacheWritePerMillion = spec?.CacheWritePerMillion ?? 0m,
            });
            first = false;
        }

        var sorted = ModelCatalogSorter.Sort(results);
        return Task.FromResult<IReadOnlyList<ModelInfo>?>(sorted.Count > 0 ? sorted : null);
    }

    private static string ExtractProvider(string modelId)
    {
        var slash = modelId.IndexOf('/');
        return slash > 0 ? modelId[..slash] : "opencode";
    }

    /// <summary>
    /// Resolves the context and output token limits for a formatted model string (e.g.
    /// <c>anthropic/claude-opus-5</c>), checking the static catalog first and falling back to the
    /// canonical <see cref="ModelSpecs"/> table. Returns null when neither source knows the model.
    /// </summary>
    internal static (int Context, int Output)? TryGetLimits(string formattedModel)
    {
        if (string.IsNullOrWhiteSpace(formattedModel))
            return null;

        var slash = formattedModel.IndexOf('/');
        var bareModel = slash > 0 ? formattedModel[(slash + 1)..] : formattedModel;

        var staticMatch = new OpenCodeModelCatalog().GetStaticModels().FirstOrDefault(m =>
            m.ContextWindow is not null && m.MaxOutputTokens is not null &&
            (string.Equals(m.Id, formattedModel, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(m.Id, bareModel, StringComparison.OrdinalIgnoreCase)));

        if (staticMatch is not null)
            return (staticMatch.ContextWindow!.Value, staticMatch.MaxOutputTokens!.Value);

        return ModelSpecs.Find(formattedModel) is { } spec
            ? (spec.ContextWindow, spec.MaxOutputTokens)
            : null;
    }
}
