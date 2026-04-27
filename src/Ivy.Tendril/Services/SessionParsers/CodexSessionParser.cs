using System.Text.Json;

namespace Ivy.Tendril.Services.SessionParsers;

public class CodexSessionParser : ISessionParser
{
    public string Name => "codex";

    public CostCalculation Parse(string filePath, IModelPricingService pricingService)
    {
        var model = "o4-mini";
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var totalCachedTokens = 0;

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var entryType = TryGetStringProperty(root, "type");

                if (entryType == "turn_context" &&
                    root.TryGetProperty("payload", out var turnPayload) &&
                    turnPayload.TryGetProperty("model", out var turnModel))
                    model = turnModel.GetString() ?? model;

                if (entryType == "event_msg" &&
                    root.TryGetProperty("payload", out var payload) &&
                    TryGetStringProperty(payload, "type") == "token_count" &&
                    payload.TryGetProperty("info", out var info) &&
                    info.ValueKind != JsonValueKind.Null &&
                    info.TryGetProperty("total_token_usage", out var usage))
                {
                    totalInputTokens = TryGetInt32Property(usage, "input_tokens");
                    totalOutputTokens = TryGetInt32Property(usage, "output_tokens");
                    totalCachedTokens = TryGetInt32Property(usage, "cached_input_tokens");
                    var reasoningTokens = TryGetInt32Property(usage, "reasoning_output_tokens");
                    totalOutputTokens += reasoningTokens;
                }
            }
            catch
            {
                /* Skip malformed lines */
            }
        }

        var pricing = pricingService.GetPricing(model);
        return CalculateCostFromTokens(totalInputTokens, totalOutputTokens, totalCachedTokens, pricing);
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
