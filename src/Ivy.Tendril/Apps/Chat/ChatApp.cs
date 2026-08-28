using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive.Disposables;
using Ivy;
using Ivy.Core;
using Ivy.Core.Hooks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Apps.Chat.Dialogs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Chat;

[App(title: "Chat", icon: Icons.MessageSquare, group: ["Apps"], order: Constants.Chat, isVisible: false, allowDuplicateTabs: false)]
public class ChatApp : ViewBase
{
    public override object Build()
    {
        var args = UseArgs<ChatAppArgs>();
        var configService = UseService<IConfigService>();
        var chatService = UseService<IChatHistoryService>();
        var jobService = UseService<IJobService>();
        var planService = UseService<IPlanReaderService>();
        var agentRunner = UseService<IAgentRunner>();
        var serializer = UseService<IEventSerializer>();

        var activeSessionId = UseState<string?>(args?.SessionId);
        var sessionVersion = UseState(0);
        var selectedAgent = UseState(() => configService.Settings.CodingAgent ?? "claude");
        var selectedModel = UseState(() =>
        {
            var agent = configService.Settings.CodingAgent ?? "claude";
            var initialModels = GetModelsForAgent(agentRunner, agent);
            return initialModels.Count > 0 ? initialModels[0].Id : "default";
        });
        var selectedEffort = UseState("default");
        var lastSyncedSessionId = UseRef<string?>(null);
        var isStreaming = UseState(false);
        var streamingSessionId = UseState<string?>(null);
        var liveSessionStreams = UseState(new Dictionary<string, string>());
        var activeSessionRef = UseRef<IAgentSession?>(null);
        var runningSessionIds = UseState(() => new HashSet<string>(chatService.GetGeneratingSessionIds()));
        var messageQueue = UseRef(new ConcurrentQueue<ChatSendMessageDto>());
        var initialHandled = UseRef(false);

        var searchState = UseState("");

        UseEffect(() =>
        {
            void OnSessionsChanged(object? sender, EventArgs e) => sessionVersion.Set(v => v + 1);
            void OnGeneratingSessionsChanged(object? sender, EventArgs e)
            {
                sessionVersion.Set(v => v + 1);
                runningSessionIds.Set(new HashSet<string>(chatService.GetGeneratingSessionIds()));
            }
            void OnJobsChanged() => sessionVersion.Set(v => v + 1);

            chatService.SessionsChanged += OnSessionsChanged;
            chatService.GeneratingSessionsChanged += OnGeneratingSessionsChanged;
            jobService.JobsChanged += OnJobsChanged;
            jobService.JobsStructureChanged += OnJobsChanged;
            jobService.JobPropertyChanged += OnJobsChanged;
            planService.CountsInvalidated += OnJobsChanged;

            return Disposable.Create(() =>
            {
                chatService.SessionsChanged -= OnSessionsChanged;
                chatService.GeneratingSessionsChanged -= OnGeneratingSessionsChanged;
                jobService.JobsChanged -= OnJobsChanged;
                jobService.JobsStructureChanged -= OnJobsChanged;
                jobService.JobPropertyChanged -= OnJobsChanged;
                planService.CountsInvalidated -= OnJobsChanged;
            });
        });

        var currentVersion = sessionVersion.Value;
        var sessions = chatService.GetSessions();
        if (activeSessionId.Value == null && sessions.Count > 0 && !initialHandled.Value && string.IsNullOrEmpty(args?.Prompt))
        {
            activeSessionId.Set(sessions[0].Id);
        }

        var activeSession = activeSessionId.Value != null ? chatService.GetSession(activeSessionId.Value) : null;
        if (activeSession != null)
        {
            chatService.ClearSessionCompleted(activeSession.Id);
        }

        if (activeSession != null && lastSyncedSessionId.Value != activeSession.Id)
        {
            lastSyncedSessionId.Value = activeSession.Id;
            if (!string.IsNullOrEmpty(activeSession.AgentId))
            {
                selectedAgent.Set(activeSession.AgentId);
            }
            if (!string.IsNullOrEmpty(activeSession.ModelId))
            {
                selectedModel.Set(activeSession.ModelId);
            }
            if (!string.IsNullOrEmpty(activeSession.Effort))
            {
                selectedEffort.Set(activeSession.Effort);
            }
        }

        var registeredAgentIds = agentRunner.RegisteredAgents;
        if (registeredAgentIds.Count == 0)
        {
            registeredAgentIds = ["claude", "opencode", "codex", "gemini", "antigravity", "copilot", "ivy"];
        }

        var agentDtos = registeredAgentIds.Select(id =>
        {
            var (label, _) = AgentBranding.For(id, agentRunner, configService);
            return new AgentOptionDto(id, label);
        }).ToList();

        var currentModelOptions = GetModelsForAgent(agentRunner, selectedAgent.Value);
        var modelDtos = currentModelOptions.Select(m => new ModelOptionDto(m.Id, m.DisplayName)).ToList();

        if (!currentModelOptions.Any(m => m.Id.Equals(selectedModel.Value, StringComparison.OrdinalIgnoreCase)))
        {
            if (currentModelOptions.Count > 0)
            {
                selectedModel.Set(currentModelOptions[0].Id);
            }
        }

        var supportsEffort = DoesAgentSupportEffort(agentRunner, selectedAgent.Value);
        var currentEffortOptions = GetEffortsForAgentAndModel(agentRunner, selectedAgent.Value, selectedModel.Value);
        if (!currentEffortOptions.Any(e => e.Id.Equals(selectedEffort.Value, StringComparison.OrdinalIgnoreCase)))
        {
            selectedEffort.Set("default");
        }

        var sessionDtos = sessions.Select(s =>
        {
            var isGenerating = runningSessionIds.Value.Contains(s.Id);
            var status = isGenerating ? "generating" : "done";
            return new ChatSessionDto(
                s.Id,
                s.Title,
                s.AgentId,
                s.ModelId,
                s.CreatedAt.ToString("o"),
                s.UpdatedAt.ToString("o"),
                s.Messages.Select(m => new ChatMessageDto(
                    m.Id,
                    m.Role,
                    m.Content,
                    m.Timestamp.ToString("t"),
                    m.AgentId,
                    m.ModelId,
                    m.RawStream,
                    m.Effort
                )).ToList(),
                status,
                s.Effort
            );
        }).ToList();

        async Task ExecuteSendMessage(ChatSendMessageDto dto)
        {
            var userPrompt = dto.Prompt.Trim();
            var attachments = dto.Attachments ?? [];
            if (string.IsNullOrWhiteSpace(userPrompt) && attachments.Count == 0) return;

            string targetSessionId = dto.SessionId ?? "";
            if (string.IsNullOrEmpty(targetSessionId)) return;

            var runningSet = new HashSet<string>(runningSessionIds.Value) { targetSessionId };
            runningSessionIds.Set(runningSet);
            streamingSessionId.Set(targetSessionId);
            chatService.SetSessionGenerating(targetSessionId, true);

            var attachedFilePaths = new List<string>();
            if (attachments.Count > 0)
            {
                var attachDir = Path.Combine(configService.TendrilHome, "Attachments", targetSessionId);
                if (!Directory.Exists(attachDir))
                {
                    Directory.CreateDirectory(attachDir);
                }

                foreach (var att in attachments)
                {
                    try
                    {
                        var rawName = Path.GetFileName(att.Name);
                        var fileName = !string.IsNullOrWhiteSpace(rawName) ? rawName : $"file_{Guid.NewGuid():N}.bin";
                        var filePath = Path.Combine(attachDir, fileName);
                        if (!string.IsNullOrEmpty(att.Base64Data))
                        {
                            var base64 = att.Base64Data.Contains(",")
                                ? att.Base64Data[(att.Base64Data.IndexOf(",") + 1)..]
                                : att.Base64Data;
                            var bytes = Convert.FromBase64String(base64);
                            File.WriteAllBytes(filePath, bytes);
                        }
                        attachedFilePaths.Add(filePath);
                    }
                    catch
                    {
                        // Ignore attachment write exceptions
                    }
                }
            }

            var promptWithAttachments = userPrompt;
            if (attachedFilePaths.Count > 0)
            {
                var sb = new StringBuilder(userPrompt);
                sb.AppendLine("\n\n[Attached Files]:");
                foreach (var path in attachedFilePaths)
                {
                    sb.AppendLine($"- {path}");
                }
                promptWithAttachments = sb.ToString();
            }

            var sess = chatService.GetSession(targetSessionId);
            var history = sess?.Messages ?? [];

            var agentPromptBuilder = new StringBuilder();
            if (history.Count > 0)
            {
                agentPromptBuilder.AppendLine("# Previous Conversation Discussion History");
                agentPromptBuilder.AppendLine("The following is the previous conversation history in this chat session:");
                agentPromptBuilder.AppendLine();

                foreach (var prevMsg in history)
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

            chatService.AddMessage(targetSessionId, "user", promptWithAttachments, selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
            isStreaming.Set(true);

            try
            {
                var effortOverride = selectedEffort.Value != "default" ? AgentProviderFactory.ParseEffort(selectedEffort.Value) : null;
                var context = AgentLaunchHelper.PrepareResolutionContext(
                    configService,
                    agentRunner,
                    selectedAgent.Value,
                    fullAgentPrompt,
                    modelOverride: selectedModel.Value,
                    effortOverride: effortOverride,
                    permissionMode: PermissionMode.FullAuto,
                    chatSessionId: targetSessionId);

                var session = await agentRunner.LaunchAsync(context);
                activeSessionRef.Value = session;

                var rawLines = new List<string>();
                var rawLock = new object();

                using var sub = session.Events.Subscribe(evt =>
                {
                    try
                    {
                        var wireJson = serializer.Serialize(evt);
                        if (!string.IsNullOrEmpty(wireJson))
                        {
                            lock (rawLock)
                            {
                                rawLines.Add(wireJson);
                                var map = new Dictionary<string, string>(liveSessionStreams.Value)
                                {
                                    [targetSessionId] = string.Join("\n", rawLines)
                                };
                                liveSessionStreams.Set(map);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore serialization exceptions
                    }
                });

                var result = await session.WaitForCompletionAsync();

                var responseContent = !string.IsNullOrWhiteSpace(result.Response)
                    ? result.Response
                    : (result.IsSuccess ? "Task completed successfully." : "Agent execution completed with status code " + (result.ExitCode?.ToString() ?? "unknown"));

                string? fullRawStream = null;
                lock (rawLock)
                {
                    if (rawLines.Count > 0) fullRawStream = string.Join("\n", rawLines);
                }
                chatService.AddMessage(targetSessionId, "assistant", responseContent, selectedAgent.Value, selectedModel.Value, rawStream: fullRawStream, effort: selectedEffort.Value);
            }
            catch (Exception ex)
            {
                chatService.AddMessage(targetSessionId, "assistant", $"Error executing request: {ex.Message}", selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
            }
            finally
            {
                activeSessionRef.Value = null;
                isStreaming.Set(false);
                streamingSessionId.Set(null);

                var finishedSet = new HashSet<string>(runningSessionIds.Value);
                finishedSet.Remove(targetSessionId);
                runningSessionIds.Set(finishedSet);
                chatService.SetSessionGenerating(targetSessionId, false);

                var map = new Dictionary<string, string>(liveSessionStreams.Value);
                map.Remove(targetSessionId);
                liveSessionStreams.Set(map);

                var remainingItems = new List<ChatSendMessageDto>();
                ChatSendMessageDto? nextForSession = null;
                while (messageQueue.Value.TryDequeue(out var item))
                {
                    if (nextForSession == null && (item.SessionId == targetSessionId || string.IsNullOrEmpty(item.SessionId)))
                    {
                        nextForSession = item;
                    }
                    else
                    {
                        remainingItems.Add(item);
                    }
                }
                foreach (var rem in remainingItems)
                {
                    messageQueue.Value.Enqueue(rem);
                }

                if (nextForSession != null)
                {
                    _ = ExecuteSendMessage(nextForSession);
                }
            }
        }

        void SendMessage(ChatSendMessageDto dto)
        {
            var userPrompt = dto.Prompt.Trim();
            var attachments = dto.Attachments ?? [];
            if (string.IsNullOrWhiteSpace(userPrompt) && attachments.Count == 0) return;

            string targetSessionId = !string.IsNullOrEmpty(dto.SessionId) ? dto.SessionId : (activeSessionId.Value ?? "");
            if (string.IsNullOrEmpty(targetSessionId))
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
                targetSessionId = newSess.Id;
                activeSessionId.Set(targetSessionId);
            }

            var pinnedDto = new ChatSendMessageDto(userPrompt, dto.Attachments, targetSessionId);

            if (runningSessionIds.Value.Contains(targetSessionId))
            {
                messageQueue.Value.Enqueue(pinnedDto);
            }
            else
            {
                _ = ExecuteSendMessage(pinnedDto);
            }
        }

        if (!initialHandled.Value && !string.IsNullOrEmpty(args?.Prompt))
        {
            initialHandled.Value = true;
            var targetId = activeSessionId.Value;
            if (string.IsNullOrEmpty(targetId))
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
                targetId = newSess.Id;
                activeSessionId.Set(targetId);
            }
            SendMessage(new ChatSendMessageDto(args.Prompt, null, targetId));
        }

        var trackedJobs = new List<ChatTrackedJobDto>();
        var trackedPlans = new List<ChatTrackedPlanDto>();

        if (!string.IsNullOrEmpty(activeSessionId.Value))
        {
            var matchingJobs = jobService.GetJobs()
                .Where(j => j.ChatSessionId == activeSessionId.Value)
                .OrderByDescending(j => j.StartedAt ?? DateTime.MinValue)
                .ToList();

            var allPlans = planService.GetPlans();
            var planLookup = allPlans.ToDictionary(p => p.Id.ToString("D5"), p => p);

            foreach (var j in matchingJobs)
            {
                var planId = j.ResolvePlanId();
                string? planTitle = j.ReportedPlanTitle;
                if (string.IsNullOrEmpty(planTitle) && !string.IsNullOrEmpty(planId) && planLookup.TryGetValue(planId, out var matchedPlan))
                {
                    planTitle = matchedPlan.Title;
                }

                string durationStr = "";
                if (j.DurationSeconds.HasValue)
                {
                    durationStr = $"{j.DurationSeconds.Value}s";
                }
                else if (j.StartedAt.HasValue)
                {
                    var elapsed = (int)(DateTime.UtcNow - j.StartedAt.Value).TotalSeconds;
                    durationStr = $"{Math.Max(0, elapsed)}s";
                }

                trackedJobs.Add(new ChatTrackedJobDto(
                    j.Id,
                    j.Type,
                    planId,
                    planTitle,
                    j.Status.ToString(),
                    j.StatusMessage,
                    j.StartedAt?.ToString("o"),
                    durationStr
                ));
            }

            var planIds = matchingJobs
                .Select(j => j.ResolvePlanId())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            foreach (var pId in planIds)
            {
                if (planLookup.TryGetValue(pId, out var p))
                {
                    trackedPlans.Add(new ChatTrackedPlanDto(
                        p.Id.ToString("D5"),
                        p.Title,
                        p.FolderName,
                        p.Status.ToString()
                    ));
                }
                else if (Directory.Exists(planService.PlansDirectory))
                {
                    var pFolder = Directory.GetDirectories(planService.PlansDirectory, $"{pId}-*").FirstOrDefault();
                    if (pFolder != null)
                    {
                        var diskPlan = planService.GetPlanByFolder(pFolder);
                        if (diskPlan != null)
                        {
                            trackedPlans.Add(new ChatTrackedPlanDto(
                                diskPlan.Id.ToString("D5"),
                                diskPlan.Title,
                                diskPlan.FolderName,
                                diskPlan.Status.ToString()
                            ));
                        }
                    }
                }
            }
        }

        var sidebar = new SidebarView(
            sessions,
            activeSessionId,
            sessionVersion,
            selectedAgent,
            selectedModel,
            searchState,
            chatService
        );

        var content = new ContentView(
            activeSession,
            activeSessionId,
            sessionVersion,
            selectedAgent,
            selectedModel,
            selectedEffort,
            isStreaming,
            streamingSessionId,
            runningSessionIds,
            liveSessionStreams,
            messageQueue,
            activeSessionRef,
            sessionDtos,
            agentDtos,
            modelDtos,
            currentEffortOptions,
            supportsEffort,
            trackedJobs,
            trackedPlans,
            chatService,
            jobService,
            planService,
            configService,
            agentRunner,
            SendMessage
        );

        return new SidebarLayout(content, sidebar).SidebarContentScroll(Scroll.None);
    }

    internal static bool DoesAgentSupportEffort(IAgentRunner runner, string agentId)
    {
        var normalized = AgentProviderFactory.NormalizeAgentName(agentId);
        try
        {
            var descriptor = runner.GetDescriptor(normalized);
            return descriptor != null && descriptor.Capabilities.HasFlag(AgentCapabilities.EffortControl);
        }
        catch
        {
            return false;
        }
    }

    internal static List<EffortOptionDto> GetEffortsForAgentAndModel(IAgentRunner runner, string agentId, string? modelId)
    {
        var normalized = AgentProviderFactory.NormalizeAgentName(agentId);
        IAgentDescriptor? descriptor = null;
        try { descriptor = runner.GetDescriptor(normalized); } catch { }

        IReadOnlyList<EffortOption>? efforts = null;
        if (descriptor != null)
        {
            var catalog = runner.GetModelCatalog(normalized);
            if (catalog != null && !string.IsNullOrEmpty(modelId) && modelId != "default")
            {
                var staticModels = catalog.GetStaticModels();
                var match = staticModels?.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
                if (match?.SupportedEfforts != null && match.SupportedEfforts.Count > 0)
                {
                    efforts = match.SupportedEfforts;
                }
            }

            if (efforts == null || efforts.Count == 0)
            {
                efforts = descriptor.GetSupportedEfforts(modelId);
            }
            if (efforts == null || efforts.Count == 0)
            {
                efforts = descriptor.SupportedEfforts;
            }
        }

        var list = new List<EffortOptionDto> { new("default", "Default") };
        if (efforts != null && efforts.Count > 0)
        {
            list.AddRange(efforts.Select(e => new EffortOptionDto(e.Id, e.DisplayName)));
        }
        return list;
    }

    internal static List<(string Id, string DisplayName)> GetModelsForAgent(IAgentRunner runner, string agentId)
    {
        var normalized = AgentProviderFactory.NormalizeAgentName(agentId);
        var catalog = runner.GetModelCatalog(normalized);
        if (catalog != null)
        {
            try
            {
                var asyncResult = catalog.GetModelsAsync().GetAwaiter().GetResult();
                if (asyncResult != null && asyncResult.Models.Count > 0)
                {
                    var sorted = ModelCatalogSorter.Sort(asyncResult.Models);
                    return sorted.Select(m => (m.Id, m.DisplayName ?? m.Id)).ToList();
                }
            }
            catch
            {
                // Fallback to static model catalog if discovery times out or throws
            }

            var staticModels = catalog.GetStaticModels();
            if (staticModels != null && staticModels.Count > 0)
            {
                var sorted = ModelCatalogSorter.Sort(staticModels);
                return sorted.Select(m => (m.Id, m.DisplayName ?? m.Id)).ToList();
            }
        }

        return [("default", "Default")];
    }
}
