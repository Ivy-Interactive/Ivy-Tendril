using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public sealed class ChatExecutionService : IChatExecutionService
{
    private readonly IConfigService _configService;
    private readonly IChatHistoryService _chatService;
    private readonly IAgentRunner _agentRunner;
    private readonly IChatSessionNamingService _namingService;
    private readonly IEventSerializer _serializer;
    private readonly ILogger<ChatExecutionService> _logger;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IJobService? _jobService;
    private readonly IPlanReaderService? _planReaderService;
    private readonly IPlanDatabaseService? _database;

    private readonly ConcurrentDictionary<string, byte> _notifiedJobCompletions = new(StringComparer.OrdinalIgnoreCase);
    private bool _jobServiceSubscribed;
    private readonly object _jobSubLock = new();

    private IPlanReaderService? ResolvedPlanReaderService =>
        _planReaderService ?? _serviceProvider?.GetService<IPlanReaderService>();

    private IPlanDatabaseService? ResolvedDatabase =>
        _database ?? _serviceProvider?.GetService<IPlanDatabaseService>();

    private IJobService? ResolvedJobService
    {
        get
        {
            var js = _jobService ?? _serviceProvider?.GetService<IJobService>();
            if (js != null && !_jobServiceSubscribed)
            {
                lock (_jobSubLock)
                {
                    if (!_jobServiceSubscribed)
                    {
                        js.JobFinished += OnJobFinished;
                        _jobServiceSubscribed = true;
                    }
                }
            }
            return js;
        }
    }

    private readonly ConcurrentDictionary<string, ActiveChatExecution> _activeExecutions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _pendingSystemEvents = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex JobStartedRegex = new(
        @"\bJob started:\s*([0-9a-zA-Z_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal void TryTrackSpawnedJob(string sessionId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var match = JobStartedRegex.Match(text);
        if (match.Success)
        {
            var jobId = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(jobId))
            {
                _chatService.AddSpawnedJob(sessionId, jobId);
                ResolvedJobService?.SetChatSessionId(jobId, sessionId);
            }
        }
    }

    public event Action<string>? SessionGeneratingChanged;
    public event Action<string>? StreamUpdated;
    public event Action<string, string>? StreamLineEmitted;

    internal void EmitStreamLine(string sessionId, string wireJson)
    {
        if (_activeExecutions.TryGetValue(sessionId, out var exec))
        {
            lock (exec.Lock)
            {
                exec.RawLines.Add(wireJson);
                if (wireJson.Contains("\"kind\":\"text\"") || wireJson.Contains("\"text\":"))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(wireJson);
                        if (doc.RootElement.TryGetProperty("text", out var textProp))
                        {
                            var text = textProp.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                exec.LastText = text;
                            }
                        }
                    }
                    catch { }
                }
            }

            if (Stopwatch.GetElapsedTime(exec.LastPersistTicks).TotalSeconds >= 1.0)
            {
                exec.LastPersistTicks = Stopwatch.GetTimestamp();
                string? currentText;
                string? currentRaw;
                lock (exec.Lock)
                {
                    currentText = exec.LastText;
                    currentRaw = exec.RawLines.Count > 0 ? string.Join("\n", exec.RawLines) : null;
                }
                if (!string.IsNullOrEmpty(exec.AssistantMessageId))
                {
                    _chatService.UpdateMessage(sessionId, exec.AssistantMessageId, currentText ?? string.Empty, currentRaw, flushImmediately: false);
                }
            }
        }
        StreamLineEmitted?.Invoke(sessionId, wireJson);
        StreamUpdated?.Invoke(sessionId);
    }

    public ChatExecutionService(
        IConfigService configService,
        IChatHistoryService chatService,
        IAgentRunner agentRunner,
        IChatSessionNamingService namingService,
        IEventSerializer serializer,
        ILogger<ChatExecutionService>? logger = null,
        IServiceProvider? serviceProvider = null,
        IJobService? jobService = null,
        IPlanReaderService? planReaderService = null,
        IPlanDatabaseService? database = null)
    {
        _configService = configService;
        _chatService = chatService;
        _agentRunner = agentRunner;
        _namingService = namingService;
        _serializer = serializer;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatExecutionService>.Instance;
        _serviceProvider = serviceProvider;
        _jobService = jobService;
        _planReaderService = planReaderService;
        _database = database;
        _chatService.ClearAllGeneratingSessions();
        if (_jobService != null)
        {
            _jobService.JobFinished += OnJobFinished;
            _jobServiceSubscribed = true;
        }
    }

    public bool IsGenerating(string sessionId) =>
        !string.IsNullOrEmpty(sessionId) && _activeExecutions.ContainsKey(sessionId);

    public string GetStreamSnapshot(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return string.Empty;
        if (_activeExecutions.TryGetValue(sessionId, out var exec))
        {
            lock (exec.Lock)
            {
                return string.Join("\n", exec.RawLines);
            }
        }
        return string.Empty;
    }

    public IObservable<string> GetLiveStreamObservable(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return Observable.Empty<string>();

        return Observable.Create<string>(observer =>
        {
            Action<string, string> handler = (sessId, line) =>
            {
                if (string.Equals(sessId, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    observer.OnNext(line);
                }
            };

            StreamLineEmitted += handler;
            return Disposable.Create(() => StreamLineEmitted -= handler);
        });
    }

    public async Task SendMessageAsync(
        string sessionId,
        string prompt,
        IReadOnlyList<ChatAttachmentDto>? attachments = null,
        string? agentId = null,
        string? modelId = null,
        string? effort = null,
        string role = "user",
        CancellationToken ct = default)
    {
        var userPrompt = prompt?.Trim() ?? string.Empty;
        var attList = attachments ?? Array.Empty<ChatAttachmentDto>();
        if (string.IsNullOrWhiteSpace(userPrompt) && attList.Count == 0) return;
        if (string.IsNullOrEmpty(sessionId)) return;

        // If this session is already running an execution, enqueue this message.
        if (_activeExecutions.ContainsKey(sessionId))
        {
            if (role.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                // Internal system events must NEVER be placed in the user's interactive prompt queue!
                // Instead, queue in _pendingSystemEvents to be processed after the active execution finishes.
                var queue = _pendingSystemEvents.GetOrAdd(sessionId, _ => new ConcurrentQueue<string>());
                queue.Enqueue(userPrompt);
                return;
            }

            _chatService.EnqueueMessage(sessionId, new ChatSendMessageDto(userPrompt, attList.ToList(), sessionId));
            return;
        }

        var sess = _chatService.GetSession(sessionId);
        var targetAgent = !string.IsNullOrEmpty(agentId)
            ? agentId
            : (sess?.AgentId ?? _configService.Settings.CodingAgent ?? "claude");
        var targetModel = !string.IsNullOrEmpty(modelId) && modelId != "default"
            ? modelId
            : (sess?.ModelId ?? "default");
        var targetEffort = !string.IsNullOrEmpty(effort)
            ? effort
            : (sess?.Effort ?? "default");

        var jobTimeoutMinutes = _configService.Settings.JobTimeout;
        var totalTimeout = jobTimeoutMinutes > 0
            ? TimeSpan.FromMinutes(jobTimeoutMinutes)
            : TimeSpan.FromMinutes(15);

        var cts = new CancellationTokenSource(totalTimeout);
        var activeExec = new ActiveChatExecution(cts);
        _activeExecutions[sessionId] = activeExec;

        _chatService.SetSessionGenerating(sessionId, true);
        SessionGeneratingChanged?.Invoke(sessionId);

        // Process attachments and build user prompt
        var attachedFilePaths = new List<string>();
        var attachmentErrors = new List<string>();
        if (attList.Count > 0)
        {
            var attachDir = Path.Combine(_configService.TendrilHome, "Attachments", sessionId);
            if (!Directory.Exists(attachDir))
            {
                Directory.CreateDirectory(attachDir);
            }

            foreach (var att in attList)
            {
                try
                {
                    var rawName = Path.GetFileName(att.Name);
                    var fileName = !string.IsNullOrWhiteSpace(rawName)
                        ? string.Concat(rawName.Split(Path.GetInvalidFileNameChars()))
                        : $"file_{Guid.NewGuid():N}.bin";
                    if (string.IsNullOrWhiteSpace(fileName)) fileName = $"file_{Guid.NewGuid():N}.bin";
                    var filePath = !string.IsNullOrWhiteSpace(att.LocalPath) && File.Exists(att.LocalPath)
                        ? att.LocalPath
                        : Path.Combine(attachDir, fileName);

                    if (!string.IsNullOrEmpty(att.Base64Data))
                    {
                        var base64 = att.Base64Data.Contains(",")
                            ? att.Base64Data[(att.Base64Data.IndexOf(",") + 1)..]
                            : att.Base64Data;
                        var bytes = Convert.FromBase64String(base64);
                        File.WriteAllBytes(filePath, bytes);
                    }

                    if (File.Exists(filePath))
                    {
                        attachedFilePaths.Add(filePath);
                    }
                    else
                    {
                        attachmentErrors.Add($"Attachment '{att.Name}' was not found at {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    attachmentErrors.Add($"Failed to process attachment '{att.Name}': {ex.Message}");
                }
            }
        }

        var promptWithAttachments = userPrompt;
        if (attachedFilePaths.Count > 0)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                sb.AppendLine(userPrompt);
                sb.AppendLine();
            }
            sb.AppendLine("[Attached Files]:");
            foreach (var path in attachedFilePaths)
            {
                sb.AppendLine($"- {path}");
            }
            promptWithAttachments = sb.ToString().TrimEnd();
        }

        if (attachmentErrors.Count > 0)
        {
            var warning = "Warning: Some attachments could not be processed:\n" + string.Join("\n", attachmentErrors.Select(e => $"- {e}"));
            _chatService.AddMessage(sessionId, "assistant", warning, targetAgent, targetModel, effort: targetEffort);
        }

        // Add user or system message to history
        _chatService.AddMessage(sessionId, role, promptWithAttachments, targetAgent, targetModel, effort: targetEffort);

        // Build prompt with conversation history and spawned jobs status
        var currentSess = _chatService.GetSession(sessionId);
        var history = currentSess?.Messages ?? [];
        var agentPromptBuilder = new StringBuilder();

        var jobService = ResolvedJobService;
        if (currentSess?.SpawnedJobIds is { Count: > 0 } spawnedIds && jobService != null)
        {
            var spawnedJobs = new List<(string Id, string Type, JobStatus Status, string? PlanId, string? PlanTitle, string? StatusMessage)>();
            foreach (var jId in spawnedIds)
            {
                var j = jobService.GetJob(jId);
                if (j != null && string.Equals(j.ChatSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    spawnedJobs.Add((j.Id, j.Type, j.Status, j.ReportedPlanId, j.ReportedPlanTitle, j.StatusMessage));
                }
            }

            if (spawnedJobs.Count > 0)
            {
                agentPromptBuilder.AppendLine("# Jobs Spawned in this Chat Session");
                agentPromptBuilder.AppendLine("The following jobs were spawned in this chat session:");
                agentPromptBuilder.AppendLine();

                bool allDone = true;
                bool anyFailed = false;

                foreach (var sj in spawnedJobs)
                {
                    var planPart = !string.IsNullOrEmpty(sj.PlanId)
                        ? $" | Plan: {sj.PlanId} ({sj.PlanTitle})"
                        : "";
                    var msgPart = !string.IsNullOrEmpty(sj.StatusMessage)
                        ? $" | Message: {sj.StatusMessage}"
                        : "";
                    agentPromptBuilder.AppendLine($"- Job {sj.Id}: {sj.Type} | Status: {sj.Status}{planPart}{msgPart}");

                    if (sj.Status == JobStatus.Failed || sj.Status == JobStatus.Timeout)
                    {
                        anyFailed = true;
                    }
                    if (sj.Status != JobStatus.Completed)
                    {
                        allDone = false;
                    }
                }
                agentPromptBuilder.AppendLine();

                if (allDone)
                {
                    agentPromptBuilder.AppendLine("All spawned jobs have completed. Proactively guide the user through the next steps (e.g. ask if they want you to review the plan or implementation, inspect results, or proceed to creating PRs).");
                }
                else if (anyFailed)
                {
                    agentPromptBuilder.AppendLine("Some spawned jobs failed or encountered issues. Guide the user through the failures and offer to diagnose, retry, or adjust the plan.");
                }
                else
                {
                    agentPromptBuilder.AppendLine("Some spawned jobs are still running or pending. Inform the user of their progress as appropriate.");
                }

                agentPromptBuilder.AppendLine("---");
                agentPromptBuilder.AppendLine();
            }
        }

        if (history.Count > 1) // Prior messages exist before this current user message
        {
            agentPromptBuilder.AppendLine("# Previous Conversation Discussion History");
            agentPromptBuilder.AppendLine("The following is the previous conversation history in this chat session:");
            agentPromptBuilder.AppendLine();

            // Exclude the last message which is the current user request
            foreach (var prevMsg in history.Take(history.Count - 1))
            {
                var roleLabel = prevMsg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                    ? "User"
                    : (prevMsg.Role.Equals("system", StringComparison.OrdinalIgnoreCase) ? "System Event" : "Assistant");
                agentPromptBuilder.AppendLine($"### {roleLabel}");
                agentPromptBuilder.AppendLine(prevMsg.Content);
                agentPromptBuilder.AppendLine();
            }

            agentPromptBuilder.AppendLine("---");
            agentPromptBuilder.AppendLine();
        }

        agentPromptBuilder.AppendLine("# Current Chat Session");
        agentPromptBuilder.AppendLine($"Chat Session ID: {sessionId}");
        agentPromptBuilder.AppendLine($"When starting jobs using `tendril job start`, always include `--chat-session {sessionId}` so the job is tracked in this chat session.");
        agentPromptBuilder.AppendLine("---");
        agentPromptBuilder.AppendLine();

        if (role.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            agentPromptBuilder.AppendLine("# Current Event Notification");
            agentPromptBuilder.AppendLine(promptWithAttachments);
            agentPromptBuilder.AppendLine();
            agentPromptBuilder.AppendLine("Evaluate this completed job event. Proactively inspect the job outcomes/artifacts if needed, determine whether any action is needed, and advise the user with a concise summary and suggested next steps.");
        }
        else
        {
            agentPromptBuilder.AppendLine("# Current User Request");
            agentPromptBuilder.AppendLine(promptWithAttachments);
        }
        var fullAgentPrompt = agentPromptBuilder.ToString();

        // Initialize assistant message record in chat history so an in-progress response exists immediately on disk
        var assistantMsg = _chatService.AddMessage(sessionId, "assistant", string.Empty, targetAgent, targetModel, effort: targetEffort);
        var assistantMessageId = assistantMsg.Id;
        activeExec.AssistantMessageId = assistantMessageId;

        // Launch background agent execution
        var executionTask = Task.Run(async () =>
        {
            string? lastTextEvent = null;
            var openToolCalls = new HashSet<string>();

            try
            {
                var effortOverride = targetEffort != "default" ? AgentProviderFactory.ParseEffort(targetEffort) : null;
                var context = AgentLaunchHelper.PrepareResolutionContext(
                    _configService,
                    _agentRunner,
                    targetAgent,
                    fullAgentPrompt,
                    modelOverride: targetModel != "default" ? targetModel : null,
                    effortOverride: effortOverride,
                    permissionMode: PermissionMode.FullAuto);

                var envWithChat = new Dictionary<string, string>(context.ExtraEnvironment ?? new Dictionary<string, string>())
                {
                    ["TENDRIL_CHAT_SESSION_ID"] = sessionId
                };
                context = context with { ExtraEnvironment = envWithChat };

                var session = await _agentRunner.LaunchAsync(context, cts.Token);
                activeExec.Session = session;

                using var sub = session.Events.Subscribe(evt =>
                {
                    try
                    {
                        if (evt is ToolCallEvent toolCall && !string.IsNullOrEmpty(toolCall.ToolUseId))
                        {
                            lock (activeExec.Lock)
                            {
                                openToolCalls.Add(toolCall.ToolUseId);
                            }
                        }
                        else if (evt is ToolResultEvent toolResult)
                        {
                            if (!string.IsNullOrEmpty(toolResult.ToolUseId))
                            {
                                lock (activeExec.Lock)
                                {
                                    openToolCalls.Remove(toolResult.ToolUseId);
                                }
                            }
                            if (!string.IsNullOrWhiteSpace(toolResult.Output))
                            {
                                TryTrackSpawnedJob(sessionId, toolResult.Output);
                            }
                        }
                        else if (evt is TextEvent textEvt && !string.IsNullOrWhiteSpace(textEvt.Text))
                        {
                            lock (activeExec.Lock)
                            {
                                if (textEvt.IsDelta)
                                {
                                    lastTextEvent = (lastTextEvent ?? "") + textEvt.Text;
                                }
                                else
                                {
                                    lastTextEvent = textEvt.Text;
                                }
                            }
                            lock (activeExec.Lock)
                            {
                                activeExec.LastText = textEvt.Text;
                            }
                        }

                        var wireJson = _serializer.Serialize(evt);
                        if (!string.IsNullOrEmpty(wireJson))
                        {
                            lock (activeExec.Lock)
                            {
                                activeExec.RawLines.Add(wireJson);
                            }
                            StreamLineEmitted?.Invoke(sessionId, wireJson);
                            StreamUpdated?.Invoke(sessionId);

                            if (Stopwatch.GetElapsedTime(activeExec.LastPersistTicks).TotalSeconds >= 1.0)
                            {
                                activeExec.LastPersistTicks = Stopwatch.GetTimestamp();
                                string? currentText;
                                string? currentRaw;
                                lock (activeExec.Lock)
                                {
                                    currentText = activeExec.LastText;
                                    currentRaw = activeExec.RawLines.Count > 0 ? string.Join("\n", activeExec.RawLines) : null;
                                }
                                _chatService.UpdateMessage(sessionId, assistantMessageId, currentText ?? string.Empty, currentRaw, flushImmediately: false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to serialize chat event for session {SessionId}", sessionId);
                    }
                });

                var result = await session.WaitForCompletionAsync(cts.Token);

                string? collectedText;
                string? fullRawStream = null;
                lock (activeExec.Lock)
                {
                    collectedText = lastTextEvent ?? activeExec.LastText;
                    if (activeExec.RawLines.Count > 0)
                        fullRawStream = string.Join("\n", activeExec.RawLines);
                }

                var responseContent = !string.IsNullOrWhiteSpace(result.Response)
                    ? result.Response
                    : (!string.IsNullOrWhiteSpace(collectedText)
                        ? collectedText
                        : (result.IsSuccess
                            ? "Task completed successfully."
                            : "Agent execution completed with status code " + (result.ExitCode?.ToString() ?? "unknown")));

                _chatService.UpdateMessage(sessionId, assistantMessageId, responseContent, rawStream: fullRawStream, flushImmediately: true);

                // Auto-generate title on first exchange
                var updatedSession = _chatService.GetSession(sessionId);
                if (updatedSession != null &&
                    (updatedSession.Title == "New Chat" || string.IsNullOrWhiteSpace(updatedSession.Title)) &&
                    updatedSession.Messages.Count == 2)
                {
                    var firstUserMsg = updatedSession.Messages.FirstOrDefault(m => m.Role == "user")?.Content;
                    if (!string.IsNullOrWhiteSpace(firstUserMsg))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _namingService.GenerateAndSetTitleAsync(
                                    sessionId,
                                    firstUserMsg,
                                    responseContent,
                                    targetAgent,
                                    targetModel);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Failed to generate title for session {SessionId}", sessionId);
                            }
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                string? fullRawStream = null;
                string? collectedText = null;
                lock (activeExec.Lock)
                {
                    collectedText = lastTextEvent ?? activeExec.LastText;

                    foreach (var toolUseId in openToolCalls)
                    {
                        var cancelToolResult = new ToolResultEvent
                        {
                            Kind = AgentEventKind.ToolResult,
                            Timestamp = DateTimeOffset.UtcNow,
                            ToolUseId = toolUseId,
                            Output = "[Cancelled]",
                            IsError = true
                        };
                        var cancelToolJson = _serializer.Serialize(cancelToolResult);
                        if (!string.IsNullOrEmpty(cancelToolJson))
                        {
                            activeExec.RawLines.Add(cancelToolJson);
                            StreamLineEmitted?.Invoke(sessionId, cancelToolJson);
                        }
                    }
                    openToolCalls.Clear();

                    var cancelEvt = new TextEvent
                    {
                        Kind = AgentEventKind.Text,
                        Timestamp = DateTimeOffset.UtcNow,
                        Text = "Execution was cancelled.",
                        IsDelta = false
                    };
                    var cancelJson = _serializer.Serialize(cancelEvt);
                    if (!string.IsNullOrEmpty(cancelJson))
                    {
                        activeExec.RawLines.Add(cancelJson);
                        StreamLineEmitted?.Invoke(sessionId, cancelJson);
                    }

                    if (activeExec.RawLines.Count > 0)
                    {
                        fullRawStream = string.Join("\n", activeExec.RawLines);
                    }
                }

                var responseContent = !string.IsNullOrWhiteSpace(collectedText)
                    ? $"{collectedText}\n\nExecution was cancelled."
                    : "Execution was cancelled.";

                _chatService.UpdateMessage(sessionId, assistantMessageId, responseContent, rawStream: fullRawStream, flushImmediately: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing request for session {SessionId}", sessionId);
                string? fullRawStream = null;
                string? collectedText = null;
                lock (activeExec.Lock)
                {
                    collectedText = lastTextEvent ?? activeExec.LastText;

                    foreach (var toolUseId in openToolCalls)
                    {
                        var errToolResult = new ToolResultEvent
                        {
                            Kind = AgentEventKind.ToolResult,
                            Timestamp = DateTimeOffset.UtcNow,
                            ToolUseId = toolUseId,
                            Output = $"[Error: {ex.Message}]",
                            IsError = true
                        };
                        var errToolJson = _serializer.Serialize(errToolResult);
                        if (!string.IsNullOrEmpty(errToolJson))
                        {
                            activeExec.RawLines.Add(errToolJson);
                            StreamLineEmitted?.Invoke(sessionId, errToolJson);
                        }
                    }
                    openToolCalls.Clear();

                    var errorEvt = new ErrorEvent
                    {
                        Kind = AgentEventKind.Error,
                        Timestamp = DateTimeOffset.UtcNow,
                        Message = $"Error executing request: {ex.Message}"
                    };
                    var errorJson = _serializer.Serialize(errorEvt);
                    if (!string.IsNullOrEmpty(errorJson))
                    {
                        activeExec.RawLines.Add(errorJson);
                        StreamLineEmitted?.Invoke(sessionId, errorJson);
                    }

                    if (activeExec.RawLines.Count > 0)
                    {
                        fullRawStream = string.Join("\n", activeExec.RawLines);
                    }
                }

                var responseContent = !string.IsNullOrWhiteSpace(collectedText)
                    ? $"{collectedText}\n\nError executing request: {ex.Message}"
                    : $"Error executing request: {ex.Message}";

                _chatService.UpdateMessage(sessionId, assistantMessageId, responseContent, rawStream: fullRawStream, flushImmediately: true);
            }
            finally
            {
                if (_activeExecutions.TryRemove(sessionId, out var removedExec))
                {
                    removedExec.Dispose();
                }

                _chatService.SetSessionGenerating(sessionId, false);
                SessionGeneratingChanged?.Invoke(sessionId);
                StreamUpdated?.Invoke(sessionId);

                if (!activeExec.IsInterrupted)
                {
                    // Process pending internal system event first, if any
                    if (_pendingSystemEvents.TryGetValue(sessionId, out var sysQueue) && sysQueue.TryDequeue(out var pendingSysEvent))
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(200, CancellationToken.None);
                            await SendMessageAsync(sessionId, pendingSysEvent, role: "system");
                        });
                    }
                    // Otherwise process next queued user message if one exists
                    else if (_chatService.TryDequeueMessage(sessionId, out var nextQueuedItem) && nextQueuedItem != null)
                    {
                        _ = SendMessageAsync(sessionId, nextQueuedItem.Prompt, nextQueuedItem.Attachments, targetAgent, targetModel, targetEffort);
                    }
                }
            }
        });
        activeExec.ExecutionTask = executionTask;
    }

    public async Task CancelAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        if (_activeExecutions.TryRemove(sessionId, out var exec))
        {
            exec.IsInterrupted = true;
            try
            {
                FlushExecution(sessionId, exec, "Execution was cancelled.");
                await exec.Cts.CancelAsync();
                if (exec.Session != null)
                {
                    await exec.Session.StopAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Exception stopping session {SessionId}", sessionId);
            }

            if (exec.ExecutionTask != null)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await exec.ExecutionTask.WaitAsync(timeoutCts.Token);
                }
                catch { }
            }

            exec.Dispose();
        }

        _chatService.ClearQueuedMessages(sessionId);
        _pendingSystemEvents.TryRemove(sessionId, out _);
        _chatService.SetSessionGenerating(sessionId, false);
        SessionGeneratingChanged?.Invoke(sessionId);
        StreamUpdated?.Invoke(sessionId);
    }

    public async Task InterruptAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        if (_activeExecutions.TryRemove(sessionId, out var exec))
        {
            exec.IsInterrupted = true;
            try
            {
                await exec.Cts.CancelAsync();
                if (exec.Session != null)
                {
                    await exec.Session.StopAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Exception stopping session {SessionId}", sessionId);
            }

            if (exec.ExecutionTask != null)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await exec.ExecutionTask.WaitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (exec.Session != null)
                    {
                        try { await exec.Session.KillAsync(); } catch { }
                    }
                }
                catch
                {
                    // Ignore exceptions from cancelled task
                }
            }

            exec.Dispose();
            _chatService.SetSessionGenerating(sessionId, false);
            SessionGeneratingChanged?.Invoke(sessionId);
            StreamUpdated?.Invoke(sessionId);
        }
    }

    public async Task ForceSendMessageAsync(
        string sessionId,
        string prompt,
        IReadOnlyList<ChatAttachmentDto>? attachments = null,
        string? agentId = null,
        string? modelId = null,
        string? effort = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        // Interrupt currently running execution if any, allowing it to cancel cleanly
        await InterruptAsync(sessionId);

        // Send the new message immediately
        await SendMessageAsync(
            sessionId,
            prompt,
            attachments,
            agentId,
            modelId,
            effort,
            role: "user",
            ct: ct);
    }

    public void Dispose()
    {
        if (_jobServiceSubscribed && ResolvedJobService != null)
        {
            ResolvedJobService.JobFinished -= OnJobFinished;
        }

        foreach (var (sessionId, exec) in _activeExecutions)
        {
            try
            {
                FlushExecution(sessionId, exec);
                exec.Cts.Cancel();
                exec.Dispose();
            }
            catch { }
        }
        _activeExecutions.Clear();
        _pendingSystemEvents.Clear();
    }

    private void OnJobFinished(JobItem job)
    {
        if (job == null) return;

        string? targetSessionId = job.ChatSessionId;
        if (string.IsNullOrEmpty(targetSessionId) && !string.IsNullOrEmpty(job.PlanFile))
        {
            var folderName = Path.GetFileName(job.PlanFile);
            var planReader = ResolvedPlanReaderService;
            var plan = planReader?.GetPlanByFolder(job.PlanFile) ?? (folderName != job.PlanFile ? planReader?.GetPlanByFolder(folderName) : null);
            targetSessionId = plan?.ChatSessionId;

            if (string.IsNullOrEmpty(targetSessionId))
            {
                var db = ResolvedDatabase;
                var dbPlan = db?.GetPlanByFolder(job.PlanFile) ?? (folderName != job.PlanFile ? db?.GetPlanByFolder(folderName) : null);
                targetSessionId = dbPlan?.ChatSessionId;

                if (string.IsNullOrEmpty(targetSessionId) && db != null)
                {
                    try
                    {
                        var jobs = db.GetJobsForPlan(folderName);
                        if (jobs.Count == 0 && folderName != job.PlanFile)
                            jobs = db.GetJobsForPlan(job.PlanFile);

                        targetSessionId = jobs
                            .Where(j => !string.IsNullOrEmpty(j.ChatSessionId))
                            .OrderByDescending(j => j.StartedAt)
                            .ThenByDescending(j => j.Id)
                            .FirstOrDefault()?.ChatSessionId;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to resolve historical jobs for plan {PlanFile}", job.PlanFile);
                    }
                }
            }

            if (!string.IsNullOrEmpty(targetSessionId))
            {
                job.ChatSessionId = targetSessionId;
            }
        }

        if (string.IsNullOrEmpty(targetSessionId)) return;

        var sess = _chatService.GetSession(targetSessionId);
        if (sess == null) return;

        var notifKey = $"{targetSessionId}:{job.Id}:{job.Status}";
        if (!_notifiedJobCompletions.TryAdd(notifKey, 0)) return;

        var outcomeSummary = !string.IsNullOrEmpty(job.StatusMessage)
            ? job.StatusMessage
            : (job.Status == JobStatus.Completed ? "Completed successfully" : job.Status.ToString());
        var planInfo = !string.IsNullOrEmpty(job.ReportedPlanTitle)
            ? $"{job.ReportedPlanId}: {job.ReportedPlanTitle}"
            : (!string.IsNullOrEmpty(job.PlanFile) ? Path.GetFileNameWithoutExtension(job.PlanFile) : job.Type);

        var eventMessage = $"[System Event] Job {job.Id} ({job.Type}) for '{planInfo}' has finished with status: {job.Status} ({outcomeSummary}). " +
            "Please inspect the outcome, determine whether any action is needed or if any issues occurred, and proactively guide the user on the results and next steps.";

        _ = SendMessageAsync(targetSessionId, eventMessage, role: "system");
    }

    private void FlushExecution(string sessionId, ActiveChatExecution exec, string? fallbackMessage = null)
    {
        if (string.IsNullOrEmpty(exec.AssistantMessageId)) return;
        try
        {
            string? collectedText;
            string? fullRawStream = null;
            lock (exec.Lock)
            {
                collectedText = exec.LastText;
                if (exec.RawLines.Count > 0)
                    fullRawStream = string.Join("\n", exec.RawLines);
            }

            var content = !string.IsNullOrWhiteSpace(collectedText)
                ? collectedText
                : (fallbackMessage ?? string.Empty);

            _chatService.UpdateMessage(sessionId, exec.AssistantMessageId, content, rawStream: fullRawStream, flushImmediately: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to flush execution for session {SessionId}", sessionId);
        }
    }

    private sealed class ActiveChatExecution : IDisposable
    {
        public IAgentSession? Session { get; set; }
        public CancellationTokenSource Cts { get; }
        public List<string> RawLines { get; } = [];
        public object Lock { get; } = new();
        public long LastStreamUpdateTicks { get; set; }
        public long LastPersistTicks { get; set; }
        public string? AssistantMessageId { get; set; }
        public string? LastText { get; set; }
        public Task? ExecutionTask { get; set; }
        public bool IsInterrupted { get; set; }

        public ActiveChatExecution(CancellationTokenSource cts)
        {
            Cts = cts;
            LastPersistTicks = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            try { Cts.Dispose(); } catch { }
        }
    }
}
