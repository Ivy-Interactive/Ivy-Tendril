using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvyModelCatalog : IModelCatalogProvider
{
    private static readonly ModelCapabilities DefaultCaps =
        ModelCapabilities.CodeGeneration | ModelCapabilities.ToolUse | ModelCapabilities.Streaming;

    private readonly OpenCodeModelCatalog _inner = new();

    public string AgentId => Abstractions.AgentId.Ivy;

    public IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new()
        {
            Id = "ivy-stem", DisplayName = "Ivy Stem",
            Capabilities = DefaultCaps, Provider = "ivy", IsDefault = true,
        },
        new()
        {
            Id = "ivy-branch", DisplayName = "Ivy Branch",
            Capabilities = DefaultCaps, Provider = "ivy",
        },
        new()
        {
            Id = "ivy-leaf", DisplayName = "Ivy Leaf",
            Capabilities = DefaultCaps, Provider = "ivy",
        },
        new()
        {
            Id = "default", DisplayName = "Ivy Default",
            Capabilities = DefaultCaps, Provider = "ivy",
        },
    ];

    public async Task<ModelCatalogResult> GetModelsAsync(CancellationToken ct = default)
    {
        var staticModels = GetStaticModels();
        try
        {
            var result = await _inner.GetModelsAsync(ct);
            var ivyModels = result.Models
                .Where(m => m.Id.StartsWith("ivy", StringComparison.OrdinalIgnoreCase) ||
                            "ivy".Equals(m.Provider, StringComparison.OrdinalIgnoreCase))
                .Select(m => new ModelInfo
                {
                    Id = m.Id,
                    DisplayName = FormatIvyDisplayName(m.Id, m.DisplayName),
                    Capabilities = m.Capabilities,
                    Provider = "ivy",
                    IsDefault = m.IsDefault,
                    ContextWindow = m.ContextWindow,
                    InputPerMillion = m.InputPerMillion,
                    OutputPerMillion = m.OutputPerMillion,
                    CacheReadPerMillion = m.CacheReadPerMillion,
                    CacheWritePerMillion = m.CacheWritePerMillion,
                })
                .ToList();

            if (ivyModels.Count > 0)
            {
                return new ModelCatalogResult
                {
                    AgentId = AgentId,
                    Models = ivyModels,
                    Source = result.Source,
                    RetrievedAt = result.RetrievedAt,
                    ExpiresAt = result.ExpiresAt,
                };
            }
        }
        catch
        {
            // Fallback to static model list
        }

        return new ModelCatalogResult
        {
            AgentId = AgentId,
            Models = staticModels,
            Source = ModelCatalogSource.Static,
            RetrievedAt = DateTimeOffset.UtcNow,
        };
    }

    private static string FormatIvyDisplayName(string id, string? rawDisplayName)
    {
        if (string.Equals(id, "ivy-stem", StringComparison.OrdinalIgnoreCase)) return "Ivy Stem";
        if (string.Equals(id, "ivy-branch", StringComparison.OrdinalIgnoreCase)) return "Ivy Branch";
        if (string.Equals(id, "ivy-leaf", StringComparison.OrdinalIgnoreCase)) return "Ivy Leaf";
        if (string.Equals(id, "default", StringComparison.OrdinalIgnoreCase)) return "Ivy Default";

        if (!string.IsNullOrEmpty(rawDisplayName) && rawDisplayName.StartsWith("Ivy", StringComparison.OrdinalIgnoreCase))
            return rawDisplayName;

        return "Ivy " + id.Replace("ivy-", "").Replace("-", " ");
    }
}
