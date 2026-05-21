using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Codex;

public sealed class CodexSessionCostParser : ISessionCostParser
{
    public string AgentId => Abstractions.AgentId.Codex;

    public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        string? model = null;
        int inputTokens = 0;
        int outputTokens = 0;
        int cacheReadTokens = 0;
        DateTimeOffset? startedAt = null;
        DateTimeOffset? completedAt = null;

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) continue;
                var type = typeProp.GetString();

                if (type == "thread.started")
                {
                    startedAt ??= DateTimeOffset.UtcNow;
                    if (root.TryGetProperty("thread_id", out var tid))
                        sessionId = tid.GetString() ?? sessionId;
                }
                else if (type == "turn.completed")
                {
                    completedAt = DateTimeOffset.UtcNow;

                    if (root.TryGetProperty("usage", out var usage))
                    {
                        inputTokens += usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                        outputTokens += usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
                        cacheReadTokens += usage.TryGetProperty("cached_input_tokens", out var cr) ? cr.GetInt32() : 0;
                    }

                    model ??= root.TryGetProperty("model", out var mdl) ? mdl.GetString() : null;
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        decimal totalCost = 0;
        if (model is not null)
        {
            totalCost = pricing.CalculateCost(model, inputTokens, outputTokens, cacheReadTokens);
        }

        return new SessionCostResult
        {
            SessionId = sessionId,
            AgentId = AgentId,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = 0,
            TotalCostUsd = totalCost,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };
    }

    public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
    {
        var basePath = projectPath ?? GetDefaultSessionsPath();
        if (!Directory.Exists(basePath)) return [];

        return Directory.GetFiles(basePath, "*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    private static string GetDefaultSessionsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex", "sessions");
    }
}
