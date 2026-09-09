using System.Reactive.Disposables;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Views;

/// <summary>
///     The chat panel beside a plan: the plan's own session, hosted by the same content view the
///     Chat app uses. The session is created on the first message, so opening the panel to look
///     leaves nothing behind.
/// </summary>
public class PlanChatView(PlanFile plan) : ViewBase
{
    public const string Headline = "Ask Tendril to Change Anything";

    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var chatService = UseService<IChatHistoryService>();
        var executionService = UseService<IChatExecutionService>();
        var agentRunner = UseService<IAgentRunner>();
        Context.TryUseService<IPlanReaderService>(out var planService);
        Context.TryUseService<IJobService>(out var jobService);

        var sessionVersion = UseState(0);
        var streamVersion = UseState(0);
        var activeSessionId = UseState<string?>(() => PlanChatSessions.FindForPlan(chatService, plan)?.Id);
        var syncedSessionId = UseState<string?>(() => activeSessionId.Value);
        var selectedAgent = UseState(() => DefaultAgent(chatService.GetSession(activeSessionId.Value ?? "")));
        var selectedModel = UseState(() => DefaultModel(chatService.GetSession(activeSessionId.Value ?? "")));
        var selectedEffort = UseState(() => chatService.GetSession(activeSessionId.Value ?? "")?.Effort ?? "default");

        UseEffect(() =>
        {
            void OnSessionsChanged(object? sender, EventArgs e) => sessionVersion.Set(v => v + 1);
            void OnGeneratingChanged(object? sender, EventArgs e) => sessionVersion.Set(v => v + 1);

            void OnStreamUpdated(string sessId)
            {
                if (string.Equals(sessId, activeSessionId.Value, StringComparison.OrdinalIgnoreCase))
                    streamVersion.Set(v => v + 1);
            }

            void OnSessionGeneratingChanged(string sessId)
            {
                if (string.Equals(sessId, activeSessionId.Value, StringComparison.OrdinalIgnoreCase))
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

            return Disposable.Create(() =>
            {
                chatService.SessionsChanged -= OnSessionsChanged;
                chatService.GeneratingSessionsChanged -= OnGeneratingChanged;
                executionService.StreamUpdated -= OnStreamUpdated;
                executionService.SessionGeneratingChanged -= OnSessionGeneratingChanged;
                if (jobService != null) jobService.JobsChanged -= OnJobsChanged;
            });
        });

        _ = sessionVersion.Value;
        _ = streamVersion.Value;

        // The panel follows the selected plan: a switch lands on that plan's session, or on none.
        var session = PlanChatSessions.FindForPlan(chatService, plan);
        if (!string.Equals(session?.Id, activeSessionId.Value, StringComparison.OrdinalIgnoreCase))
        {
            activeSessionId.Set(session?.Id);
            chatService.ClearSessionCompleted(session?.Id ?? "");
        }

        if (!string.Equals(session?.Id, syncedSessionId.Value, StringComparison.OrdinalIgnoreCase))
        {
            syncedSessionId.Set(session?.Id);
            selectedAgent.Set(DefaultAgent(session));
            selectedModel.Set(DefaultModel(session));
            selectedEffort.Set(session?.Effort ?? "default");
        }

        var agentId = session?.AgentId ?? selectedAgent.Value;
        var modelOptions = ChatApp.GetModelsForAgent(agentRunner, agentId);
        var effectiveModel = modelOptions.Any(m => m.Id.Equals(selectedModel.Value, StringComparison.OrdinalIgnoreCase))
            ? selectedModel.Value
            : modelOptions.Count > 0 ? modelOptions[0].Id : selectedModel.Value;
        var modelDtos = modelOptions.Select(m => new ModelOptionDto(m.Id, m.DisplayName)).ToList();
        var supportsEffort = ChatApp.DoesAgentSupportEffort(agentRunner, agentId);
        var effortOptions = ChatApp.GetEffortsForAgentAndModel(agentRunner, agentId, effectiveModel);
        var effectiveEffort = effortOptions.Any(e => e.Id.Equals(selectedEffort.Value, StringComparison.OrdinalIgnoreCase))
            ? selectedEffort.Value
            : "default";

        var isGenerating = session != null && executionService.IsGenerating(session.Id);
        var streamSnapshot = isGenerating ? executionService.GetStreamSnapshot(session!.Id) : string.Empty;
        var sessionDtos = session != null
            ? [ChatApp.ToSessionDto(session, true, executionService, jobService, chatService)]
            : new List<ChatSessionDto>();

        void SendMessage(ChatSendMessageDto dto)
        {
            var prompt = dto.Prompt?.Trim() ?? string.Empty;
            var attachments = dto.Attachments ?? [];
            if (string.IsNullOrWhiteSpace(prompt) && attachments.Count == 0) return;

            var targetSessionId = session?.Id;
            if (string.IsNullOrEmpty(targetSessionId))
            {
                var created = PlanChatSessions.CreateForPlan(
                    chatService, planService, plan, selectedAgent.Value, effectiveModel, effectiveEffort);
                targetSessionId = created.Id;
                activeSessionId.Set(targetSessionId);
            }

            if (dto.ForceSend)
                _ = executionService.ForceSendMessageAsync(targetSessionId, prompt, attachments, selectedAgent.Value, effectiveModel, effectiveEffort);
            else
                _ = executionService.SendMessageAsync(targetSessionId, prompt, attachments, selectedAgent.Value, effectiveModel, effectiveEffort);

            sessionVersion.Set(v => v + 1);
            streamVersion.Set(v => v + 1);
        }

        return new Chat.ContentView(
            session,
            activeSessionId,
            sessionVersion,
            selectedAgent,
            selectedModel,
            selectedEffort,
            sessionDtos,
            ChatApp.BuildAgentDtos(agentRunner, configService),
            modelDtos,
            effortOptions,
            supportsEffort,
            isGenerating,
            streamSnapshot,
            $"#{plan.Id} {plan.Title}",
            Headline,
            chatService,
            executionService,
            agentRunner,
            SendMessage,
            id => activeSessionId.Set(id),
            embedded: true);

        string DefaultAgent(ChatSessionModel? sess) =>
            sess?.AgentId ?? configService.Settings.CodingAgent ?? "claude";

        string DefaultModel(ChatSessionModel? sess)
        {
            if (!string.IsNullOrEmpty(sess?.ModelId)) return sess.ModelId;
            var models = ChatApp.GetModelsForAgent(agentRunner, DefaultAgent(sess));
            return models.Count > 0 ? models[0].Id : "default";
        }
    }
}
