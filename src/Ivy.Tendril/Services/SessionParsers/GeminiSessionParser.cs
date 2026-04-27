using System.Text.Json;

namespace Ivy.Tendril.Services.SessionParsers;

public class GeminiSessionParser : ISessionParser
{
    public string Name => "gemini";

    public CostCalculation Parse(string filePath, IModelPricingService pricingService)
    {
        var totalCost = 0.0;
        var totalTokens = 0;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("messages", out var messages)) return new CostCalculation();

            foreach (var msg in messages.EnumerateArray())
            {
                var msgType = TryGetStringProperty(msg, "type");
                if (msgType != "gemini") continue;
                if (!msg.TryGetProperty("tokens", out var tokens)) continue;

                var model = TryGetStringProperty(msg, "model") ?? "gemini-3-flash-preview";
                var pricing = pricingService.GetPricing(model);

                var inputTokens = TryGetInt32Property(tokens, "input");
                var outputTokens = TryGetInt32Property(tokens, "output");
                var cachedTokens = TryGetInt32Property(tokens, "cached");

                var cost = CalculateCostFromTokens(inputTokens, outputTokens, cachedTokens, pricing);
                totalTokens += cost.TotalTokens;
                totalCost += cost.TotalCost;
            }
        }
        catch
        {
            /* Return empty on parse failure */
        }

        return new CostCalculation { TotalTokens = totalTokens, TotalCost = totalCost };
    }

    private static CostCalculation CalculateCostFromTokens(
        int inputTokens,
        int outputTokens,
        int cachedTokens,
        ModelPricing pricing)
    {
        var totalTokens = inputTokens + outputTokens;
        var totalCost = inputTokens * pricing.Input * 1e-6
                        + outputTokens * pricing.Output * 1e-6
                        + cachedTokens * pricing.CacheRead * 1e-6;

        return new CostCalculation { TotalTokens = totalTokens, TotalCost = totalCost };
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
    }

    private static int TryGetInt32Property(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetInt32() : 0;
    }
}
