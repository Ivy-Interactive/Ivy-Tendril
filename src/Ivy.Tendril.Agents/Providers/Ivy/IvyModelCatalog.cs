using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvyModelCatalog : IModelCatalogProvider
{
    private readonly OpenCodeModelCatalog _inner = new();

    public string AgentId => Abstractions.AgentId.Ivy;

    public IReadOnlyList<ModelInfo> GetStaticModels()
    {
        return _inner.GetStaticModels().Select(m => new ModelInfo
        {
            Id = m.Id,
            DisplayName = m.DisplayName.Replace("OpenCode", "Ivy Agent"),
            Capabilities = m.Capabilities,
            Provider = "ivy",
            IsDefault = m.IsDefault,
            ContextWindow = m.ContextWindow,
            InputPerMillion = m.InputPerMillion,
            OutputPerMillion = m.OutputPerMillion,
            CacheReadPerMillion = m.CacheReadPerMillion,
            CacheWritePerMillion = m.CacheWritePerMillion,
        }).ToList();
    }

    public async Task<ModelCatalogResult> GetModelsAsync(CancellationToken ct = default)
    {
        var result = await _inner.GetModelsAsync(ct);
        return new ModelCatalogResult
        {
            AgentId = AgentId,
            Models = result.Models.Select(m => new ModelInfo
            {
                Id = m.Id,
                DisplayName = m.DisplayName.Replace("OpenCode", "Ivy Agent"),
                Capabilities = m.Capabilities,
                Provider = "ivy",
                IsDefault = m.IsDefault,
                ContextWindow = m.ContextWindow,
                InputPerMillion = m.InputPerMillion,
                OutputPerMillion = m.OutputPerMillion,
                CacheReadPerMillion = m.CacheReadPerMillion,
                CacheWritePerMillion = m.CacheWritePerMillion,
            }).ToList(),
            Source = result.Source,
            RetrievedAt = result.RetrievedAt,
            ExpiresAt = result.ExpiresAt,
        };
    }
}
