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
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
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

    private IJobService? ResolvedJobService => _jobService ?? _serviceProvider?.GetService<IJobService>();

    private readonly ConcurrentDictionary<string, ActiveChatExecution> _activeExecutions = new(StringComparer.OrdinalIgnoreCase);

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
        IJobService? jobService = null)
    {
        _configService = configService;
        _chatService = chatService;
        _agentRunner = agentRunner;
        _namingService = namingService;
        _serializer = serializer;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatExecutionService>.Instance;
        _serviceProvider = serviceProvider;
        _jobService = jobService;
        _chatService.ClearAllGeneratingSessions();
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
        CancellationToken ct = default)
    {
        var userPrompt = prompt?.Trim() ?? string.Empty;
        var attList = attachments ?? Array.Empty<ChatAttachmentDto>();
        if (string.IsNullOrWhiteSpace(userPrompt) && attList.Count == 0) return;
        if (string.IsNullOrEmpty(sessionId)) return;

        // If this session is already running an execution, enqueue this message.
        if (_activeExecutions.ContainsKey(sessionId))
        {
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

        // Add user message to history
        _chatService.AddMessage(sessionId, "user", promptWithAttachments, targetAgent, targetModel, effort: targetEffort);

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
                if (j != null)
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
                var roleLabel = prevMsg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "User" : "Assistant";
                agentPromptBuilder.AppendLine($"### {roleLabel}");
                agentPromptBuilder.AppendLine(prevMsg.Content);
                agentPromptBuilder.AppendLine();
            }

            agentPromptBuilder.AppendLine("---");
            agentPromptBuilder.AppendLine();
        }

        agentPromptBuilder.AppendLine("# Current User Request");
        agentPromptBuilder.AppendLine(promptWithAttachments);
        var fullAgentPrompt = agentPromptBuilder.ToString();

        // Launch background agent execution
        _ = Task.Run(async () =>
        {
            var rawLock = new object();
            var rawLines = new List<string>();
            string? lastTextEvent = null;

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
                        if (evt is TextEvent textEvt && !string.IsNullOrWhiteSpace(textEvt.Text))
                        {
                            lock (rawLock)
                            {
                                lastTextEvent = textEvt.Text;
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
                    collectedText = lastTextEvent;
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

                _chatService.AddMessage(sessionId, "assistant", responseContent, targetAgent, targetModel, rawStream: fullRawStream, effort: targetEffort);

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
                _chatService.AddMessage(sessionId, "assistant", "Execution was cancelled.", targetAgent, targetModel, effort: targetEffort);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing request for session {SessionId}", sessionId);
                _chatService.AddMessage(sessionId, "assistant", $"Error executing request: {ex.Message}", targetAgent, targetModel, effort: targetEffort);
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

                // Process next queued message if one exists
                if (_chatService.TryDequeueMessage(sessionId, out var nextQueuedItem) && nextQueuedItem != null)
                {
                    _ = SendMessageAsync(sessionId, nextQueuedItem.Prompt, nextQueuedItem.Attachments, targetAgent, targetModel, targetEffort);
                }
            }
        });
    }

    public async Task CancelAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        if (_activeExecutions.TryRemove(sessionId, out var exec))
        {
            try
            {
                await exec.Cts.CancelAsync();
                if (exec.Session != null)
                {
                    await exec.Session.StopAsync();
                }
                exec.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Exception stopping session {SessionId}", sessionId);
            }
        }

        _chatService.ClearQueuedMessages(sessionId);
        _chatService.SetSessionGenerating(sessionId, false);
        SessionGeneratingChanged?.Invoke(sessionId);
        StreamUpdated?.Invoke(sessionId);
    }

    public void Dispose()
    {
        foreach (var (_, exec) in _activeExecutions)
        {
            try
            {
                exec.Cts.Cancel();
                exec.Dispose();
            }
            catch { }
        }
        _activeExecutions.Clear();
    }

    private sealed class ActiveChatExecution : IDisposable
    {
        public IAgentSession? Session { get; set; }
        public CancellationTokenSource Cts { get; }
        public List<string> RawLines { get; } = [];
        public object Lock { get; } = new();
        public long LastStreamUpdateTicks { get; set; }

        public ActiveChatExecution(CancellationTokenSource cts)
        {
            Cts = cts;
        }

        public void Dispose()
        {
            try { Cts.Dispose(); } catch { }
        }
    }
}
