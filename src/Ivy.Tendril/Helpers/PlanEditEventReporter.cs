namespace Ivy.Tendril.Helpers;

/// <summary>
///     Reports a direct plan edit from a CLI process into the running Tendril instance, which fans it
///     out to the chat sessions attached to the plan.
///     <para>
///         Best-effort by design, exactly like <see cref="MasterClient.TryPutJson" /> behind
///         <c>tendril job status</c>: an edit is already on disk by the time this runs, so a missing or
///         restarted master must produce a <c>Warning:</c> line and nothing else. Never a non-zero
///         exit, never a throw.
///     </para>
/// </summary>
public static class PlanEditEventReporter
{
    /// <summary>
    ///     Posts the edit to <c>api/plans/{planId}/events</c>.
    /// </summary>
    /// <param name="planId">Plan id or folder name — the endpoint resolves either.</param>
    /// <param name="summary">What changed, e.g. <c>Solution changed (+12/-3 lines)</c>.</param>
    /// <param name="reason">Why the edit was made, when the caller passed <c>--reason</c>.</param>
    /// <param name="sourceChatSessionId">
    ///     The chat session making the edit, so it is not notified about its own change. Defaults to
    ///     <see cref="ResolveSourceChatSession" />.
    /// </param>
    /// <param name="revisionFile">
    ///     The revision file written, e.g. <c>004.md</c>. Used to deduplicate a retried report.
    /// </param>
    /// <returns>True when the master accepted the event.</returns>
    public static bool Report(
        string planId,
        string summary,
        string? reason = null,
        string? sourceChatSessionId = null,
        string? revisionFile = null,
        CancellationToken cancellationToken = default)
    {
        var (ok, error) = MasterClient.TryPostJson(
            $"api/plans/{planId}/events",
            new
            {
                summary,
                reason,
                sourceChatSessionId = ResolveSourceChatSession(sourceChatSessionId),
                revisionFile
            },
            cancellationToken);

        if (!ok)
            Console.Error.WriteLine($"Warning: could not report the edit to plan {planId}: {error}");

        return ok;
    }

    /// <summary>
    ///     Falls back to <c>TENDRIL_CHAT_SESSION_ID</c> when <c>--chat-session</c> was not passed, the
    ///     same convention as <see cref="Commands.JobStartCommand" />. A chat-launched agent inherits
    ///     that variable (see <c>ChatExecutionService</c>), so the session that made the edit is left
    ///     out of the fan-out even when the agent forgets the option. Job and promptware processes do
    ///     not set it, so their edits still reach every attached session.
    /// </summary>
    internal static string? ResolveSourceChatSession(string? sourceChatSessionId)
    {
        return !string.IsNullOrWhiteSpace(sourceChatSessionId)
            ? sourceChatSessionId
            : Environment.GetEnvironmentVariable("TENDRIL_CHAT_SESSION_ID");
    }

    /// <summary>
    ///     Names <c>--reason</c> when a direct edit arrives without one. Advisory: the event is still
    ///     reported, because an edit nobody can explain still beats an edit nobody hears about.
    /// </summary>
    public static void WarnAboutMissingReason(string? reason)
    {
        if (!string.IsNullOrWhiteSpace(reason))
            return;

        Console.Error.WriteLine(
            "warning: no --reason given for this plan edit. Pass --reason \"<why you changed it>\" so the " +
            "plan's other chat sessions are told why, not just what.");
    }
}
