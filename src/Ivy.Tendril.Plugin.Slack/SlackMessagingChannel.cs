using System.Text.Json;
using Ivy.Tendril.Plugins;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Plugins.Slack;

public class SlackMessagingChannel(SlackChannelSettings settings, ILogger? logger = null) : ITendrilMessagingChannel
{
    private readonly ILogger _logger = logger ?? CreateDefaultLogger();
    private ITendrilApi? _api;
    private SlackWebApiClient? _botClient;
    private string _botUserId = "";

    public string Id => "slack";

    private static ILogger CreateDefaultLogger() =>
        LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SlackMessagingChannel>();

    public async Task StartAsync(ITendrilApi api, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.BotToken) || string.IsNullOrEmpty(settings.AppToken))
        {
            _logger.LogWarning("Slack channel not started: BotToken or AppToken missing");
            return;
        }

        _api = api;
        _botClient = new SlackWebApiClient(settings.BotToken);
        using var appClient = new SlackWebApiClient(settings.AppToken);

        var auth = await CallAuthTestAsync(cancellationToken);
        if (auth == null) return;

        _logger.LogInformation("Slack channel connected to workspace {Team} as {BotUser}", auth.TeamName, auth.BotUser);

        var socketClient = new SlackSocketModeClient(appClient, HandleEnvelopeAsync, _logger);
        await socketClient.RunAsync(cancellationToken);
    }

    private async Task<SlackAuthInfo?> CallAuthTestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _botClient!.CallAsync("auth.test", null, cancellationToken);
            _botUserId = result.TryGetProperty("user_id", out var userId) ? userId.GetString() ?? "" : "";
            return new SlackAuthInfo(
                result.TryGetProperty("team", out var team) ? team.GetString() ?? "" : "",
                result.TryGetProperty("user", out var user) ? user.GetString() ?? "" : "",
                result.TryGetProperty("team_id", out var teamId) ? teamId.GetString() ?? "" : "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slack auth.test failed; channel not started");
            return null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _botClient?.Dispose();
        _botClient = null;
        return Task.CompletedTask;
    }

    public async Task SendNotificationAsync(TendrilNotification notification, CancellationToken cancellationToken)
    {
        if (_botClient == null || string.IsNullOrEmpty(settings.DefaultChannel))
            return;

        var emoji = notification.IsSuccess ? ":white_check_mark:" : ":x:";
        await _botClient.PostMessageAsync(
            settings.DefaultChannel,
            $"{emoji} *{notification.Title}*\n{notification.Message}",
            cancellationToken: cancellationToken);
    }

    private async Task<object?> HandleEnvelopeAsync(SlackEnvelope envelope)
    {
        if (_api == null || _botClient == null)
            return null;

        switch (envelope.Type)
        {
            case "slash_commands":
                return HandleSlashCommand(envelope.Payload);
            case "events_api":
                await HandleEventAsync(envelope.Payload);
                return null;
            default:
                return null;
        }
    }

    private object? HandleSlashCommand(JsonElement payload)
    {
        var userId = GetString(payload, "user_id");
        var text = GetString(payload, "text");

        if (!settings.IsUserAllowed(userId))
            return new { response_type = "ephemeral", text = ":no_entry: You are not authorized to control Tendril." };

        var reply = SlackCommandHandler.Execute(text, _api!);
        return new { response_type = "in_channel", text = reply };
    }

    private async Task HandleEventAsync(JsonElement payload)
    {
        if (!payload.TryGetProperty("event", out var slackEvent))
            return;

        var eventType = GetString(slackEvent, "type");
        if (eventType is not ("app_mention" or "message"))
            return;

        var userId = GetString(slackEvent, "user");
        if (eventType == "message")
        {
            if (GetString(slackEvent, "channel_type") != "im") return;
            if (slackEvent.TryGetProperty("bot_id", out _)) return;
            if (userId == _botUserId || userId.Length == 0) return;
        }

        var channel = GetString(slackEvent, "channel");
        var threadTs = GetString(slackEvent, "thread_ts");
        if (threadTs.Length == 0 && eventType == "app_mention")
            threadTs = GetString(slackEvent, "ts");

        var text = GetString(slackEvent, "text");
        if (_botUserId.Length > 0)
            text = text.Replace($"<@{_botUserId}>", "", StringComparison.OrdinalIgnoreCase).Trim();

        var reply = settings.IsUserAllowed(userId)
            ? SlackCommandHandler.Execute(text, _api!)
            : ":no_entry: You are not authorized to control Tendril.";

        await _botClient!.PostMessageAsync(channel, reply, threadTs.Length > 0 ? threadTs : null);
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
