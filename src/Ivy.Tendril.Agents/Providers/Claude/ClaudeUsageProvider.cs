using System.Net.Http.Headers;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Claude;

/// <summary>
/// Reads the subscription rate-limit windows Claude Code shows in <c>/usage</c> from the OAuth usage
/// endpoint, authenticating with the token Claude Code keeps in <c>.credentials.json</c>. An expired token
/// is not refreshed: refreshing rotates the refresh token and would sign Claude Code itself out.
/// </summary>
public sealed class ClaudeUsageProvider : IAgentUsageProvider
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string OAuthBetaHeader = "oauth-2025-04-20";
    private const int FiveHourMinutes = 300;
    private const int WeeklyMinutes = 10080;

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    // The overall weekly bucket plus the model-scoped ones; the most used one is reported.
    private static readonly (string Property, string? Label)[] WeeklyBuckets =
    [
        ("seven_day", null),
        ("seven_day_opus", "Opus"),
        ("seven_day_sonnet", "Sonnet"),
    ];

    private readonly HttpClient _httpClient;
    private readonly string _credentialsPath;
    private readonly TimeProvider _timeProvider;

    public string AgentId => Abstractions.AgentId.Claude;

    public ClaudeUsageProvider(
        HttpClient? httpClient = null,
        string? credentialsPath = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _credentialsPath = credentialsPath ?? GetDefaultCredentialsPath();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AgentUsageSnapshot?> GetUsageAsync(CancellationToken ct = default)
    {
        var callTime = _timeProvider.GetUtcNow();

        try
        {
            var accessToken = ReadAccessToken(callTime);
            if (accessToken is null)
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("anthropic-beta", OAuthBetaHeader);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseUsage(body, callTime);
        }
        catch
        {
            return null;
        }
    }

    private string? ReadAccessToken(DateTimeOffset now)
    {
        if (!File.Exists(_credentialsPath))
            return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(_credentialsPath));
        if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
            oauth.ValueKind != JsonValueKind.Object)
            return null;

        if (oauth.TryGetProperty("expiresAt", out var expiresProp) &&
            expiresProp.TryGetInt64(out var expiresAtMs) &&
            DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs) <= now)
            return null;

        return oauth.TryGetProperty("accessToken", out var tokenProp) && tokenProp.ValueKind == JsonValueKind.String
            ? tokenProp.GetString()
            : null;
    }

    private static AgentUsageSnapshot? ParseUsage(string json, DateTimeOffset capturedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var windows = new List<AgentUsageWindow>();

        if (ParseWindow(root, "five_hour", FiveHourMinutes) is { } fiveHour)
            windows.Add(fiveHour);

        AgentUsageWindow? weekly = null;
        string? weeklyLabel = null;
        foreach (var (property, label) in WeeklyBuckets)
        {
            if (ParseWindow(root, property, WeeklyMinutes) is not { } candidate)
                continue;

            if (weekly is null || candidate.UsedPercent > weekly.UsedPercent)
            {
                weekly = candidate;
                weeklyLabel = label;
            }
        }

        if (weekly is not null)
            windows.Add(weekly);

        if (windows.Count == 0)
            return null;

        return new AgentUsageSnapshot
        {
            AgentId = Abstractions.AgentId.Claude,
            Windows = windows,
            CapturedAt = capturedAt,
            Note = weeklyLabel is null ? null : $"from {weeklyLabel} weekly limit",
        };
    }

    private static AgentUsageWindow? ParseWindow(JsonElement root, string property, int windowMinutes)
    {
        if (!root.TryGetProperty(property, out var bucket) || bucket.ValueKind != JsonValueKind.Object)
            return null;

        if (!bucket.TryGetProperty("utilization", out var utilProp) ||
            utilProp.ValueKind != JsonValueKind.Number ||
            !utilProp.TryGetDouble(out var utilization))
            return null;

        DateTimeOffset? resetsAt = null;
        if (bucket.TryGetProperty("resets_at", out var resetsProp) &&
            resetsProp.ValueKind == JsonValueKind.String &&
            resetsProp.TryGetDateTimeOffset(out var resetsVal))
        {
            resetsAt = resetsVal;
        }

        return new AgentUsageWindow
        {
            WindowMinutes = windowMinutes,
            UsedPercent = utilization,
            ResetsAt = resetsAt,
        };
    }

    private static string GetDefaultCredentialsPath()
    {
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(configDir))
            configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(configDir, ".credentials.json");
    }
}
