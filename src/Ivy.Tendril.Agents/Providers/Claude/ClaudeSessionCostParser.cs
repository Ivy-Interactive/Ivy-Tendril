using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Claude;

public sealed class ClaudeSessionCostParser : ISessionCostParser
{
    public string AgentId => Abstractions.AgentId.Claude;

    public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        string? model = null;
        int inputTokens = 0;
        int outputTokens = 0;
        int cacheReadTokens = 0;
        int cacheWriteTokens = 0;
        decimal totalCost = 0;
        DateTimeOffset? startedAt = null;
        DateTimeOffset? completedAt = null;
        var processedMessageIds = new HashSet<string>();

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) continue;
                var type = typeProp.GetString();

                if (type == "system" && root.TryGetProperty("subtype", out var st) && st.GetString() == "init")
                {
                    model ??= root.TryGetProperty("model", out var m) ? m.GetString() : null;
                    startedAt ??= DateTimeOffset.UtcNow;
                }
                else if (type == "assistant" && root.TryGetProperty("message", out var msg))
                {
                    // Claude outputs one JSONL line per content block, but each line
                    // carries the full message with identical usage data. Deduplicate.
                    if (msg.TryGetProperty("id", out var idProp))
                    {
                        var msgId = idProp.GetString();
                        if (msgId is not null && !processedMessageIds.Add(msgId))
                            continue;
                    }

                    if (msg.TryGetProperty("usage", out var usage))
                    {
                        inputTokens += usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                        outputTokens += usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
                        cacheReadTokens += usage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0;
                        cacheWriteTokens += ExtractCacheWriteTokens(usage);
                    }

                    model ??= msg.TryGetProperty("model", out var mdl) ? mdl.GetString() : null;
                }
                else if (type == "result")
                {
                    if (root.TryGetProperty("total_cost_usd", out var cost))
                        totalCost = cost.GetDecimal();

                    completedAt = DateTimeOffset.UtcNow;

                    if (root.TryGetProperty("usage", out var resultUsage))
                    {
                        inputTokens = resultUsage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : inputTokens;
                        outputTokens = resultUsage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : outputTokens;
                        cacheReadTokens = resultUsage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : cacheReadTokens;
                        cacheWriteTokens = resultUsage.TryGetProperty("cache_creation_input_tokens", out var cw) ? cw.GetInt32() : cacheWriteTokens;
                    }
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        if (totalCost == 0 && model is not null)
        {
            totalCost = pricing.CalculateCost(model, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens);
        }

        return new SessionCostResult
        {
            SessionId = sessionId,
            AgentId = AgentId,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
            TotalCostUsd = totalCost,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };
    }

    public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
    {
        var basePath = projectPath ?? GetDefaultProjectsPath();
        if (!Directory.Exists(basePath)) return [];

        return Directory.GetFiles(basePath, "*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    private static int ExtractCacheWriteTokens(JsonElement usage)
    {
        // Format 1: nested object with ephemeral TTL keys
        if (usage.TryGetProperty("cache_creation", out var cacheCreation))
        {
            var tokens = 0;
            if (cacheCreation.TryGetProperty("ephemeral_5m_input_tokens", out var c5))
                tokens += c5.GetInt32();
            if (cacheCreation.TryGetProperty("ephemeral_1h_input_tokens", out var c1))
                tokens += c1.GetInt32();
            return tokens;
        }

        // Format 2: flat field
        if (usage.TryGetProperty("cache_creation_input_tokens", out var flat))
            return flat.GetInt32();

        return 0;
    }

    private static string GetDefaultProjectsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", "projects");
    }
}
