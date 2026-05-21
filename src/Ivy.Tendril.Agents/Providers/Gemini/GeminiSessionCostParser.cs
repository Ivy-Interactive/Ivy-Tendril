using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiSessionCostParser : ISessionCostParser
{
    public string AgentId => Abstractions.AgentId.Gemini;

    public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        string? model = null;
        int inputTokens = 0;
        int outputTokens = 0;
        int cacheReadTokens = 0;
        int cacheWriteTokens = 0;
        decimal totalCost = 0;

        try
        {
            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return BuildResult(sessionId, model, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens, totalCost);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Extract session_id if available
            if (root.TryGetProperty("session_id", out var sidProp))
            {
                var sid = sidProp.GetString();
                if (!string.IsNullOrEmpty(sid))
                    sessionId = sid;
            }

            // Extract token usage from stats.models
            if (root.TryGetProperty("stats", out var stats) &&
                stats.TryGetProperty("models", out var models) &&
                models.ValueKind == JsonValueKind.Object)
            {
                foreach (var modelEntry in models.EnumerateObject())
                {
                    model ??= modelEntry.Name;

                    if (modelEntry.Value.TryGetProperty("tokens", out var tokens))
                    {
                        inputTokens += tokens.TryGetProperty("input", out var inp) ? inp.GetInt32() : 0;
                        outputTokens += tokens.TryGetProperty("candidates", out var cand) ? cand.GetInt32() : 0;
                        cacheReadTokens += tokens.TryGetProperty("cached", out var cached) ? cached.GetInt32() : 0;
                    }
                }
            }

            // Calculate cost via pricing provider
            if (model is not null)
            {
                totalCost = pricing.CalculateCost(model, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens);
            }
        }
        catch (JsonException)
        {
            // Return partial result on parse failure
        }
        catch (IOException)
        {
            // Return partial result on read failure
        }

        return BuildResult(sessionId, model, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens, totalCost);
    }

    public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
    {
        var basePath = projectPath ?? GetDefaultHistoryPath();
        if (!Directory.Exists(basePath)) return [];

        return Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    private static SessionCostResult BuildResult(
        string sessionId, string? model,
        int inputTokens, int outputTokens, int cacheReadTokens, int cacheWriteTokens,
        decimal totalCost)
    {
        return new SessionCostResult
        {
            SessionId = sessionId,
            AgentId = Abstractions.AgentId.Gemini,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
            TotalCostUsd = totalCost,
        };
    }

    private static string GetDefaultHistoryPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".gemini", "history");
    }
}
