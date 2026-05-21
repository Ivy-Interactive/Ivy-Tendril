using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class ModelPricingProvider : IModelPricingProvider
{
    private static readonly Dictionary<string, ModelPricing> KnownPricing = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-4-20250514"] = new()
        {
            Model = "claude-opus-4-20250514",
            InputPerMillion = 15m,
            OutputPerMillion = 75m,
            CacheWritePerMillion = 18.75m,
            CacheReadPerMillion = 1.50m,
        },
        ["claude-sonnet-4-5-20250514"] = new()
        {
            Model = "claude-sonnet-4-5-20250514",
            InputPerMillion = 3m,
            OutputPerMillion = 15m,
            CacheWritePerMillion = 3.75m,
            CacheReadPerMillion = 0.30m,
        },
        ["claude-sonnet-4-20250514"] = new()
        {
            Model = "claude-sonnet-4-20250514",
            InputPerMillion = 3m,
            OutputPerMillion = 15m,
            CacheWritePerMillion = 3.75m,
            CacheReadPerMillion = 0.30m,
        },
        ["claude-haiku-3-5-20241022"] = new()
        {
            Model = "claude-haiku-3-5-20241022",
            InputPerMillion = 0.80m,
            OutputPerMillion = 4m,
            CacheWritePerMillion = 1m,
            CacheReadPerMillion = 0.08m,
        },
    };

    private readonly Dictionary<string, ModelPricing> _pricing;

    public ModelPricingProvider()
        : this([])
    {
    }

    public ModelPricingProvider(IEnumerable<ModelPricing> additionalPricing)
    {
        _pricing = new Dictionary<string, ModelPricing>(KnownPricing, StringComparer.OrdinalIgnoreCase);
        foreach (var p in additionalPricing)
            _pricing[p.Model] = p;
    }

    public ModelPricing? GetPricing(string modelName)
    {
        if (_pricing.TryGetValue(modelName, out var pricing))
            return pricing;

        foreach (var (key, value) in _pricing)
        {
            if (modelName.Contains(key, StringComparison.OrdinalIgnoreCase))
                return value;
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
}
