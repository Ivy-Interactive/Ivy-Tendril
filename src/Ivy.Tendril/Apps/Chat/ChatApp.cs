using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using Ivy;
using Ivy.Core;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers;
using Ivy.Tendril.Apps.Views;
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
        var executionService = UseService<IChatExecutionService>();
        var agentRunner = UseService<IAgentRunner>();

        var activeSessionId = UseState<string?>(() =>
        {
            if (!string.IsNullOrEmpty(args?.SessionId)) return args.SessionId;
            return chatService.GetSessions().FirstOrDefault()?.Id;
        });
        var sessionVersion = UseState(0);
        var selectedAgent = UseState(() =>
        {
            var sess = !string.IsNullOrEmpty(args?.SessionId) ? chatService.GetSession(args.SessionId) : chatService.GetSessions().FirstOrDefault();
            return sess?.AgentId ?? configService.Settings.CodingAgent ?? "claude";
        });
        var selectedModel = UseState(() =>
        {
            var sess = !string.IsNullOrEmpty(args?.SessionId) ? chatService.GetSession(args.SessionId) : chatService.GetSessions().FirstOrDefault();
            if (!string.IsNullOrEmpty(sess?.ModelId)) return sess.ModelId;
            var agent = sess?.AgentId ?? configService.Settings.CodingAgent ?? "claude";
            var initialModels = GetModelsForAgent(agentRunner, agent);
            return initialModels.Count > 0 ? initialModels[0].Id : "default";
        });
        var selectedEffort = UseState(() =>
        {
            var sess = !string.IsNullOrEmpty(args?.SessionId) ? chatService.GetSession(args.SessionId) : chatService.GetSessions().FirstOrDefault();
            return sess?.Effort ?? "default";
        });
        var searchState = UseState("");
        var initialHandled = UseRef(false);
        var streamVersion = UseState(0);

        UseEffect(() =>
        {
            void OnSessionsChanged(object? sender, EventArgs e) => sessionVersion.Set(v => v + 1);

            void OnGeneratingChanged(object? sender, EventArgs e)
            {
                if (!string.IsNullOrEmpty(activeSessionId.Value))
                {
                    chatService.ClearSessionCompleted(activeSessionId.Value);
                }
                sessionVersion.Set(v => v + 1);
            }

            void OnStreamUpdated(string sessId)
            {
                if (!string.IsNullOrEmpty(activeSessionId.Value) &&
                    string.Equals(sessId, activeSessionId.Value, StringComparison.OrdinalIgnoreCase))
                {
                    streamVersion.Set(v => v + 1);
                }
            }

            void OnSessionGeneratingChanged(string sessId)
            {
                if (!string.IsNullOrEmpty(activeSessionId.Value) &&
                    string.Equals(sessId, activeSessionId.Value, StringComparison.OrdinalIgnoreCase))
                {
                    chatService.ClearSessionCompleted(activeSessionId.Value);
                    streamVersion.Set(v => v + 1);
                    sessionVersion.Set(v => v + 1);
                }
            }

            chatService.SessionsChanged += OnSessionsChanged;
            chatService.GeneratingSessionsChanged += OnGeneratingChanged;
            executionService.StreamUpdated += OnStreamUpdated;
            executionService.SessionGeneratingChanged += OnSessionGeneratingChanged;

            if (!string.IsNullOrEmpty(activeSessionId.Value))
            {
                chatService.ClearSessionCompleted(activeSessionId.Value);
            }

            return Disposable.Create(() =>
            {
                chatService.SessionsChanged -= OnSessionsChanged;
                chatService.GeneratingSessionsChanged -= OnGeneratingChanged;
                executionService.StreamUpdated -= OnStreamUpdated;
                executionService.SessionGeneratingChanged -= OnSessionGeneratingChanged;
            });
        });

        void SelectSession(string sessionId)
        {
            activeSessionId.Set(sessionId);
            chatService.ClearSessionCompleted(sessionId);
            var sess = chatService.GetSession(sessionId);
            if (sess != null)
            {
                if (!string.IsNullOrEmpty(sess.AgentId)) selectedAgent.Set(sess.AgentId);
                if (!string.IsNullOrEmpty(sess.ModelId)) selectedModel.Set(sess.ModelId);
                if (!string.IsNullOrEmpty(sess.Effort)) selectedEffort.Set(sess.Effort);
            }
        }

        var currentVersion = sessionVersion.Value;
        _ = streamVersion.Value;
        var sessions = chatService.GetSessions();
        var currentSessionId = activeSessionId.Value;
        var activeSession = currentSessionId != null ? chatService.GetSession(currentSessionId) : null;
        var isSessionGenerating = currentSessionId != null && executionService.IsGenerating(currentSessionId);
        var streamSnapshot = isSessionGenerating ? executionService.GetStreamSnapshot(currentSessionId!) : string.Empty;

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
        var effectiveModel = currentModelOptions.Any(m => m.Id.Equals(selectedModel.Value, StringComparison.OrdinalIgnoreCase))
            ? selectedModel.Value
            : (currentModelOptions.Count > 0 ? currentModelOptions[0].Id : selectedModel.Value);
        var modelDtos = currentModelOptions.Select(m => new ModelOptionDto(m.Id, m.DisplayName)).ToList();

        var supportsEffort = DoesAgentSupportEffort(agentRunner, selectedAgent.Value);
        var currentEffortOptions = GetEffortsForAgentAndModel(agentRunner, selectedAgent.Value, effectiveModel);
        var effectiveEffort = currentEffortOptions.Any(e => e.Id.Equals(selectedEffort.Value, StringComparison.OrdinalIgnoreCase))
            ? selectedEffort.Value
            : "default";

        // Compact DTO serialization: only serialize full message history for the active session,
        // preventing massive SignalR payload bloat when a user has hundreds of sessions.
        var sessionDtos = sessions.Select(s =>
        {
            var isGenerating = executionService.IsGenerating(s.Id);
            var status = isGenerating ? "generating" : "done";
            var isActive = s.Id == currentSessionId;

            return new ChatSessionDto(
                s.Id,
                s.Title,
                s.AgentId,
                s.ModelId,
                s.CreatedAt.ToString("o"),
                s.UpdatedAt.ToString("o"),
                isActive
                    ? s.Messages.Select(m => new ChatMessageDto(
                        m.Id,
                        m.Role,
                        m.Content,
                        m.Timestamp.ToString("t"),
                        m.AgentId,
                        m.ModelId,
                        m.RawStream,
                        m.Effort
                    )).ToList()
                    : [],
                status,
                s.Effort
            );
        }).ToList();

        void SendMessage(ChatSendMessageDto dto)
        {
            var userPrompt = dto.Prompt?.Trim() ?? string.Empty;
            var attachments = dto.Attachments ?? [];
            if (string.IsNullOrWhiteSpace(userPrompt) && attachments.Count == 0) return;

            string targetSessionId = !string.IsNullOrEmpty(dto.SessionId) ? dto.SessionId : (activeSessionId.Value ?? string.Empty);
            if (string.IsNullOrEmpty(targetSessionId))
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, effectiveModel, effort: effectiveEffort);
                targetSessionId = newSess.Id;
                SelectSession(targetSessionId);
            }

            sessionVersion.Set(v => v + 1);
            streamVersion.Set(v => v + 1);

            _ = executionService.SendMessageAsync(
                targetSessionId,
                userPrompt,
                attachments,
                selectedAgent.Value,
                effectiveModel,
                effectiveEffort);
        }

        if (!initialHandled.Value && !string.IsNullOrEmpty(args?.Prompt))
        {
            initialHandled.Value = true;
            var targetId = activeSessionId.Value;
            if (string.IsNullOrEmpty(targetId))
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, effectiveModel, effort: effectiveEffort);
                targetId = newSess.Id;
                SelectSession(targetId);
            }
            SendMessage(new ChatSendMessageDto(args.Prompt, null, targetId));
        }

        var sidebar = new SidebarView(
            sessions,
            activeSessionId,
            sessionVersion,
            selectedAgent,
            selectedModel,
            selectedEffort,
            searchState,
            chatService,
            SelectSession
        );

        var content = new ContentView(
            activeSession,
            activeSessionId,
            sessionVersion,
            selectedAgent,
            selectedModel,
            selectedEffort,
            sessionDtos,
            agentDtos,
            modelDtos,
            currentEffortOptions,
            supportsEffort,
            isSessionGenerating,
            streamSnapshot,
            chatService,
            executionService,
            agentRunner,
            SendMessage,
            SelectSession
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
