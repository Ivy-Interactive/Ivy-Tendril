using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class ModelsDevPricingSource
{
    private static readonly Uri ApiUrl = new("https://models.dev/api.json");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private static Dictionary<string, ModelPricingEntry>? _cache;
    private static DateTimeOffset _cacheExpiry;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public record ModelPricingEntry(
        decimal InputPerMillion,
        decimal OutputPerMillion,
        decimal CacheReadPerMillion,
        decimal CacheWritePerMillion,
        int? ContextWindow,
        int? MaxOutputTokens);

    public static async Task<Dictionary<string, ModelPricingEntry>?> GetCacheAsync(CancellationToken ct = default)
    {
        if (_cache is not null && DateTimeOffset.UtcNow < _cacheExpiry)
            return _cache;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is not null && DateTimeOffset.UtcNow < _cacheExpiry)
                return _cache;

            var json = await Http.GetStringAsync(ApiUrl, ct);
            _cache = ParseApiJson(json);
            _cacheExpiry = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _cache;
        }
        catch
        {
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static Dictionary<string, ModelPricingEntry>? ParseApiJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, ModelPricingEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in doc.RootElement.EnumerateObject())
            {
                if (!provider.Value.TryGetProperty("models", out var models))
                    continue;

                foreach (var model in models.EnumerateObject())
                {
                    var modelObj = model.Value;

                    decimal input = 0, output = 0, cacheRead = 0, cacheWrite = 0;
                    int? context = null, maxOutput = null;

                    if (modelObj.TryGetProperty("cost", out var cost))
                    {
                        input = GetDecimal(cost, "input");
                        output = GetDecimal(cost, "output");
                        cacheRead = GetDecimal(cost, "cache_read");
                        cacheWrite = GetDecimal(cost, "cache_write");
                    }

                    if (modelObj.TryGetProperty("limit", out var limit))
                    {
                        if (limit.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.Number)
                            context = ctx.GetInt32();
                        if (limit.TryGetProperty("output", out var outp) && outp.ValueKind == JsonValueKind.Number)
                            maxOutput = outp.GetInt32();
                    }

                    if (input == 0 && output == 0) continue;

                    var entry = new ModelPricingEntry(input, output, cacheRead, cacheWrite, context, maxOutput);

                    var id = model.Name;
                    if (modelObj.TryGetProperty("id", out var idProp) && idProp.GetString() is { } idStr)
                        id = idStr;

                    result.TryAdd(id, entry);

                    if (modelObj.TryGetProperty("name", out var nameProp) && nameProp.GetString() is { } name)
                        result.TryAdd(name, entry);
                }
            }

            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static decimal GetDecimal(JsonElement parent, string prop)
    {
        if (!parent.TryGetProperty(prop, out var el)) return 0;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(el.GetString(), out var d) => d,
            _ => 0,
        };
    }
}
