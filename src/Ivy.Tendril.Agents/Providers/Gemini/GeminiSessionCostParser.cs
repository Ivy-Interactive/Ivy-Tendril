using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiSessionCostParser : ISessionCostParser
{
    public string AgentId => Abstractions.AgentId.Gemini;

    public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("stats", out var stats) &&
                stats.TryGetProperty("models", out var models))
            {
                long totalInput = 0;
                long totalOutput = 0;
                long totalCacheRead = 0;
                string? model = null;

                foreach (var entry in models.EnumerateArray())
                {
                    if (model is null && entry.TryGetProperty("model", out var m))
                        model = m.GetString();

                    if (entry.TryGetProperty("prompt", out var prompt))
                        totalInput += prompt.GetInt64();
                    if (entry.TryGetProperty("candidates", out var candidates))
                        totalOutput += candidates.GetInt64();
                    if (entry.TryGetProperty("cacheRead", out var cacheRead))
                        totalCacheRead += cacheRead.GetInt64();
                }

                decimal costUsd = 0;
                if (model is not null)
                {
                    var p = pricing.GetPricing(model);
                    if (p is not null)
                    {
                        costUsd =
                            (totalInput * p.InputPerMillion / 1_000_000m) +
                            (totalOutput * p.OutputPerMillion / 1_000_000m) +
                            (totalCacheRead * p.CacheReadPerMillion / 1_000_000m);
                    }
                }

                return new SessionCostResult
                {
                    SessionId = sessionId,
                    AgentId = AgentId,
                    Model = model,
                    InputTokens = (int)totalInput,
                    OutputTokens = (int)totalOutput,
                    CacheReadTokens = (int)totalCacheRead,
                    TotalCostUsd = costUsd,
                };
            }
        }
        catch (Exception)
        {
            // Graceful degradation — return partial result
        }

        return new SessionCostResult
        {
            SessionId = sessionId,
            AgentId = AgentId,
        };
    }

    public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".gemini", "history");

        if (!Directory.Exists(dir))
            return [];

        return Directory.GetFiles(dir, "*.json");
    }
}
