using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class ModelPricingProvider : IModelPricingProvider, IModelPricingRefresher
{
    /// <summary>
    /// The lookup table and the length-ordered keys that index it, replaced as one unit.
    /// <see cref="RefreshFromModelsDevAsync" /> can run on a background thread while the UI and the
    /// cost calculation read rates, so readers take a snapshot rather than observe a half-updated
    /// dictionary.
    /// </summary>
    private sealed record Snapshot(Dictionary<string, ModelPricing> Pricing, string[] SortedKeys);

    private Snapshot _state;
    private readonly Lock _writeLock = new();

    public ModelPricingProvider()
        : this(DefaultCatalogs())
    {
    }

    public ModelPricingProvider(IEnumerable<ModelPricing> additionalPricing)
        : this(DefaultCatalogs())
    {
        AddPricing(additionalPricing);
    }

    public ModelPricingProvider(IEnumerable<IModelCatalogProvider> catalogs)
    {
        var pricing = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);

        foreach (var catalog in catalogs)
        {
            foreach (var model in catalog.GetStaticModels())
            {
                var entry = new ModelPricing
                {
                    Model = model.Id,
                    InputPerMillion = model.InputPerMillion,
                    OutputPerMillion = model.OutputPerMillion,
                    CacheWritePerMillion = model.CacheWritePerMillion,
                    CacheReadPerMillion = model.CacheReadPerMillion,
                    // GetStaticModels() never carries a PricingSource (only the async
                    // GetModelsAsync path enriches from models.dev), so in practice this labels the
                    // hardcoded catalog. That is the truth, and the cost sheet says so — until
                    // RefreshFromModelsDevAsync relabels the entries it can price from models.dev.
                    Source = model.PricingSource ?? $"Static catalog ({catalog.AgentId})",
                };

                var hasPricing = model.InputPerMillion > 0 || model.OutputPerMillion > 0;
                if (hasPricing)
                    pricing[model.Id] = entry;
                else
                    pricing.TryAdd(model.Id, entry);

            }
        }

        _state = Build(pricing);
    }

    public ModelPricing? GetPricing(string modelName)
    {
        var state = Volatile.Read(ref _state);

        if (state.Pricing.TryGetValue(modelName, out var pricing))
            return pricing;

        foreach (var key in state.SortedKeys)
        {
            if (modelName.Contains(key, StringComparison.OrdinalIgnoreCase))
                return state.Pricing[key];
        }

        return null;
    }

    public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0)
    {
        var pricing = GetPricing(modelName);
        if (pricing is null) return 0m;

        return (inputTokens * pricing.InputPerMillion / 1_000_000m)
             + (outputTokens * pricing.OutputPerMillion / 1_000_000m)
             + (cacheReadTokens * pricing.CacheReadPerMillion / 1_000_000m)
             + (cacheWriteTokens * pricing.CacheWritePerMillion / 1_000_000m);
    }

    public void AddPricing(IEnumerable<ModelPricing> additional)
    {
        lock (_writeLock)
        {
            var current = _state;
            Dictionary<string, ModelPricing>? updated = null;

            foreach (var p in additional)
            {
                updated ??= new Dictionary<string, ModelPricing>(current.Pricing, StringComparer.OrdinalIgnoreCase);
                updated[p.Model] = p;
            }

            if (updated is not null)
                Volatile.Write(ref _state, Build(updated));
        }
    }

    /// <summary>
    /// Replaces the rates of every model this provider knows with the models.dev entry that matches
    /// it, relabelling <see cref="ModelPricing.Source" /> accordingly. The constructor is
    /// deliberately synchronous and must never make a network call, so this is the async warm-up
    /// that lets computed costs track real prices instead of the hardcoded catalogs; see
    /// <c>ModelPricingWarmupService</c>, which calls it at startup and periodically thereafter.
    /// Cheap to call repeatedly: <see cref="ModelsDevPricingSource" /> caches the API response for
    /// 24 hours and returns the stale (or no) cache rather than throwing when it cannot reach the
    /// network.
    /// </summary>
    /// <returns>How many entries actually changed.</returns>
    public async Task<int> RefreshFromModelsDevAsync(CancellationToken ct = default)
    {
        var cache = await ModelsDevPricingSource.GetCacheAsync(ct);
        if (cache is null || cache.Count == 0)
            return 0;

        var current = Volatile.Read(ref _state);
        var updates = new List<ModelPricing>();

        foreach (var pricing in current.Pricing.Values)
        {
            var entry = ModelsDevPricingSource.Find(cache, pricing.Model);
            if (entry is null)
                continue;

            // Overwrites all four rates from the one source, exactly as the catalog enrichment that
            // feeds the model picker does — so the picker and the cost sheet never disagree.
            var updated = pricing with
            {
                InputPerMillion = entry.InputPerMillion,
                OutputPerMillion = entry.OutputPerMillion,
                CacheReadPerMillion = entry.CacheReadPerMillion,
                CacheWritePerMillion = entry.CacheWritePerMillion,
                Source = ModelsDevPricingSource.SourceUrl,
            };

            if (updated != pricing)
                updates.Add(updated);
        }

        AddPricing(updates);
        return updates.Count;
    }

    private static Snapshot Build(Dictionary<string, ModelPricing> pricing) =>
        new(pricing, pricing.Keys.OrderByDescending(k => k.Length).ToArray());

    private static IModelCatalogProvider[] DefaultCatalogs() =>
    [
        new Providers.Antigravity.AntigravityModelCatalog(),
        new Providers.Claude.ClaudeModelCatalog(),
        new Providers.Codex.CodexModelCatalog(),
        new Providers.Copilot.CopilotModelCatalog(),
        new Providers.OpenCode.OpenCodeModelCatalog(),
        new Providers.Ivy.IvyModelCatalog(),
    ];
}
