using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Ivy.Tendril.Services.Slack;

public record SlackAuthInfo(string UserId, string BotName, string Team);

public record SlackMessage(string? User, string? BotId, string Text, string Ts);

public class SlackApiException : Exception
{
    private static readonly string[] UnrecoverableErrors =
    [
        "invalid_auth", "not_authed", "account_inactive", "token_revoked", "token_expired", "missing_scope"
    ];

    public SlackApiException(string method, string error)
        : base($"Slack API '{method}' failed: {error}")
    {
        Error = error;
    }

    public string Error { get; }

    public bool IsUnrecoverable => UnrecoverableErrors.Contains(Error);
}

public class SlackApiClient(IHttpClientFactory httpClientFactory, string botToken, string appToken)
{
    private const string BaseUrl = "https://slack.com/api/";
    private readonly ConcurrentDictionary<string, string> _userNames = new();
    private readonly ConcurrentDictionary<string, string> _channelNames = new();

    private async Task<JsonNode> CallAsync(string method, string token,
        Dictionary<string, string>? args = null, CancellationToken ct = default)
    {
        using var http = httpClientFactory.CreateClient("Slack");
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + method);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new FormUrlEncodedContent(args ?? new Dictionary<string, string>());

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonNode.Parse(body)
                   ?? throw new SlackApiException(method, "empty_response");

        if (json["ok"]?.GetValue<bool>() != true)
            throw new SlackApiException(method, json["error"]?.GetValue<string>() ?? "unknown_error");

        return json;
    }

    public async Task<SlackAuthInfo> AuthTestAsync(CancellationToken ct = default)
    {
        var json = await CallAsync("auth.test", botToken, ct: ct);
        return new SlackAuthInfo(
            json["user_id"]?.GetValue<string>() ?? "",
            json["user"]?.GetValue<string>() ?? "",
            json["team"]?.GetValue<string>() ?? "");
    }

    public async Task<string> OpenSocketUrlAsync(CancellationToken ct = default)
    {
        var json = await CallAsync("apps.connections.open", appToken, ct: ct);
        return json["url"]?.GetValue<string>()
               ?? throw new SlackApiException("apps.connections.open", "missing_url");
    }

    public async Task PostMessageAsync(string channel, string text, string? threadTs = null,
        CancellationToken ct = default)
    {
        var args = new Dictionary<string, string>
        {
            ["channel"] = channel,
            ["text"] = text,
            ["unfurl_links"] = "false"
        };
        if (threadTs != null)
            args["thread_ts"] = threadTs;

        await CallAsync("chat.postMessage", botToken, args, ct);
    }

    public async Task AddReactionAsync(string channel, string timestamp, string name,
        CancellationToken ct = default)
    {
        await CallAsync("reactions.add", botToken, new Dictionary<string, string>
        {
            ["channel"] = channel,
            ["timestamp"] = timestamp,
            ["name"] = name
        }, ct);
    }

    public async Task<List<SlackMessage>> GetThreadRepliesAsync(string channel, string threadTs, int limit,
        CancellationToken ct = default)
    {
        var json = await CallAsync("conversations.replies", botToken, new Dictionary<string, string>
        {
            ["channel"] = channel,
            ["ts"] = threadTs,
            ["limit"] = limit.ToString()
        }, ct);

        return ParseMessages(json);
    }

    public async Task<List<SlackMessage>> GetChannelHistoryAsync(string channel, int limit,
        CancellationToken ct = default)
    {
        var json = await CallAsync("conversations.history", botToken, new Dictionary<string, string>
        {
            ["channel"] = channel,
            ["limit"] = limit.ToString()
        }, ct);

        var messages = ParseMessages(json);
        messages.Reverse();
        return messages;
    }

    public async Task<string> GetChannelNameAsync(string channel, CancellationToken ct = default)
    {
        if (_channelNames.TryGetValue(channel, out var cached))
            return cached;

        var json = await CallAsync("conversations.info", botToken, new Dictionary<string, string>
        {
            ["channel"] = channel
        }, ct);

        var name = json["channel"]?["name"]?.GetValue<string>() ?? channel;
        _channelNames[channel] = name;
        return name;
    }

    public async Task<string> GetUserNameAsync(string userId, CancellationToken ct = default)
    {
        if (_userNames.TryGetValue(userId, out var cached))
            return cached;

        var json = await CallAsync("users.info", botToken, new Dictionary<string, string>
        {
            ["user"] = userId
        }, ct);

        var profile = json["user"]?["profile"];
        var displayName = profile?["display_name"]?.GetValue<string>();
        var realName = profile?["real_name"]?.GetValue<string>();
        var name = !string.IsNullOrEmpty(displayName) ? displayName
            : !string.IsNullOrEmpty(realName) ? realName
            : json["user"]?["name"]?.GetValue<string>() ?? userId;

        _userNames[userId] = name;
        return name;
    }

    private static List<SlackMessage> ParseMessages(JsonNode json)
    {
        var result = new List<SlackMessage>();
        if (json["messages"] is not JsonArray messages)
            return result;

        foreach (var message in messages)
        {
            if (message is null)
                continue;

            var subtype = message["subtype"]?.GetValue<string>();
            if (subtype is "channel_join" or "channel_leave")
                continue;

            result.Add(new SlackMessage(
                message["user"]?.GetValue<string>(),
                message["bot_id"]?.GetValue<string>(),
                message["text"]?.GetValue<string>() ?? "",
                message["ts"]?.GetValue<string>() ?? ""));
        }

        return result;
    }
}
