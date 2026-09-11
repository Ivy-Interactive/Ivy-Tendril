using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using Ivy;
using Ivy.Core;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Chat.Dialogs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Chat;

[App(title: "Chat", icon: Icons.MessageSquare, group: ["Apps"], order: Constants.Chat, isVisible: false, allowDuplicateTabs: false)]
public class ChatApp : ViewBase
{
    internal static string DisplayTitle(ChatSessionModel session) =>
        string.IsNullOrWhiteSpace(session.Title) ? "New Chat" : session.Title;

    /// <summary>The plan a session belongs to, shown as its row tag; null for a free-standing chat.</summary>
    internal static string? PlanTag(ChatSessionModel session) =>
        string.IsNullOrEmpty(session.PlanFolderName) ? null : $"#{TendrilAppShell.FormatPlanId(session.PlanFolderName)}";

    internal static ChatJobDto ToJobDto(JobItem job, IPlanReaderService? planService = null)
    {
        var planId = job.ResolvePlanId();
        if (string.IsNullOrEmpty(planId) && !string.IsNullOrEmpty(job.PlanFile))
        {
            var extracted = PlanYamlHelper.ExtractPlanIdFromFolder(job.PlanFile);
            if (!string.IsNullOrEmpty(extracted)) planId = extracted;
        }
        if (string.IsNullOrEmpty(planId) && !string.IsNullOrEmpty(job.TypedArgs?.PlanFolder))
        {
            var extracted = PlanYamlHelper.ExtractPlanIdFromFolder(job.TypedArgs.PlanFolder);
            if (!string.IsNullOrEmpty(extracted)) planId = extracted;
        }
        var resolvedPlanId = string.IsNullOrEmpty(planId) ? null : planId;

        var planTitle = job.ReportedPlanTitle;
        if (string.IsNullOrWhiteSpace(planTitle) && resolvedPlanId != null && planService != null)
        {
            planTitle = ContentView.FindPlan(planService, resolvedPlanId)?.Title;
        }
        if (string.IsNullOrWhiteSpace(planTitle) && !string.IsNullOrEmpty(job.PlanFile))
        {
            planTitle = PlanYamlHelper.ExtractSafeTitleFromFolder(job.PlanFile);
        }
        if (string.IsNullOrWhiteSpace(planTitle) && !string.IsNullOrEmpty(job.TypedArgs?.PlanFolder))
        {
            planTitle = PlanYamlHelper.ExtractSafeTitleFromFolder(job.TypedArgs.PlanFolder);
        }
        var resolvedPlanTitle = string.IsNullOrWhiteSpace(planTitle) ? null : planTitle;

        return new(
            job.Id,
            job.Type,
            job.Status.ToString(),
            resolvedPlanId,
            resolvedPlanTitle,
            job.StatusMessage,
            Constants.JobTypeColors.TryGetValue(job.Type, out var color) ? color.ToString() : null);
    }

    internal static string? BuildRowState(
        ChatSessionModel session,
        string? selectedId,
        IReadOnlySet<string> generatingIds,
        IReadOnlySet<string> completedIds)
    {
        if (generatingIds.Contains(session.Id)) return "working";
        if (completedIds.Contains(session.Id) && session.Id != selectedId) return "completed";
        return null;
    }

    /// <summary>
    ///     The Chats list the shell sidebar shows while this app (or a terminal session) is open.
    ///     Terminal sessions are listed too; the shell reroutes their selection to the terminal pane.
    /// </summary>
    internal static ShellSidebarListState BuildSidebarList(
        IReadOnlyList<ChatSessionModel> sessions,
        string? selectedId,
        IReadOnlySet<string> generatingIds,
        IReadOnlySet<string> completedIds,
        Action openSearch,
        Action? startNewChat = null,
        Action<string, string>? renameSession = null,
        Action<string>? deleteSession = null)
    {
        var items = sessions
            .Select(s => new ShellSectionItemDto(
                s.Id,
                DisplayTitle(s),
                PlanTag(s),
                Icon: s.IsTerminal() ? "Terminal" : null,
                State: BuildRowState(s, selectedId, generatingIds, completedIds)))
            .ToList();
        return new ShellSidebarListState(
            "chat", "Chats", items, selectedId,
            id => new ChatAppArgs(SessionId: id),
            Searchable: true,
            OnSearch: openSearch,
            SearchLabel: "Search chats",
            OnNew: startNewChat,
            NewLabel: startNewChat != null ? "New chat" : null,
            CollapsedMenu: true,
            OnRename: renameSession,
            OnDelete: deleteSession);
    }

    public override object Build()
    {
        var args = UseArgs<ChatAppArgs>();
        var configService = UseService<IConfigService>();
        var chatService = UseService<IChatHistoryService>();
        var executionService = UseService<IChatExecutionService>();
        var agentRunner = UseService<IAgentRunner>();
        Context.TryUseService<IJobService>(out var jobService);
        Context.TryUseService<IPlanReaderService>(out var planService);
        Context.TryUseService<IChatAgentPreferences>(out var preferences);
        var navigator = UseNavigation();
        var sidebarListSignal = Context.UseSignal<ShellSidebarListSignal, ShellSidebarListState, Unit>();
        var activeSessionId = UseState<string?>(() => InitialSession()?.Id);
        var sessionVersion = UseState(0);
        var deletingSessionId = UseState<string?>(null);
        var selectedAgent = UseState(() =>
        {
            var sess = InitialSession();
            if (!string.IsNullOrEmpty(sess?.AgentId)) return sess.AgentId;
            var lastAgent = configService.Settings.LastChatAgent;
            if (!string.IsNullOrEmpty(lastAgent) && agentRunner.RegisteredAgents.Any(a => string.Equals(a, lastAgent, StringComparison.OrdinalIgnoreCase)))
            {
                return agentRunner.RegisteredAgents.First(a => string.Equals(a, lastAgent, StringComparison.OrdinalIgnoreCase));
            }
            return configService.Settings.CodingAgent ?? "claude";
        });
        var selectedModel = UseState(() =>
        {
            var sess = InitialSession();
            if (!string.IsNullOrEmpty(sess?.ModelId)) return sess.ModelId;
            return ResolveModel(
                GetModelsForAgent(agentRunner, selectedAgent.Value),
                preferences?.Get(selectedAgent.Value).ModelId,
                configService.Settings.LastChatModel);
        });
        var selectedEffort = UseState(() =>
        {
            var sess = InitialSession();
            if (sess != null) return sess.Effort ?? "default";
            return preferences?.Get(selectedAgent.Value).Effort ?? configService.Settings.LastChatEffort ?? "default";
        });
        var initialHandled = UseRef(false);
        var streamVersion = UseState(0);
        var (searchDialog, showSearchDialog) = UseTrigger(isOpen =>
        {
            if (!isOpen.Value) return null;
            return new ChatSearchDialog(isOpen, chatService, SelectSession);
        });

        UseEffect(() =>
        {
            void OnSessionsChanged(object? sender, EventArgs e) => sessionVersion.Set(v => v + 1);

            void OnGeneratingChanged(object? sender, EventArgs e) => sessionVersion.Set(v => v + 1);

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
                    streamVersion.Set(v => v + 1);
                    sessionVersion.Set(v => v + 1);
                }
            }

            void OnJobsChanged() => sessionVersion.Set(v => v + 1);

            chatService.SessionsChanged += OnSessionsChanged;
            chatService.GeneratingSessionsChanged += OnGeneratingChanged;
            executionService.StreamUpdated += OnStreamUpdated;
            executionService.SessionGeneratingChanged += OnSessionGeneratingChanged;
            if (jobService != null) jobService.JobsChanged += OnJobsChanged;

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
                if (jobService != null) jobService.JobsChanged -= OnJobsChanged;
                chatService.PruneEmptySessions();
            });
        });

        // The chat session the args name, when it still exists; a bare prompt starts a fresh chat;
        // otherwise the most recent chat. Terminal sessions belong to the AgentApp pane, never here.
        ChatSessionModel? InitialSession()
        {
            if (!string.IsNullOrEmpty(args?.SessionId))
            {
                var named = chatService.GetSession(args.SessionId);
                if (named != null && !named.IsTerminal()) return named;
            }
            if (!string.IsNullOrEmpty(args?.Prompt)) return null;
            return chatService.GetSessions().FirstOrDefault(s => !s.IsTerminal());
        }

        void SelectSession(string sessionId)
        {
            var sess = chatService.GetSession(sessionId);
            if (sess?.IsTerminal() == true)
            {
                navigator.Navigate(typeof(AgentApp), new AgentAppArgs(SessionId: sessionId));
                return;
            }
            chatService.PruneEmptySessions(activeSessionId: sessionId);
            activeSessionId.Set(sessionId);
            chatService.ClearSessionCompleted(sessionId);
            if (sess != null)
            {
                if (!string.IsNullOrEmpty(sess.AgentId)) selectedAgent.Set(sess.AgentId);
                if (!string.IsNullOrEmpty(sess.ModelId)) selectedModel.Set(sess.ModelId);
                if (!string.IsNullOrEmpty(sess.Effort)) selectedEffort.Set(sess.Effort);
            }
            // The shell keys this page on its args, so the selection is also a navigation: the
            // sidebar row and the browser URL follow, and re-opening the app lands on this session.
            navigator.Navigate(typeof(ChatApp), new ChatAppArgs(SessionId: sessionId));
        }

        _ = sessionVersion.Value;
        _ = streamVersion.Value;
        var allSessions = chatService.GetSessions();
        var sessions = allSessions.Where(s => !s.IsTerminal()).ToList();
        var currentSessionId = activeSessionId.Value;
        var activeSession = currentSessionId != null ? chatService.GetSession(currentSessionId) : null;
        var isSessionGenerating = currentSessionId != null && executionService.IsGenerating(currentSessionId);
        var streamSnapshot = isSessionGenerating ? executionService.GetStreamSnapshot(currentSessionId!) : string.Empty;

        var currentModelOptions = GetModelsForAgent(agentRunner, selectedAgent.Value);
        var effectiveModel = ResolveModel(currentModelOptions, selectedModel.Value);
        var modelDtos = currentModelOptions.Select(m => new ModelOptionDto(m.Id, m.DisplayName)).ToList();

        var supportsEffort = DoesAgentSupportEffort(agentRunner, selectedAgent.Value);
        var currentEffortOptions = GetEffortsForAgentAndModel(agentRunner, selectedAgent.Value, effectiveModel);
        var effectiveEffort = ResolveEffort(currentEffortOptions, selectedEffort.Value);

        var agentDtos = BuildAgentDtos(agentRunner, configService, preferences, selectedAgent.Value, effectiveModel, effectiveEffort);

        // Compact DTO serialization: only serialize full message history for the active session,
        // preventing massive SignalR payload bloat when a user has hundreds of sessions.
        var sessionDtos = sessions
            .Select(s => ToSessionDto(s, s.Id == currentSessionId, executionService, jobService, chatService, planService))
            .ToList();

        void StartNewChat()
        {
            if (ChatLauncher.UsesTerminal(configService))
            {
                navigator.Navigate(typeof(AgentApp), new AgentAppArgs());
                return;
            }
            chatService.PruneEmptySessions();
            var newSess = chatService.CreateSession(selectedAgent.Value, effectiveModel, effort: effectiveEffort, kind: ChatSessionKinds.Chat);
            SelectSession(newSess.Id);
        }

        void SendMessage(ChatSendMessageDto dto)
        {
            var userPrompt = dto.Prompt?.Trim() ?? string.Empty;
            var attachments = dto.Attachments ?? [];
            if (string.IsNullOrWhiteSpace(userPrompt) && attachments.Count == 0) return;

            string targetSessionId = !string.IsNullOrEmpty(dto.SessionId) ? dto.SessionId : (activeSessionId.Value ?? string.Empty);
            if (string.IsNullOrEmpty(targetSessionId))
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, effectiveModel, effort: effectiveEffort, kind: ChatSessionKinds.Chat);
                targetSessionId = newSess.Id;
                SelectSession(targetSessionId);
            }

            if (dto.ForceSend)
            {
                _ = executionService.ForceSendMessageAsync(
                    targetSessionId,
                    userPrompt,
                    attachments,
                    selectedAgent.Value,
                    effectiveModel,
                    effectiveEffort);
            }
            else
            {
                _ = executionService.SendMessageAsync(
                    targetSessionId,
                    userPrompt,
                    attachments,
                    selectedAgent.Value,
                    effectiveModel,
                    effectiveEffort);
            }

            sessionVersion.Set(v => v + 1);
            streamVersion.Set(v => v + 1);
        }

        if (!initialHandled.Value && !string.IsNullOrEmpty(args?.Prompt))
        {
            initialHandled.Value = true;
            var targetId = activeSessionId.Value;
            if (string.IsNullOrEmpty(targetId))
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, effectiveModel, effort: effectiveEffort, kind: ChatSessionKinds.Chat);
                targetId = newSess.Id;
                SelectSession(targetId);
            }
            SendMessage(new ChatSendMessageDto(args.Prompt, null, targetId));
        }

        _ = sidebarListSignal.Send(BuildSidebarList(
            allSessions,
            currentSessionId,
            chatService.GetGeneratingSessionIds(),
            chatService.GetCompletedSessionIds(),
            showSearchDialog,
            StartNewChat,
            (id, title) =>
            {
                chatService.RenameSession(id, title);
                sessionVersion.Set(v => v + 1);
            },
            id => deletingSessionId.Set(id)));

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
            DashboardApp.BuildGreeting(DateTime.Now),
            "What Are We Producing Today?",
            chatService,
            executionService,
            agentRunner,
            SendMessage,
            SelectSession,
            StartNewChat,
            sharedDeletingSessionId: deletingSessionId
        );

        return new Fragment(content, searchDialog);
    }

    /// <summary>
    ///     A session as the widget wants it. Only the active session carries its messages, which
    ///     keeps the payload small for a user with hundreds of sessions.
    /// </summary>
    internal static ChatSessionDto ToSessionDto(
        ChatSessionModel s,
        bool isActive,
        IChatExecutionService executionService,
        IJobService? jobService,
        IChatHistoryService chatService,
        IPlanReaderService? planService = null)
    {
        var isGenerating = executionService.IsGenerating(s.Id);
        var status = isGenerating ? "generating" : "done";

        var messages = s.Messages;
        if (isGenerating && messages.Count > 0 && messages[^1].Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
        {
            // Omit in-progress assistant message while generating to prevent duplicate rendering with live stream
            messages = messages.Take(messages.Count - 1).ToList();
        }

        List<ChatJobDto>? spawnedJobs = null;
        if (jobService != null)
        {
            var combinedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matchingJobs = jobService.GetJobs().Where(j => string.Equals(j.ChatSessionId, s.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var mj in matchingJobs)
            {
                if (combinedIds.Add(mj.Id))
                {
                    chatService.AddSpawnedJob(s.Id, mj.Id);
                }
            }

            if (s.SpawnedJobIds is { Count: > 0 } jIds)
            {
                var staleIds = new List<string>();
                foreach (var id in jIds)
                {
                    var job = jobService.GetJob(id);
                    if (job != null)
                    {
                        if (string.Equals(job.ChatSessionId, s.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            combinedIds.Add(id);
                        }
                        else
                        {
                            staleIds.Add(id);
                        }
                    }
                }

                if (staleIds.Count > 0)
                {
                    chatService.RemoveSpawnedJobs(s.Id, staleIds);
                }
            }

            if (combinedIds.Count > 0)
            {
                spawnedJobs = combinedIds.Select(jId =>
                {
                    var job = jobService.GetJob(jId);
                    if (job == null) return new ChatJobDto(jId, "Job", "Unknown");
                    return ToJobDto(job, planService);
                }).ToList();
            }
        }

        return new ChatSessionDto(
            s.Id,
            s.Title,
            s.AgentId,
            s.ModelId,
            s.CreatedAt.ToString("o"),
            s.UpdatedAt.ToString("o"),
            isActive
                ? messages.Select(m => new ChatMessageDto(
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
            s.Effort,
            spawnedJobs
        );
    }

    internal static string ResolveModel(IReadOnlyList<(string Id, string DisplayName)> models, params string?[]? preferred)
    {
        foreach (var candidate in preferred ?? [])
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            var match = models.FirstOrDefault(m => m.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (match.Id != null) return match.Id;
        }
        return models.Count > 0 ? models[0].Id : "default";
    }

    internal static string ResolveEffort(IReadOnlyList<EffortOptionDto> efforts, params string?[]? preferred)
    {
        foreach (var candidate in preferred ?? [])
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            var match = efforts.FirstOrDefault(e => e.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Id;
        }
        return "default";
    }

    /// <summary>
    ///     Every agent carries the model and effort remembered for it, so the picker can show and
    ///     change them without selecting the agent; the selected agent shows the live selection.
    /// </summary>
    internal static List<AgentOptionDto> BuildAgentDtos(
        IAgentRunner agentRunner,
        IConfigService configService,
        IChatAgentPreferences? preferences = null,
        string? selectedAgent = null,
        string? selectedModel = null,
        string? selectedEffort = null)
    {
        var registeredAgentIds = agentRunner.RegisteredAgents;
        if (registeredAgentIds.Count == 0)
        {
            registeredAgentIds = ["claude", "opencode", "codex", "gemini", "antigravity", "copilot", "ivy"];
        }

        return registeredAgentIds.Select(agentId =>
        {
            var (label, icon) = AgentBranding.For(agentId, agentRunner, configService);
            var isSelected = agentId.Equals(selectedAgent, StringComparison.OrdinalIgnoreCase);
            var preference = preferences?.Get(agentId) ?? new ChatAgentPreference();
            var agentModels = GetModelsForAgent(agentRunner, agentId);
            var model = ResolveModel(agentModels, isSelected ? selectedModel : preference.ModelId);
            var agentEfforts = GetEffortsForAgentAndModel(agentRunner, agentId, model);
            var effort = ResolveEffort(agentEfforts, isSelected ? selectedEffort : preference.Effort);
            return new AgentOptionDto(
                agentId,
                label,
                icon.ToString(),
                agentModels.Select(m => new ModelOptionDto(m.Id, m.DisplayName)).ToList(),
                DoesAgentSupportEffort(agentRunner, agentId),
                model,
                effort,
                agentEfforts);
        }).ToList();
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
