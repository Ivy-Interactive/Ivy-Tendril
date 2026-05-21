using System.Runtime.InteropServices;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Copilot;

public sealed class CopilotSessionCostParser : ISessionCostParser
{
    public string AgentId => Abstractions.AgentId.Copilot;

    public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        string? model = null;
        int outputTokens = 0;
        int premiumRequests = 0;
        long sessionDurationMs = 0;
        DateTimeOffset? startedAt = null;
        DateTimeOffset? completedAt = null;

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') continue;

            // Skip ephemeral lines fast
            if (line.Contains("\"ephemeral\":true", StringComparison.Ordinal) ||
                line.Contains("\"ephemeral\": true", StringComparison.Ordinal))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) continue;
                var type = typeProp.GetString();

                if (type == "session.tools_updated" && root.TryGetProperty("data", out var initData))
                {
                    model ??= initData.TryGetProperty("model", out var m) ? m.GetString() : null;
                    startedAt ??= root.TryGetProperty("timestamp", out var ts)
                        ? ParseTimestamp(ts.GetString())
                        : DateTimeOffset.UtcNow;
                }
                else if (type == "assistant.message" && root.TryGetProperty("data", out var msgData))
                {
                    if (msgData.TryGetProperty("outputTokens", out var ot))
                        outputTokens += ot.GetInt32();
                }
                else if (type == "result")
                {
                    if (root.TryGetProperty("usage", out var usage))
                    {
                        premiumRequests = usage.TryGetProperty("premiumRequests", out var pr) ? pr.GetInt32() : premiumRequests;
                        sessionDurationMs = usage.TryGetProperty("sessionDurationMs", out var dur) ? dur.GetInt64() : sessionDurationMs;
                    }

                    completedAt = root.TryGetProperty("timestamp", out var endTs)
                        ? ParseTimestamp(endTs.GetString())
                        : DateTimeOffset.UtcNow;
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        // Copilot bills by premium requests; token-based cost is not applicable
        return new SessionCostResult
        {
            SessionId = sessionId,
            AgentId = AgentId,
            Model = model,
            InputTokens = 0,
            OutputTokens = outputTokens,
            CacheReadTokens = 0,
            CacheWriteTokens = 0,
            TotalCostUsd = 0,
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

    private static DateTimeOffset? ParseTimestamp(string? ts)
    {
        if (ts is null) return null;
        return DateTimeOffset.TryParse(ts, out var parsed) ? parsed : null;
    }

    private static string GetDefaultSessionsPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "github-copilot", "sessions");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "github-copilot", "sessions");
    }
}
