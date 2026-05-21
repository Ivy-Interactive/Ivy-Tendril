using System.Runtime.InteropServices;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeSessionCostParser : ISessionCostParser
{
    public string AgentId => Abstractions.AgentId.OpenCode;

    public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        string? model = null;
        int inputTokens = 0;
        int outputTokens = 0;
        decimal totalCost = 0;
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

                if (type == "step_start")
                {
                    startedAt ??= DateTimeOffset.UtcNow;
                }
                else if (type == "step_finish" && root.TryGetProperty("part", out var part))
                {
                    if (part.TryGetProperty("cost", out var costProp))
                        totalCost += costProp.GetDecimal();

                    if (part.TryGetProperty("tokens", out var tokens))
                    {
                        inputTokens += tokens.TryGetProperty("input", out var it) ? it.GetInt32() : 0;
                        outputTokens += tokens.TryGetProperty("output", out var ot) ? ot.GetInt32() : 0;
                    }

                    completedAt = DateTimeOffset.UtcNow;
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        if (totalCost == 0 && model is not null)
        {
            totalCost = pricing.CalculateCost(model, inputTokens, outputTokens);
        }

        return new SessionCostResult
        {
            SessionId = sessionId,
            AgentId = AgentId,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = 0,
            CacheWriteTokens = 0,
            TotalCostUsd = totalCost,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };
    }

    public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
    {
        // OpenCode stores sessions in SQLite, not JSONL files.
        // Only return exported JSONL files if they exist at the data path.
        var basePath = projectPath ?? GetDefaultDataPath();
        if (!Directory.Exists(basePath)) return [];

        return Directory.GetFiles(basePath, "*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    private static string GetDefaultDataPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "opencode");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "opencode");
    }
}
