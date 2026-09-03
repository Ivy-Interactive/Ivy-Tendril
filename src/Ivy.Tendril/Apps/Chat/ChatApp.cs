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
        var agentRunner = UseService<IAgentRunner>();
        var namingService = UseService<IChatSessionNamingService>();
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

            chatService.SessionsChanged += OnSessionsChanged;
            chatService.GeneratingSessionsChanged += OnGeneratingSessionsChanged;
            return Disposable.Create(() =>
            {
                chatService.SessionsChanged -= OnSessionsChanged;
                chatService.GeneratingSessionsChanged -= OnGeneratingSessionsChanged;
            });
        });

        var currentVersion = sessionVersion.Value;
        var sessions = chatService.GetSessions();
        var currentSessionId = activeSessionId.Value;
        if (sessions.Count == 0 && currentSessionId == null && string.IsNullOrEmpty(args?.Prompt))
        {
            var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
            currentSessionId = newSess.Id;
            activeSessionId.Set(currentSessionId);
            sessions = chatService.GetSessions();
        }
        else if (currentSessionId == null && sessions.Count > 0 && string.IsNullOrEmpty(args?.Prompt))
        {
            currentSessionId = sessions[0].Id;
            activeSessionId.Set(currentSessionId);
        }

        var activeSession = currentSessionId != null ? chatService.GetSession(currentSessionId) : null;
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
            var userPrompt = dto.Prompt?.Trim() ?? "";
            var attachments = dto.Attachments ?? [];
            if (string.IsNullOrWhiteSpace(userPrompt) && attachments.Count == 0) return;

            string targetSessionId = dto.SessionId ?? "";
            if (string.IsNullOrEmpty(targetSessionId)) return;

            var runningSet = new HashSet<string>(runningSessionIds.Value) { targetSessionId };
            runningSessionIds.Set(runningSet);
            streamingSessionId.Set(targetSessionId);
            chatService.SetSessionGenerating(targetSessionId, true);

            var attachedFilePaths = new List<string>();
            var attachmentErrors = new List<string>();
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
                chatService.AddMessage(targetSessionId, "assistant", warning, selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
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
                    permissionMode: PermissionMode.FullAuto);

                var session = await agentRunner.LaunchAsync(context);
                activeSessionRef.Value = session;

                var rawLines = new List<string>();
                string? lastTextEvent = null;
                var rawLock = new object();

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

                var jobTimeoutMinutes = configService.Settings.JobTimeout;
                var totalTimeout = jobTimeoutMinutes > 0
                    ? TimeSpan.FromMinutes(jobTimeoutMinutes)
                    : TimeSpan.FromMinutes(15);
                using var timeoutCts = new CancellationTokenSource(totalTimeout);

                var result = await session.WaitForCompletionAsync(timeoutCts.Token);

                string? collectedText = null;
                string? fullRawStream = null;
                lock (rawLock)
                {
                    collectedText = lastTextEvent;
                    if (rawLines.Count > 0) fullRawStream = string.Join("\n", rawLines);
                }

                var responseContent = !string.IsNullOrWhiteSpace(result.Response)
                    ? result.Response
                    : (!string.IsNullOrWhiteSpace(collectedText)
                        ? collectedText
                        : (result.IsSuccess ? "Task completed successfully." : "Agent execution completed with status code " + (result.ExitCode?.ToString() ?? "unknown")));

                chatService.AddMessage(targetSessionId, "assistant", responseContent, selectedAgent.Value, selectedModel.Value, rawStream: fullRawStream, effort: selectedEffort.Value);

                var currentSession = chatService.GetSession(targetSessionId);
                if (currentSession != null &&
                    (currentSession.Title == "New Chat" || string.IsNullOrWhiteSpace(currentSession.Title)) &&
                    currentSession.Messages.Count == 2)
                {
                    var firstUserMsg = currentSession.Messages.FirstOrDefault(m => m.Role == "user")?.Content;
                    if (!string.IsNullOrWhiteSpace(firstUserMsg))
                    {
                        var agentId = selectedAgent.Value;
                        var modelId = selectedModel.Value;
                        _ = Task.Run(async () =>
                        {
                            await namingService.GenerateAndSetTitleAsync(
                                targetSessionId,
                                firstUserMsg,
                                responseContent,
                                agentId,
                                modelId);
                        });
                    }
                }
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

                if (chatService.TryDequeueMessage(targetSessionId, out var nextQueuedItem) && nextQueuedItem != null)
                {
                    var nextDto = new ChatSendMessageDto(nextQueuedItem.Prompt, nextQueuedItem.Attachments, targetSessionId);
                    _ = ExecuteSendMessage(nextDto);
                }
            }
        }

        void SendMessage(ChatSendMessageDto dto)
        {
            var userPrompt = dto.Prompt?.Trim() ?? "";
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
                chatService.EnqueueMessage(targetSessionId, pinnedDto);
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
            activeSessionRef,
            sessionDtos,
            agentDtos,
            modelDtos,
            currentEffortOptions,
            supportsEffort,
            chatService,
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
