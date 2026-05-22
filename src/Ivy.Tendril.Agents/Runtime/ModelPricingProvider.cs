using System.Reflection;
using Ivy.Tendril.Agents.Abstractions;
using YamlDotNet.Serialization;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class ModelPricingProvider : IModelPricingProvider
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();

    private readonly Dictionary<string, ModelPricing> _pricing;
    private readonly string[] _sortedKeys;

    public ModelPricingProvider()
        : this([])
    {
    }

    public ModelPricingProvider(IEnumerable<ModelPricing> additionalPricing)
    {
        _pricing = LoadEmbeddedPricing();
        foreach (var p in additionalPricing)
            _pricing[p.Model] = p;
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

    private static Dictionary<string, ModelPricing> LoadEmbeddedPricing()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Ivy.Tendril.Agents.Assets.models.yaml";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(stream);
        var yaml = reader.ReadToEnd();

        var config = YamlDeserializer.Deserialize<Dictionary<string, object>>(yaml);
        var result = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);

        if (config.TryGetValue("models", out var modelsObj) && modelsObj is Dictionary<object, object> models)
        {
            foreach (var kvp in models)
            {
                var modelName = kvp.Key.ToString() ?? "";
                if (kvp.Value is Dictionary<object, object> props)
                {
                    result[modelName] = new ModelPricing
                    {
                        Model = modelName,
                        InputPerMillion = Convert.ToDecimal(props["input"]),
                        OutputPerMillion = Convert.ToDecimal(props["output"]),
                        CacheWritePerMillion = Convert.ToDecimal(props["cacheWrite"]),
                        CacheReadPerMillion = Convert.ToDecimal(props["cacheRead"]),
                    };
                }
            }
        }

        return result;
    }
}
