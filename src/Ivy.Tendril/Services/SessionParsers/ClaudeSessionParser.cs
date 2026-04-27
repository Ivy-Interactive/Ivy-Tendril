using Ivy.Tendril.Helpers;
using System.Text.Json;

namespace Ivy.Tendril.Services.SessionParsers;

public class ClaudeSessionParser : ISessionParser
{
    public string Name => "claude";

    public CostCalculation Parse(string filePath, IModelPricingService pricingService)
    {
        var totalCost = 0.0;
        var totalTokens = 0;
        var processedIds = new HashSet<string>();

        foreach (var line in FileHelper.EnumerateLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var obj = JsonDocument.Parse(line);
                var root = obj.RootElement;

                if (root.GetProperty("type").GetString() != "assistant") continue;
                if (!root.TryGetProperty("message", out var message)) continue;
                if (!message.TryGetProperty("usage", out var usage)) continue;

                // Deduplicate: Claude Code writes one JSONL line per content block,
                // but each line carries the full message with the same usage data.
                if (message.TryGetProperty("id", out var idProp))
                {
                    var msgId = idProp.GetString();
                    if (msgId != null && !processedIds.Add(msgId))
                        continue; // Already counted this message
                }

                var model = message.TryGetProperty("model", out var m)
                    ? m.GetString() ?? "claude-opus-4"
                    : "claude-opus-4";

                var pricing = pricingService.GetPricing(model);

                var priceInput = pricing.Input * 1e-6;
                var priceOutput = pricing.Output * 1e-6;
                var priceCacheWrite = pricing.CacheWrite * 1e-6;
                var priceCacheRead = pricing.CacheRead * 1e-6;

                var inputTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                var outputTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
                var cacheReadTokens = usage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0;

                // TotalTokens tracks actual work (non-cached input + output) so the
                // number shown to the user reflects what the model genuinely processed,
                // not the much larger cache-dominated throughput.
                totalTokens += inputTokens + outputTokens;
                totalCost += inputTokens * priceInput;
                totalCost += outputTokens * priceOutput;
                totalCost += cacheReadTokens * priceCacheRead;

                if (usage.TryGetProperty("cache_creation", out var cacheCreation))
                {
                    var cacheFiveMinutes = cacheCreation.TryGetProperty("ephemeral_5m_input_tokens", out var c5)
                        ? c5.GetInt32()
                        : 0;
                    var cacheOneHour = cacheCreation.TryGetProperty("ephemeral_1h_input_tokens", out var c1)
                        ? c1.GetInt32()
                        : 0;
                    totalCost += (cacheFiveMinutes + cacheOneHour) * priceCacheWrite;
                }
                else if (usage.TryGetProperty("cache_creation_input_tokens", out var ccTokens))
                {
                    var cacheCreationTokens = ccTokens.GetInt32();
                    totalCost += cacheCreationTokens * priceCacheWrite;
                }
            }
            catch
            {
                /* Skip malformed lines */
            }
        }

        return new CostCalculation { TotalTokens = totalTokens, TotalCost = totalCost };
    }
}
