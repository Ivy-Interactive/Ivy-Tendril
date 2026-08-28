using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Runtime;

public abstract class CachedModelCatalogProvider : IModelCatalogProvider
{
    private volatile ModelCatalogResult? _cached;
    private readonly TimeSpan _cacheDuration;

    protected CachedModelCatalogProvider(TimeSpan? cacheDuration = null)
    {
        _cacheDuration = cacheDuration ?? TimeSpan.FromHours(1);
    }

    public abstract string AgentId { get; }
    public abstract IReadOnlyList<ModelInfo> GetStaticModels();

    protected virtual Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ModelInfo>?>(null);

    public async Task<ModelCatalogResult> GetModelsAsync(CancellationToken ct = default)
    {
        var current = _cached;
        if (current is { ExpiresAt: { } exp } && exp > DateTimeOffset.UtcNow)
            return current with { Source = ModelCatalogSource.Cached };

        IReadOnlyList<ModelInfo> models;
        ModelCatalogSource source;

        try
        {
            var discovered = await DiscoverModelsAsync(ct);
            if (discovered is { Count: > 0 })
            {
                models = discovered;
                source = ModelCatalogSource.Dynamic;
            }
            else
            {
                models = GetStaticModels();
                source = current is null ? ModelCatalogSource.Static : ModelCatalogSource.Fallback;
            }
        }
        catch
        {
            models = GetStaticModels();
            source = current is null ? ModelCatalogSource.Static : ModelCatalogSource.Fallback;
        }

        models = ModelCatalogSorter.Sort(models);
        models = await EnrichWithModelsDevAsync(models, ct);

        var result = new ModelCatalogResult
        {
            AgentId = AgentId,
            Models = models,
            Source = source,
            RetrievedAt = DateTimeOffset.UtcNow,
            ExpiresAt = source == ModelCatalogSource.Dynamic
                ? DateTimeOffset.UtcNow.Add(_cacheDuration)
                : null,
        };

        if (source == ModelCatalogSource.Dynamic)
            _cached = result;

        return result;
    }

    private static async Task<IReadOnlyList<ModelInfo>> EnrichWithModelsDevAsync(
        IReadOnlyList<ModelInfo> models, CancellationToken ct)
    {
        var cache = await ModelsDevPricingSource.GetCacheAsync(ct);
        if (cache is null)
            return models;

        var enriched = new List<ModelInfo>(models.Count);
        foreach (var model in models)
        {
            var entry = ModelsDevPricingSource.Find(cache, model.Id);
            if (entry is not null)
            {
                enriched.Add(model with
                {
                    InputPerMillion = entry.InputPerMillion,
                    OutputPerMillion = entry.OutputPerMillion,
                    CacheReadPerMillion = entry.CacheReadPerMillion,
                    CacheWritePerMillion = entry.CacheWritePerMillion,
                    ContextWindow = entry.ContextWindow ?? model.ContextWindow,
                    PricingSource = ModelsDevPricingSource.SourceUrl,
                });
            }
            else
            {
                enriched.Add(model);
            }
        }

        return enriched;
    }
}
