using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyModelCatalog : IModelCatalogProvider
{
    private readonly OpenCodeModelCatalog _inner = new();

    public string AgentId => Abstractions.AgentId.OpenAiProxy;

    public IReadOnlyList<ModelInfo> GetStaticModels()
    {
        return _inner.GetStaticModels().Select(m => new ModelInfo
        {
            Id = m.Id,
            DisplayName = m.DisplayName.Replace("OpenCode", "OpenAI Proxy"),
            Capabilities = m.Capabilities,
            Provider = "openaiproxy",
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
                DisplayName = m.DisplayName.Replace("OpenCode", "OpenAI Proxy"),
                Capabilities = m.Capabilities,
                Provider = "openaiproxy",
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
