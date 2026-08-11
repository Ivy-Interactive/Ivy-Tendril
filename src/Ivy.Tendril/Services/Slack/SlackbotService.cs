using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Tunnel;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Slack;

public sealed class SlackbotService : ISlackbotService, IStartable, IDisposable
{
    private const int MaxTrackedEvents = 500;

    private readonly IConfigService _config;
    private readonly IJobService _jobService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudflaredService _tunnelService;
    private readonly ILogger<SlackbotService> _logger;

    private readonly ConcurrentDictionary<string, byte> _processedEvents = new();
    private readonly ConcurrentQueue<string> _processedEventOrder = new();
    private readonly ConcurrentDictionary<string, SlackJobOrigin> _pendingJobs = new();

    private readonly SemaphoreSlim _restartLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _supervisorTask;
    private SlackApiClient? _api;
    private SlackbotConfig? _activeConfig;
    private string _botUserId = "";
    private SlackbotStatus _status = SlackbotStatus.Disabled;

    private record SlackJobOrigin(string Channel, string ThreadTs);

    public SlackbotService(
        IConfigService config,
        IJobService jobService,
        IHttpClientFactory httpClientFactory,
        ICloudflaredService tunnelService,
        ILogger<SlackbotService> logger)
    {
        _config = config;
        _jobService = jobService;
        _httpClientFactory = httpClientFactory;
        _tunnelService = tunnelService;
        _logger = logger;

        _jobService.JobsStructureChanged += OnJobsStructureChanged;
        _config.SettingsReloaded += OnSettingsReloaded;
    }

    public SlackbotStatus Status => _status;
    public string? BotName { get; private set; }
    public string? TeamName { get; private set; }
    public string? LastError { get; private set; }

    public event Action<SlackbotStatus>? StatusChanged;

    private void SetStatus(SlackbotStatus status)
    {
        if (_status == status) return;
        _status = status;
        StatusChanged?.Invoke(status);
    }

    private static bool IsMasterInstance =>
        Environment.GetEnvironmentVariable("TENDRIL_NOT_MASTER") != "1";

    public void Start()
    {
        if (!IsMasterInstance)
        {
            _logger.LogInformation("Skipping Slackbot (TENDRIL_NOT_MASTER=1)");
            return;
        }

        _restartLock.Wait();
        try
        {
            var slackConfig = _config.Settings.Slackbot;
            if (slackConfig is not { Enabled: true }
                || string.IsNullOrWhiteSpace(slackConfig.BotToken)
                || string.IsNullOrWhiteSpace(slackConfig.AppToken))
            {
                _logger.LogDebug("Slackbot is disabled or not configured, skipping");
                return;
            }

            StartSupervisor(slackConfig);
        }
        finally
        {
            _restartLock.Release();
        }
    }

    public async Task<SlackAuthInfo> TestConnectionAsync(string botToken, string appToken,
        CancellationToken ct = default)
    {
        var api = new SlackApiClient(_httpClientFactory, botToken, appToken);
        var auth = await api.AuthTestAsync(ct);
        await api.OpenSocketUrlAsync(ct);
        return auth;
    }

    public async Task RestartAsync()
    {
        if (!IsMasterInstance)
            return;

        await _restartLock.WaitAsync();
        try
        {
            await StopCoreAsync();

            var slackConfig = _config.Settings.Slackbot;
            if (slackConfig is { Enabled: true }
                && !string.IsNullOrWhiteSpace(slackConfig.BotToken)
                && !string.IsNullOrWhiteSpace(slackConfig.AppToken))
            {
                StartSupervisor(slackConfig);
            }
            else
            {
                LastError = null;
                SetStatus(SlackbotStatus.Disabled);
            }
        }
        finally
        {
            _restartLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _restartLock.WaitAsync();
        try
        {
            await StopCoreAsync();
            LastError = null;
            SetStatus(SlackbotStatus.Disabled);
        }
        finally
        {
            _restartLock.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        _cts?.Cancel();

        var supervisorTask = _supervisorTask;
        if (supervisorTask is not null)
        {
            try { await supervisorTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
        }

        _cts?.Dispose();
        _cts = null;
        _supervisorTask = null;
        _activeConfig = null;
    }

    private void StartSupervisor(SlackbotConfig slackConfig)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _activeConfig = slackConfig with { };
        LastError = null;
        SetStatus(SlackbotStatus.Connecting);
        var token = _cts.Token;
        _supervisorTask = Task.Run(() => SupervisorLoopAsync(slackConfig, token));
    }

    private void OnSettingsReloaded(object? sender, EventArgs e)
    {
        var current = _config.Settings.Slackbot;
        var active = _activeConfig;

        var shouldRun = current is { Enabled: true }
                        && !string.IsNullOrWhiteSpace(current.BotToken)
                        && !string.IsNullOrWhiteSpace(current.AppToken);
        var isRunning = active is not null;

        if (shouldRun == isRunning && (active is null || active == current))
            return;

        _logger.LogInformation("Slackbot configuration changed, restarting");
        _ = Task.Run(RestartAsync);
    }

    private async Task SupervisorLoopAsync(SlackbotConfig slackConfig, CancellationToken ct)
    {
        var consecutiveFailures = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetStatus(SlackbotStatus.Connecting);
                _api = new SlackApiClient(_httpClientFactory, slackConfig.BotToken, slackConfig.AppToken);

                var auth = await _api.AuthTestAsync(ct);
                _botUserId = auth.UserId;
                BotName = auth.BotName;
                TeamName = auth.Team;

                var socketUrl = await _api.OpenSocketUrlAsync(ct);

                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(socketUrl), ct);
                _logger.LogInformation("Slackbot connected as @{BotName} in {Team}", BotName, TeamName);
                consecutiveFailures = 0;

                await ReceiveLoopAsync(socket, slackConfig, ct);

                if (ct.IsCancellationRequested) break;
                _logger.LogInformation("Slack socket closed, reconnecting");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (SlackApiException ex) when (ex.IsUnrecoverable)
            {
                LastError = ex.Message;
                _logger.LogError(ex, "Slackbot stopped due to an unrecoverable Slack error");
                SetStatus(SlackbotStatus.Error);
                return;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                LastError = ex.Message;
                _logger.LogWarning(ex, "Slackbot connection failed (attempt {Count})", consecutiveFailures);
                SetStatus(SlackbotStatus.Connecting);
            }

            if (ct.IsCancellationRequested) break;

            var delay = TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, Math.Max(consecutiveFailures - 1, 0)), 60));
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }

        SetStatus(SlackbotStatus.Disabled);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, SlackbotConfig slackConfig, CancellationToken ct)
    {
        var buffer = new byte[16384];

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var text = Encoding.UTF8.GetString(message.ToArray());
            JsonNode? envelope;
            try
            {
                envelope = JsonNode.Parse(text);
            }
            catch
            {
                _logger.LogWarning("Slackbot received unparseable frame");
                continue;
            }

            if (envelope is null)
                continue;

            var envelopeId = envelope["envelope_id"]?.GetValue<string>();
            if (envelopeId is not null)
            {
                var ack = Encoding.UTF8.GetBytes($"{{\"envelope_id\":\"{envelopeId}\"}}");
                await socket.SendAsync(new ArraySegment<byte>(ack), WebSocketMessageType.Text, true, ct);
            }

            switch (envelope["type"]?.GetValue<string>())
            {
                case "hello":
                    SetStatus(SlackbotStatus.Connected);
                    break;
                case "disconnect":
                    _logger.LogInformation("Slack requested disconnect: {Reason}",
                        envelope["reason"]?.GetValue<string>());
                    return;
                case "events_api":
                    DispatchEvent(envelope, slackConfig, ct);
                    break;
            }
        }
    }

    private void DispatchEvent(JsonNode envelope, SlackbotConfig slackConfig, CancellationToken ct)
    {
        var payload = envelope["payload"];
        var slackEvent = payload?["event"];
        if (slackEvent?["type"]?.GetValue<string>() != "app_mention")
            return;

        var eventId = payload?["event_id"]?.GetValue<string>() ?? slackEvent["ts"]?.GetValue<string>() ?? "";
        if (!TryMarkEventProcessed(eventId))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await HandleMentionAsync(slackEvent, slackConfig, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Slackbot failed to handle mention");
            }
        }, ct);
    }

    private bool TryMarkEventProcessed(string eventId)
    {
        if (string.IsNullOrEmpty(eventId) || !_processedEvents.TryAdd(eventId, 0))
            return false;

        _processedEventOrder.Enqueue(eventId);
        while (_processedEventOrder.Count > MaxTrackedEvents && _processedEventOrder.TryDequeue(out var oldest))
            _processedEvents.TryRemove(oldest, out _);

        return true;
    }

    private async Task HandleMentionAsync(JsonNode slackEvent, SlackbotConfig slackConfig, CancellationToken ct)
    {
        var api = _api;
        if (api is null)
            return;

        var channel = slackEvent["channel"]?.GetValue<string>();
        var ts = slackEvent["ts"]?.GetValue<string>();
        var threadTs = slackEvent["thread_ts"]?.GetValue<string>();
        var user = slackEvent["user"]?.GetValue<string>();
        var text = slackEvent["text"]?.GetValue<string>() ?? "";

        if (channel is null || ts is null || user is null || user == _botUserId)
            return;

        var replyTs = threadTs ?? ts;
        var instruction = SlackTextFormatter.StripMention(text, _botUserId);

        if (string.IsNullOrWhiteSpace(instruction))
        {
            await api.PostMessageAsync(channel,
                "Tag me with a task and I will create a Tendril plan for it. " +
                "For example: `@" + (BotName ?? SlackAppManifest.AppName) + " fix the flaky login test`. " +
                "I include the recent conversation in this channel or thread as context.",
                replyTs, ct);
            return;
        }

        try { await api.AddReactionAsync(channel, ts, "eyes", ct); }
        catch (SlackApiException ex) { _logger.LogDebug("Could not add reaction: {Error}", ex.Error); }

        var context = await GatherContextAsync(api, channel, ts, threadTs, slackConfig.HistoryLimit, ct);
        var userName = await ResolveUserNameAsync(api, user, ct);
        var channelName = await ResolveChannelNameAsync(api, channel, ct);

        instruction = await NormalizeTextAsync(api, instruction, ct);

        var description = SlackTextFormatter.BuildPlanDescription(instruction, userName, channelName, context);
        var project = string.IsNullOrWhiteSpace(slackConfig.Project) ? "Auto" : slackConfig.Project;

        string jobId;
        try
        {
            jobId = _jobService.StartJob(new CreatePlanArgs(description, project));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slackbot failed to start CreatePlan job");
            await api.PostMessageAsync(channel,
                $":x: I could not create a plan: {ex.Message}", replyTs, ct);
            return;
        }

        _pendingJobs[jobId] = new SlackJobOrigin(channel, replyTs);

        var reply = ":seedling: On it! I am creating a Tendril plan from this conversation.";
        if (_tunnelService.IsConnected && _tunnelService.TunnelUrl is { } url)
            reply += $" Follow along at {url}";

        await api.PostMessageAsync(channel, reply, replyTs, ct);
    }

    private async Task<List<(string Author, string Text)>> GatherContextAsync(
        SlackApiClient api, string channel, string mentionTs, string? threadTs, int limit, CancellationToken ct)
    {
        var context = new List<(string Author, string Text)>();
        if (limit <= 0)
            return context;

        List<SlackMessage> messages;
        try
        {
            messages = threadTs is not null
                ? await api.GetThreadRepliesAsync(channel, threadTs, limit + 1, ct)
                : await api.GetChannelHistoryAsync(channel, limit + 1, ct);
        }
        catch (SlackApiException ex)
        {
            _logger.LogWarning("Slackbot could not read conversation history: {Error}", ex.Error);
            return context;
        }

        foreach (var message in messages)
        {
            if (message.Ts == mentionTs || string.IsNullOrWhiteSpace(message.Text))
                continue;

            var author = message.User is not null
                ? await ResolveUserNameAsync(api, message.User, ct)
                : BotName ?? "bot";

            var text = await NormalizeTextAsync(api, message.Text, ct);

            context.Add((author, text));
        }

        if (context.Count > limit)
            context.RemoveRange(0, context.Count - limit);

        return context;
    }

    private async Task<string> NormalizeTextAsync(SlackApiClient api, string text, CancellationToken ct)
    {
        var names = new Dictionary<string, string>();
        foreach (var id in SlackTextFormatter.ExtractUserMentions(text))
            names[id] = await ResolveUserNameAsync(api, id, ct);

        return SlackTextFormatter.Normalize(text, id => names.TryGetValue(id, out var name) ? name : id);
    }

    private async Task<string> ResolveUserNameAsync(SlackApiClient api, string userId, CancellationToken ct)
    {
        try
        {
            return await api.GetUserNameAsync(userId, ct);
        }
        catch (SlackApiException)
        {
            return userId;
        }
    }

    private async Task<string> ResolveChannelNameAsync(SlackApiClient api, string channel, CancellationToken ct)
    {
        try
        {
            return await api.GetChannelNameAsync(channel, ct);
        }
        catch (SlackApiException)
        {
            return channel;
        }
    }

    private void OnJobsStructureChanged()
    {
        if (_pendingJobs.IsEmpty)
            return;

        foreach (var (jobId, origin) in _pendingJobs)
        {
            var job = _jobService.GetJob(jobId);
            if (job is null)
            {
                _pendingJobs.TryRemove(jobId, out _);
                continue;
            }

            if (job.Status is JobStatus.Pending or JobStatus.Queued or JobStatus.Running or JobStatus.Blocked)
                continue;

            if (!_pendingJobs.TryRemove(jobId, out _))
                continue;

            var message = BuildCompletionMessage(job);
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_api is { } api)
                        await api.PostMessageAsync(origin.Channel, message, origin.ThreadTs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Slackbot failed to post completion message");
                }
            });
        }
    }

    private string BuildCompletionMessage(JobItem job)
    {
        if (job.Status == JobStatus.Completed)
        {
            var planId = job.ResolvePlanId();
            var title = job.ReportedPlanTitle;
            var label = !string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(planId)
                ? $"*{title}* (plan {planId})"
                : !string.IsNullOrEmpty(planId) ? $"plan {planId}" : "the plan";

            var message = $":white_check_mark: Done! I created {label} in Tendril.";
            if (_tunnelService.IsConnected && _tunnelService.TunnelUrl is { } url)
                message += $" Open it at {url}";
            return message;
        }

        return $":x: Plan creation did not finish ({job.Status}). " +
               (string.IsNullOrEmpty(job.ReportedFailureReason) ? "" : job.ReportedFailureReason);
    }

    public void Dispose()
    {
        _jobService.JobsStructureChanged -= OnJobsStructureChanged;
        _config.SettingsReloaded -= OnSettingsReloaded;

        _cts?.Cancel();
        try { _supervisorTask?.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
        _cts?.Dispose();
    }
}
