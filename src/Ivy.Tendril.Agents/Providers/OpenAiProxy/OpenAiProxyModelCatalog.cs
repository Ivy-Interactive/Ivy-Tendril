using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyModelCatalog : IModelCatalogProvider
{
    private static readonly ModelCapabilities DefaultCaps =
        ModelCapabilities.CodeGeneration | ModelCapabilities.ToolUse | ModelCapabilities.Streaming;

    private readonly OpenCodeModelCatalog _inner = new();
    private readonly Func<string?>? _baseUrlProvider;

    public OpenAiProxyModelCatalog(Func<string?>? baseUrlProvider = null)
    {
        _baseUrlProvider = baseUrlProvider;
    }

    public string AgentId => Abstractions.AgentId.OpenAiProxy;

    private bool IsBerget => _baseUrlProvider?.Invoke()?.Contains("api.berget.ai") ?? false;

    public IReadOnlyList<ModelInfo> GetStaticModels()
    {
        if (IsBerget)
        {
            return
            [
                new ModelInfo
                {
                    Id = "moonshotai/Kimi-K3",
                    DisplayName = "Kimi K3",
                    Capabilities = DefaultCaps,
                    Provider = "berget",
                    IsDefault = true,
                    ContextWindow = 200000,
                }
            ];
        }

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
        if (IsBerget)
        {
            return new ModelCatalogResult
            {
                AgentId = AgentId,
                Models = GetStaticModels(),
                Source = ModelCatalogSource.Static,
                RetrievedAt = DateTimeOffset.UtcNow,
            };
        }

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
