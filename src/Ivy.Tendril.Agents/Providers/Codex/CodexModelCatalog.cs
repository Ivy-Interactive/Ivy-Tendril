using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Codex;

public sealed class CodexModelCatalog : CachedModelCatalogProvider
{
    private static readonly ModelCapabilities DefaultCaps =
        ModelCapabilities.Reasoning |
        ModelCapabilities.CodeGeneration |
        ModelCapabilities.ToolUse |
        ModelCapabilities.Streaming;

    public override string AgentId => Abstractions.AgentId.Codex;

    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new ModelInfo
        {
            Id = "gpt-6-astra", DisplayName = "GPT-6 Astra",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-5.6-sol", DisplayName = "GPT-5.6-Sol",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-5.6-terra", DisplayName = "GPT-5.6-Terra",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai", IsDefault = true,
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-5.6-luna", DisplayName = "GPT-5.6-Luna",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-5.5", DisplayName = "GPT-5.5",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-5.4", DisplayName = "GPT-5.4",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-5.4-mini", DisplayName = "GPT-5.4 Mini",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-5.3-codex", DisplayName = "GPT-5.3 Codex",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "o3", DisplayName = "O3",
            Capabilities = DefaultCaps | ModelCapabilities.ExtendedThinking,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "o4-mini", DisplayName = "O4 Mini",
            Capabilities = DefaultCaps | ModelCapabilities.ExtendedThinking,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "gpt-4.1", DisplayName = "GPT-4.1",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
        new ModelInfo
        {
            Id = "codex-mini", DisplayName = "Codex Mini",
            Capabilities = DefaultCaps,
            SupportedEfforts = EffortLevels.Codex,
            Provider = "openai",
        }.WithSpec(),
    ];

    protected override async Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "codex", ["debug", "models", "--bundled"], TimeSpan.FromSeconds(15), ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;

        return ParseModelsJson(stdout);
    }

    private IReadOnlyList<ModelInfo>? ParseModelsJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement modelsElement;
            if (root.ValueKind == JsonValueKind.Array)
                modelsElement = root;
            else if (root.TryGetProperty("models", out var m))
                modelsElement = m;
            else
                return null;

            var staticModels = GetStaticModels().ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
            var results = new List<ModelInfo>();

            foreach (var item in modelsElement.EnumerateArray())
            {
                var slug = item.TryGetProperty("slug", out var s) ? s.GetString()
                         : item.TryGetProperty("id", out var id) ? id.GetString()
                         : null;
                if (slug is null) continue;

                var visibility = item.TryGetProperty("visibility", out var v) ? v.GetString() : "list";
                if (visibility == "hide") continue;

                var displayName = item.TryGetProperty("display_name", out var dn) ? dn.GetString()
                                : item.TryGetProperty("name", out var n) ? n.GetString()
                                : slug;

                if (staticModels.TryGetValue(slug, out var known))
                {
                    results.Add(known with { DisplayName = displayName ?? known.DisplayName });
                }
                else
                {
                    results.Add(new ModelInfo
                    {
                        Id = slug,
                        DisplayName = displayName ?? slug,
                        Capabilities = DefaultCaps,
                        SupportedEfforts = EffortLevels.Codex,
                        Provider = "openai",
                    });
                }
            }

            return results.Count > 0 ? results : null;
        }
        catch
        {
            return null;
        }
    }
}
