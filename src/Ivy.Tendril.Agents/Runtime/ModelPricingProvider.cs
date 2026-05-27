using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class ModelPricingProvider : IModelPricingProvider
{
    private readonly Dictionary<string, ModelPricing> _pricing;
    private string[] _sortedKeys;

    public ModelPricingProvider()
        : this(DefaultCatalogs())
    {
    }

    public ModelPricingProvider(IEnumerable<ModelPricing> additionalPricing)
        : this(DefaultCatalogs())
    {
        foreach (var p in additionalPricing)
        {
            _pricing[p.Model] = p;
        }
        _sortedKeys = _pricing.Keys.OrderByDescending(k => k.Length).ToArray();
    }

    public ModelPricingProvider(IEnumerable<IModelCatalogProvider> catalogs)
    {
        _pricing = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);

        foreach (var catalog in catalogs)
        {
            foreach (var model in catalog.GetStaticModels())
            {
                var pricing = new ModelPricing
                {
                    Model = model.Id,
                    InputPerMillion = model.InputPerMillion,
                    OutputPerMillion = model.OutputPerMillion,
                    CacheWritePerMillion = model.CacheWritePerMillion,
                    CacheReadPerMillion = model.CacheReadPerMillion,
                };

                var hasPricing = model.InputPerMillion > 0 || model.OutputPerMillion > 0;
                if (hasPricing)
                    _pricing[model.Id] = pricing;
                else
                    _pricing.TryAdd(model.Id, pricing);

                if (model.Alias is not null)
                    _pricing.TryAdd(model.Alias, pricing);
            }
        }

        _sortedKeys = _pricing.Keys.OrderByDescending(k => k.Length).ToArray();
    }

    public ModelPricing? GetPricing(string modelName)
    {
        if (_pricing.TryGetValue(modelName, out var pricing))
            return pricing;

        foreach (var key in _sortedKeys)
        {
            if (modelName.Contains(key, StringComparison.OrdinalIgnoreCase))
                return _pricing[key];
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
        var changed = false;
        foreach (var p in additional)
        {
            _pricing[p.Model] = p;
            changed = true;
        }
        if (changed)
            _sortedKeys = _pricing.Keys.OrderByDescending(k => k.Length).ToArray();
    }

    private static IModelCatalogProvider[] DefaultCatalogs() =>
    [
        new Providers.Antigravity.AntigravityModelCatalog(),
        new Providers.Claude.ClaudeModelCatalog(),
        new Providers.Codex.CodexModelCatalog(),
        new Providers.Copilot.CopilotModelCatalog(),
        new Providers.OpenCode.OpenCodeModelCatalog(),
    ];
}
