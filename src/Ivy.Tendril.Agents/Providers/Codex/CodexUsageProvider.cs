using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Codex;

public sealed class CodexUsageProvider : IAgentUsageProvider
{
    private readonly string _sessionsPath;

    public string AgentId => Abstractions.AgentId.Codex;

    public CodexUsageProvider(string? sessionsPath = null)
    {
        _sessionsPath = sessionsPath ?? GetDefaultSessionsPath();
    }

    public Task<AgentUsageSnapshot?> GetUsageAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(_sessionsPath))
                return Task.FromResult<AgentUsageSnapshot?>(null);

            var files = Directory.EnumerateFiles(_sessionsPath, "*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5)
                .ToList();

            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) break;

                var snapshot = TryParseFile(file);
                if (snapshot is not null)
                    return Task.FromResult<AgentUsageSnapshot?>(snapshot);
            }

            return Task.FromResult<AgentUsageSnapshot?>(null);
        }
        catch
        {
            return Task.FromResult<AgentUsageSnapshot?>(null);
        }
    }

    private static AgentUsageSnapshot? TryParseFile(string filePath)
    {
        try
        {
            var lines = File.ReadLines(filePath).Reverse();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("payload", out var payload) ||
                    payload.ValueKind != JsonValueKind.Object)
                    continue;

                if (!payload.TryGetProperty("type", out var typeProp) ||
                    typeProp.GetString() != "token_count")
                    continue;

                if (!payload.TryGetProperty("rate_limits", out var rateLimits) ||
                    rateLimits.ValueKind != JsonValueKind.Object)
                    continue;

                DateTimeOffset? timestamp = null;
                if (root.TryGetProperty("timestamp", out var tsProp) &&
                    tsProp.TryGetDateTimeOffset(out var tsVal))
                {
                    timestamp = tsVal;
                }

                var windows = new List<AgentUsageWindow>();

                if (rateLimits.TryGetProperty("primary", out var primaryProp))
                {
                    var pw = ParseWindow(primaryProp, timestamp);
                    if (pw is not null) windows.Add(pw);
                }

                if (rateLimits.TryGetProperty("secondary", out var secondaryProp))
                {
                    var sw = ParseWindow(secondaryProp, timestamp);
                    if (sw is not null) windows.Add(sw);
                }

                if (windows.Count == 0) continue;

                if (timestamp.HasValue)
                {
                    var shortestWindowMinutes = windows.Min(w => w.WindowMinutes);
                    if (DateTimeOffset.UtcNow - timestamp.Value > TimeSpan.FromMinutes(shortestWindowMinutes))
                    {
                        return null;
                    }
                }

                return new AgentUsageSnapshot
                {
                    AgentId = Abstractions.AgentId.Codex,
                    Windows = windows,
                    CapturedAt = timestamp,
                    Note = timestamp.HasValue ? $"as of {timestamp.Value:u}" : null,
                };
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static AgentUsageWindow? ParseWindow(JsonElement element, DateTimeOffset? lineTimestamp)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (!element.TryGetProperty("window_minutes", out var wmProp) || !wmProp.TryGetInt32(out var windowMinutes))
            return null;

        double? usedPercent = null;
        if (element.TryGetProperty("used_percent", out var upProp) && upProp.TryGetDouble(out var upVal))
            usedPercent = upVal;

        DateTimeOffset? resetsAt = null;
        if (element.TryGetProperty("resets_at", out var raProp) && raProp.TryGetInt64(out var raVal))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(raVal);
        }
        else if (element.TryGetProperty("resets_in_seconds", out var risProp) &&
                 risProp.TryGetDouble(out var risVal) &&
                 lineTimestamp.HasValue)
        {
            resetsAt = lineTimestamp.Value.AddSeconds(risVal);
        }

        return new AgentUsageWindow
        {
            WindowMinutes = windowMinutes,
            UsedPercent = usedPercent,
            ResetsAt = resetsAt,
        };
    }

    private static string GetDefaultSessionsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex", "sessions");
    }
}
