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
}
