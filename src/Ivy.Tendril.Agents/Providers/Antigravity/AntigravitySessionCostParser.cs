using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravitySessionCostParser : ISessionCostParser
{
    public string AgentId => Abstractions.AgentId.Antigravity;

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

        try
        {
            if (File.Exists(filePath))
            {
                foreach (var line in File.ReadLines(filePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line[0] != '{') continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("created_at", out var caProp) &&
                            DateTimeOffset.TryParse(caProp.GetString(), out var dt))
                        {
                            startedAt ??= dt;
                            completedAt = dt;
                        }

                        if (root.TryGetProperty("event", out var evtProp))
                        {
                            var evt = evtProp.GetString();
                            if (evt == "init")
                            {
                                if (root.TryGetProperty("conversation_id", out var cid) && cid.GetString() is { } cIdStr)
                                    sessionId = cIdStr;

                                if (root.TryGetProperty("init", out var initEl) &&
                                    initEl.TryGetProperty("model", out var m))
                                    model ??= m.GetString();
                            }
                            else if (evt == "result" && root.TryGetProperty("result", out var res))
                            {
                                if (res.TryGetProperty("total_cost_usd", out var cp) ||
                                    res.TryGetProperty("cost_usd", out cp) ||
                                    res.TryGetProperty("total_cost", out cp))
                                {
                                    totalCost = cp.GetDecimal();
                                }

                                if (res.TryGetProperty("model", out var rm))
                                    model ??= rm.GetString();

                                if (res.TryGetProperty("usage", out var usageEl))
                                {
                                    inputTokens = usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : inputTokens;
                                    outputTokens = usageEl.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : outputTokens;
                                    cacheReadTokens = usageEl.TryGetProperty("cache_read_tokens", out var cr) ? cr.GetInt32() : cacheReadTokens;
                                    cacheWriteTokens = usageEl.TryGetProperty("cache_write_tokens", out var cw) ? cw.GetInt32() :
                                                       usageEl.TryGetProperty("cache_creation_tokens", out cw) ? cw.GetInt32() : cacheWriteTokens;

                                    if (totalCost == 0 &&
                                        (usageEl.TryGetProperty("total_cost_usd", out var ucp) ||
                                         usageEl.TryGetProperty("cost_usd", out ucp) ||
                                         usageEl.TryGetProperty("total_cost", out ucp)))
                                    {
                                        totalCost = ucp.GetDecimal();
                                    }
                                }
                            }
                        }
                        else if (root.TryGetProperty("type", out var typeProp))
                        {
                            var type = typeProp.GetString();
                            if (type == "USER_INPUT" || type == "PLANNER_RESPONSE")
                            {
                                if (root.TryGetProperty("created_at", out var cat) &&
                                    DateTimeOffset.TryParse(cat.GetString(), out var dt2))
                                {
                                    startedAt ??= dt2;
                                    completedAt = dt2;
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON line
                    }
                }
            }
        }
        catch (Exception)
        {
            // Graceful degradation
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
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var conversationsDir = Path.Combine(home, ".gemini", "antigravity-cli", "conversations");
        var brainDir = Path.Combine(home, ".gemini", "antigravity-cli", "brain");

        var results = new List<string>();

        if (Directory.Exists(conversationsDir))
        {
            results.AddRange(Directory.GetFiles(conversationsDir, "*.db"));
            results.AddRange(Directory.GetFiles(conversationsDir, "*.jsonl"));
        }

        if (Directory.Exists(brainDir))
        {
            results.AddRange(Directory.GetFiles(brainDir, "*.jsonl", SearchOption.AllDirectories));
        }

        if (projectPath is not null && Directory.Exists(projectPath))
        {
            results.AddRange(Directory.GetFiles(projectPath, "*.jsonl", SearchOption.AllDirectories));
            results.AddRange(Directory.GetFiles(projectPath, "*.db", SearchOption.AllDirectories));
        }

        return results
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Distinct()
            .ToList();
    }
}
