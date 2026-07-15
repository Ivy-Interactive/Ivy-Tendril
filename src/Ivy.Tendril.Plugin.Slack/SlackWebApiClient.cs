using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ivy.Tendril.Plugins.Slack;

public record SlackAuthInfo(string TeamName, string BotUser, string TeamId);
public record SlackChannelInfo(string Id, string Name);

public class SlackApiException(string method, string error) : Exception($"Slack API {method} failed: {error}")
{
    public string SlackError { get; } = error;
}

public class SlackWebApiClient(string token, HttpMessageHandler? handler = null) : IDisposable
{
    private readonly HttpClient _http = new(handler ?? new HttpClientHandler(), disposeHandler: handler == null)
    {
        BaseAddress = new Uri("https://slack.com/api/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<SlackAuthInfo> AuthTestAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("auth.test", null, cancellationToken);
        return new SlackAuthInfo(
            result.TryGetProperty("team", out var team) ? team.GetString() ?? "" : "",
            result.TryGetProperty("user", out var user) ? user.GetString() ?? "" : "",
            result.TryGetProperty("team_id", out var teamId) ? teamId.GetString() ?? "" : "");
    }

    public async Task<string> OpenSocketUrlAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("apps.connections.open", null, cancellationToken);
        return result.GetProperty("url").GetString()
               ?? throw new SlackApiException("apps.connections.open", "missing url");
    }

    public async Task PostMessageAsync(string channel, string text, string? threadTs = null, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["channel"] = channel,
            ["text"] = text,
            ["unfurl_links"] = false
        };
        if (threadTs != null)
            payload["thread_ts"] = threadTs;
        await CallAsync("chat.postMessage", payload, cancellationToken);
    }

    public async Task<List<SlackChannelInfo>> ListChannelsAsync(CancellationToken cancellationToken = default)
    {
        var channels = new List<SlackChannelInfo>();
        string? cursor = null;
        do
        {
            var query = "conversations.list?types=public_channel&exclude_archived=true&limit=200"
                        + (cursor != null ? $"&cursor={Uri.EscapeDataString(cursor)}" : "");
            var result = await CallAsync(query, null, cancellationToken);
            foreach (var channel in result.GetProperty("channels").EnumerateArray())
                channels.Add(new SlackChannelInfo(
                    channel.GetProperty("id").GetString() ?? "",
                    channel.GetProperty("name").GetString() ?? ""));
            cursor = result.TryGetProperty("response_metadata", out var meta) &&
                     meta.TryGetProperty("next_cursor", out var next)
                ? next.GetString()
                : null;
        } while (!string.IsNullOrEmpty(cursor) && channels.Count < 1000);

        return channels.OrderBy(c => c.Name).ToList();
    }

    public async Task JoinChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        try
        {
            await CallAsync("conversations.join", new Dictionary<string, object?> { ["channel"] = channelId }, cancellationToken);
        }
        catch (SlackApiException ex) when (ex.SlackError is "already_in_channel" or "method_not_supported_for_channel_type")
        {
        }
    }

    internal async Task<JsonElement> CallAsync(string method, Dictionary<string, object?>? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, method);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload != null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = JsonDocument.Parse(body).RootElement.Clone();

        if (!json.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var error = json.TryGetProperty("error", out var err) ? err.GetString() ?? "unknown" : "unknown";
            throw new SlackApiException(method, error);
        }

        return json;
    }

    public void Dispose() => _http.Dispose();
}
