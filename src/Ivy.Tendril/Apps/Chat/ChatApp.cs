using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Chat;

[App(title: "Chat", icon: Icons.MessageSquare, group: ["Apps"], order: Constants.Chat, isVisible: true, allowDuplicateTabs: true)]
public class ChatApp : ViewBase
{
    public override object Build()
    {
        var args = UseArgs<ChatAppArgs>();
        var configService = UseService<IConfigService>();
        var chatService = UseService<IChatHistoryService>();
        var agentRunner = UseService<IAgentRunner>();
        var serializer = UseService<IEventSerializer>();

        var activeSessionId = UseState<string?>(args?.SessionId);
        var selectedAgent = UseState(() => configService.Settings.CodingAgent ?? "claude");
        var selectedModel = UseState("claude-opus-5");
        var lastSyncedSessionId = UseRef<string?>(null);
        var isStreaming = UseState(false);
        var streamingSessionId = UseState<string?>(null);
        var streamingText = UseState("");
        var streamingStream = UseStream<string>();
        var activeSessionRef = UseRef<IAgentSession?>(null);
        var runningSessionIds = UseState(new HashSet<string>());
        var messageQueue = UseRef(new ConcurrentQueue<ChatSendMessageDto>());
        var initialHandled = UseRef(false);

        // Map sessions to DTOs
        var sessions = chatService.GetSessions();
        if (activeSessionId.Value == null && sessions.Count > 0 && !initialHandled.Value && string.IsNullOrEmpty(args?.Prompt))
        {
            activeSessionId.Set(sessions[0].Id);
        }

        var activeSession = activeSessionId.Value != null ? chatService.GetSession(activeSessionId.Value) : null;

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
        }

        // Get dynamic agent options
        var registeredAgentIds = agentRunner.RegisteredAgents;
        if (registeredAgentIds.Count == 0)
        {
            registeredAgentIds = ["claude", "opencode", "codex", "gemini", "antigravity", "copilot", "ivy"];
        }

        var agentDtos = registeredAgentIds.Select(id =>
        {
            var (label, _) = AgentBranding.For(id, agentRunner);
            return new AgentOptionDto(id, label);
        }).ToList();

        // Get dynamic model options for selected agent from its model catalog provider
        var currentModelOptions = GetModelsForAgent(agentRunner, selectedAgent.Value);
        var modelDtos = currentModelOptions.Select(m => new ModelOptionDto(m.Id, m.DisplayName)).ToList();

        if (!currentModelOptions.Any(m => m.Id.Equals(selectedModel.Value, StringComparison.OrdinalIgnoreCase)))
        {
            if (currentModelOptions.Count > 0)
            {
                selectedModel.Set(currentModelOptions[0].Id);
            }
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
                    m.RawStream
                )).ToList(),
                status
            );
        }).ToList();

        async Task ExecuteSendMessage(ChatSendMessageDto dto)
        {
            var userPrompt = dto.Prompt.Trim();
            var attachments = dto.Attachments ?? [];
            if (string.IsNullOrWhiteSpace(userPrompt) && attachments.Count == 0) return;

            string targetSessionId = dto.SessionId ?? "";
            if (string.IsNullOrEmpty(targetSessionId)) return;

            // Track generating session and streaming session ID
            var nextRunning = new HashSet<string>(runningSessionIds.Value) { targetSessionId };
            runningSessionIds.Set(nextRunning);
            streamingSessionId.Set(targetSessionId);

            // Save attachments to disk
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
                        var filePath = Path.Combine(attachDir, att.Name);
                        if (!string.IsNullOrEmpty(att.Base64Data) && att.Base64Data.Contains(","))
                        {
                            var base64 = att.Base64Data[(att.Base64Data.IndexOf(",") + 1)..];
                            var bytes = Convert.FromBase64String(base64);
                            File.WriteAllBytes(filePath, bytes);
                            attachedFilePaths.Add(filePath);
                        }
                    }
                    catch
                    {
                        // Best effort file save
                    }
                }
            }

            var promptWithAttachments = userPrompt;
            if (attachedFilePaths.Count > 0)
            {
                var attachmentLines = string.Join("\n", attachedFilePaths.Select(p => $"[Attached file: {p}]"));
                promptWithAttachments = string.IsNullOrWhiteSpace(promptWithAttachments)
                    ? attachmentLines
                    : $"{promptWithAttachments}\n\n{attachmentLines}";
            }

            // Build session discussion history before saving the user prompt
            var existingSession = chatService.GetSession(targetSessionId);
            var agentPromptBuilder = new StringBuilder();

            if (existingSession != null && existingSession.Messages.Count > 0)
            {
                agentPromptBuilder.AppendLine("# Previous Conversation Discussion History");
                agentPromptBuilder.AppendLine("The following is the preceding discussion context in this chat thread:");
                agentPromptBuilder.AppendLine();

                foreach (var prevMsg in existingSession.Messages)
                {
                    if (string.IsNullOrWhiteSpace(prevMsg.Content)) continue;
                    var roleName = prevMsg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "User" : "Assistant";
                    agentPromptBuilder.AppendLine($"**[{roleName}]**:");
                    agentPromptBuilder.AppendLine(prevMsg.Content);
                    agentPromptBuilder.AppendLine();
                }

                agentPromptBuilder.AppendLine("---");
                agentPromptBuilder.AppendLine();
            }

            agentPromptBuilder.AppendLine("# Current User Request");
            agentPromptBuilder.AppendLine(promptWithAttachments);

            var fullAgentPrompt = agentPromptBuilder.ToString();

            // Save clean user prompt into database & UI
            chatService.AddMessage(targetSessionId, "user", promptWithAttachments, selectedAgent.Value, selectedModel.Value);
            isStreaming.Set(true);
            streamingText.Set("");

            try
            {
                var systemPrompt = AgentPromptCompiler.Compile(configService);
                var extraEnv = new Dictionary<string, string>();
                AgentProcessHelper.ApplyTendrilEnvironment(extraEnv, configService);

                var context = new AgentResolutionContext
                {
                    AgentId = selectedAgent.Value,
                    Prompt = fullAgentPrompt,
                    SystemPrompt = systemPrompt,
                    ModelOverride = selectedModel.Value,
                    WorkingDirectory = !string.IsNullOrEmpty(configService.TendrilHome) ? configService.TendrilHome : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    PermissionMode = PermissionMode.FullAuto,
                    ExtraEnvironment = extraEnv,
                };

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
                            streamingStream.Write(wireJson);
                            lock (rawLock)
                            {
                                rawLines.Add(wireJson);
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
                chatService.AddMessage(targetSessionId, "assistant", responseContent, selectedAgent.Value, selectedModel.Value, rawStream: fullRawStream);
            }
            catch (Exception ex)
            {
                chatService.AddMessage(targetSessionId, "assistant", $"Error executing request: {ex.Message}", selectedAgent.Value, selectedModel.Value);
            }
            finally
            {
                activeSessionRef.Value = null;
                isStreaming.Set(false);
                streamingText.Set("");
                streamingSessionId.Set(null);

                var finishedSet = new HashSet<string>(runningSessionIds.Value);
                finishedSet.Remove(targetSessionId);
                runningSessionIds.Set(finishedSet);

                // Process next message in queue if any
                if (messageQueue.Value.TryDequeue(out var nextDto))
                {
                    _ = ExecuteSendMessage(nextDto);
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
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
                targetSessionId = newSess.Id;
                activeSessionId.Set(targetSessionId);
            }

            var pinnedDto = new ChatSendMessageDto(userPrompt, dto.Attachments, targetSessionId);

            if (runningSessionIds.Value.Count > 0)
            {
                messageQueue.Value.Enqueue(pinnedDto);
            }
            else
            {
                _ = ExecuteSendMessage(pinnedDto);
            }
        }

        if (!initialHandled.Value && !string.IsNullOrWhiteSpace(args?.Prompt))
        {
            initialHandled.Value = true;
            SendMessage(new ChatSendMessageDto(args.Prompt));
        }

        return new Ivy.Tendril.Widgets.ChatWidget
        {
            ActiveSessionId = activeSessionId.Value,
            StreamingSessionId = streamingSessionId.Value,
            Sessions = sessionDtos,
            Agents = agentDtos,
            Models = modelDtos,
            SelectedAgent = selectedAgent.Value,
            SelectedModel = selectedModel.Value,
            IsStreaming = activeSessionId.Value != null && runningSessionIds.Value.Contains(activeSessionId.Value),
            StreamingText = streamingText.Value,
            StreamingStream = streamingStream,

            OnSelectSession = e =>
            {
                activeSessionId.Set(e.Value);
                return ValueTask.CompletedTask;
            },
            OnDeleteSession = e =>
            {
                chatService.DeleteSession(e.Value);
                if (activeSessionId.Value == e.Value)
                {
                    var remaining = chatService.GetSessions();
                    activeSessionId.Set(remaining.FirstOrDefault()?.Id);
                }
                return ValueTask.CompletedTask;
            },
            OnRenameSession = e =>
            {
                if (e.Value != null && e.Value.Length >= 2)
                {
                    chatService.RenameSession(e.Value[0], e.Value[1]);
                }
                return ValueTask.CompletedTask;
            },
            OnCreateSession = _ =>
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
                activeSessionId.Set(newSess.Id);
                return ValueTask.CompletedTask;
            },
            OnSendMessage = e =>
            {
                SendMessage(e.Value);
                return ValueTask.CompletedTask;
            },
            OnCancelStream = async _ =>
            {
                while (messageQueue.Value.TryDequeue(out var _)) { }
                try
                {
                    if (activeSessionRef.Value != null)
                    {
                        await activeSessionRef.Value.StopAsync();
                    }
                }
                catch
                {
                    // Ignore cancel exceptions
                }
                isStreaming.Set(false);
                streamingSessionId.Set(null);
                runningSessionIds.Set(new HashSet<string>());
            },
            OnAgentChanged = e =>
            {
                selectedAgent.Set(e.Value);
                var newModels = GetModelsForAgent(agentRunner, e.Value);
                if (newModels.Count > 0)
                {
                    selectedModel.Set(newModels[0].Id);
                }
                return ValueTask.CompletedTask;
            },
            OnModelChanged = e =>
            {
                selectedModel.Set(e.Value);
                return ValueTask.CompletedTask;
            }
        }
        .WithLayout()
        .Full()
        .RemoveParentPadding();
    }

    private static List<(string Id, string DisplayName)> GetModelsForAgent(IAgentRunner runner, string agentId)
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
                    return asyncResult.Models.Select(m => (m.Id, m.DisplayName ?? m.Id)).ToList();
                }
            }
            catch
            {
                // Fallback to static model catalog if discovery times out or throws
            }

            var staticModels = catalog.GetStaticModels();
            if (staticModels != null && staticModels.Count > 0)
            {
                return staticModels.Select(m => (m.Id, m.DisplayName ?? m.Id)).ToList();
            }
        }

        return [("default", "Default")];
    }
}
