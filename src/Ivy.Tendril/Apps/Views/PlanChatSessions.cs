using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Apps.Views;

/// <summary>
///     The chat session that belongs to a plan. A session belongs to exactly one plan, recorded on
///     the session itself; the plan's own <c>chatSessionId</c> may still point at the general chat
///     that created it, which is why the session's record wins.
/// </summary>
internal static class PlanChatSessions
{
    public static bool BelongsTo(ChatSessionModel session, PlanFile plan) =>
        string.Equals(session.PlanFolderName, plan.FolderName, StringComparison.OrdinalIgnoreCase);

    public static ChatSessionModel? FindForPlan(IChatHistoryService chatService, PlanFile plan)
    {
        if (!string.IsNullOrEmpty(plan.ChatSessionId))
        {
            var attached = chatService.GetSession(plan.ChatSessionId);
            if (attached != null && BelongsTo(attached, plan))
                return attached;
        }

        return chatService.GetSessions().FirstOrDefault(s => BelongsTo(s, plan));
    }

    /// <summary>
    ///     Starts the plan's session. The plan learns the session id when it has none yet, so jobs
    ///     launched from the page report their completion into this chat.
    /// </summary>
    public static ChatSessionModel CreateForPlan(
        IChatHistoryService chatService,
        IPlanReaderService? planService,
        PlanFile plan,
        string agentId,
        string modelId,
        string? effort)
    {
        var session = chatService.CreateSession(
            agentId, modelId, title: $"#{plan.Id} {plan.Title}", effort: effort, planFolderName: plan.FolderName);

        if (string.IsNullOrEmpty(plan.ChatSessionId))
            planService?.SetChatSessionId(plan.FolderName, session.Id);

        return session;
    }

    /// <summary>
    ///     Sends a message into the plan's chat, starting the session with the configured agent
    ///     when the plan has none yet. Returns the session id the panel will show.
    /// </summary>
    public static string Send(
        IChatHistoryService chatService,
        IChatExecutionService executionService,
        IPlanReaderService? planService,
        IAgentRunner agentRunner,
        IConfigService config,
        PlanFile plan,
        string prompt)
    {
        var session = FindForPlan(chatService, plan);
        if (session == null)
        {
            var agentId = config.Settings.CodingAgent ?? "claude";
            var models = ChatApp.GetModelsForAgent(agentRunner, agentId);
            session = CreateForPlan(chatService, planService, plan, agentId, models.Count > 0 ? models[0].Id : "default", null);
        }

        _ = executionService.SendMessageAsync(session.Id, prompt, null, session.AgentId, session.ModelId, session.Effort);
        return session.Id;
    }

    /// <summary>The opening message of "Discuss with {agent}", phrased for where the plan is.</summary>
    public static string DiscussPrompt(PlanFile plan) =>
        plan.Status is PlanStatus.Review or PlanStatus.Completed or PlanStatus.Failed
            ? "I want to discuss the outcome of this plan before completing it. Summarize what was done and point out anything worth a closer look."
            : "I want to discuss this plan before executing it. Summarize it and point out anything you would change.";
}
