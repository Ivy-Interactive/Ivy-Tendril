using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

        var activeSessionId = UseState<string?>(args?.SessionId);
        var selectedAgent = UseState(() => configService.Settings.CodingAgent ?? "claude");
        var selectedModel = UseState("opus");
        var isStreaming = UseState(false);
        var streamingText = UseState("");
        var initialHandled = UseRef(false);

        // Map sessions to DTOs
        var sessions = chatService.GetSessions();
        if (activeSessionId.Value == null && sessions.Count > 0 && !initialHandled.Value && string.IsNullOrEmpty(args?.Prompt))
        {
            activeSessionId.Set(sessions[0].Id);
        }

        var activeSession = activeSessionId.Value != null ? chatService.GetSession(activeSessionId.Value) : null;

        if (activeSession != null)
        {
            if (selectedAgent.Value != activeSession.AgentId && !string.IsNullOrEmpty(activeSession.AgentId))
            {
                selectedAgent.Set(activeSession.AgentId);
            }
            if (selectedModel.Value != activeSession.ModelId && !string.IsNullOrEmpty(activeSession.ModelId))
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

        var sessionDtos = sessions.Select(s => new ChatSessionDto(
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
            )).ToList()
        )).ToList();

        async Task SendMessage(string userPrompt)
        {
            var trimmed = userPrompt.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || isStreaming.Value) return;

            string targetSessionId = activeSessionId.Value ?? "";
            if (string.IsNullOrEmpty(targetSessionId))
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
                targetSessionId = newSess.Id;
                activeSessionId.Set(targetSessionId);
            }

            chatService.AddMessage(targetSessionId, "user", trimmed, selectedAgent.Value, selectedModel.Value);
            isStreaming.Set(true);
            streamingText.Set("");

            try
            {
                var context = new AgentResolutionContext
                {
                    AgentId = selectedAgent.Value,
                    Prompt = trimmed,
                    ModelOverride = selectedModel.Value,
                    WorkingDirectory = !string.IsNullOrEmpty(configService.TendrilHome) ? configService.TendrilHome : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    PermissionMode = PermissionMode.FullAuto,
                };

                var session = await agentRunner.LaunchAsync(context);

                var rawLines = new ConcurrentBag<string>();
                using var sub = session.RawOutput?.Subscribe(line =>
                {
                    rawLines.Add(line);
                    streamingText.Set(string.Join("\n", rawLines));
                });

                var result = await session.WaitForCompletionAsync();

                var responseContent = !string.IsNullOrWhiteSpace(result.Response)
                    ? result.Response
                    : (result.IsSuccess ? "Task completed successfully." : "Agent execution completed with status code " + (result.ExitCode?.ToString() ?? "unknown"));

                var fullRawStream = rawLines.Count > 0 ? string.Join("\n", rawLines) : null;
                chatService.AddMessage(targetSessionId, "assistant", responseContent, selectedAgent.Value, selectedModel.Value, rawStream: fullRawStream);
            }
            catch (Exception ex)
            {
                chatService.AddMessage(targetSessionId, "assistant", $"Error executing request: {ex.Message}", selectedAgent.Value, selectedModel.Value);
            }
            finally
            {
                isStreaming.Set(false);
                streamingText.Set("");
            }
        }

        if (!initialHandled.Value && !string.IsNullOrWhiteSpace(args?.Prompt))
        {
            initialHandled.Value = true;
            _ = Task.Run(async () => await SendMessage(args.Prompt));
        }

        return new Ivy.Tendril.Widgets.ChatWidget
        {
            ActiveSessionId = activeSessionId.Value,
            Sessions = sessionDtos,
            Agents = agentDtos,
            Models = modelDtos,
            SelectedAgent = selectedAgent.Value,
            SelectedModel = selectedModel.Value,
            IsStreaming = isStreaming.Value,
            StreamingText = streamingText.Value,

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
            OnCreateSession = _ =>
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
                activeSessionId.Set(newSess.Id);
                return ValueTask.CompletedTask;
            },
            OnSendMessage = e =>
            {
                _ = SendMessage(e.Value);
                return ValueTask.CompletedTask;
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
