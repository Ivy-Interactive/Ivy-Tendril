using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Services;

public interface IChatExecutionService : IDisposable
{
    event Action<string>? SessionGeneratingChanged;
    event Action<string>? StreamUpdated;
    bool IsGenerating(string sessionId);
    string GetStreamSnapshot(string sessionId);
    string? GetStreamingMessageId(string sessionId);
    IObservable<string> GetLiveStreamObservable(string sessionId);
    Task SendMessageAsync(
        string sessionId,
        string prompt,
        IReadOnlyList<ChatAttachmentDto>? attachments = null,
        string? agentId = null,
        string? modelId = null,
        string? effort = null,
        string role = "user",
        CancellationToken ct = default);
    Task CancelAsync(string sessionId);
    Task InterruptAsync(string sessionId);

    /// <summary>
    ///     Patches the buffer of an in-progress execution so a mid-run question answer survives the
    ///     next persist tick, which otherwise overwrites the stored message's <c>Content</c>/
    ///     <c>RawStream</c> from this same buffer roughly once a second. No-ops when
    ///     <paramref name="sessionId"/> has no active execution.
    /// </summary>
    void ApplyQuestionAnswers(string sessionId, IReadOnlyDictionary<string, string[]> answers);

    /// <summary>
    ///     Announces a direct edit to a plan — one made without a job, from a chat or the CLI — to
    ///     every chat session attached to that plan, so a session that spawned the plan and still
    ///     believes it knows what the plan says learns otherwise.
    /// </summary>
    /// <param name="planFolderName">Folder name of the edited plan, e.g. <c>00123-AddRetries</c>.</param>
    /// <param name="summary">What changed, e.g. <c>Solution changed (+12/-3 lines)</c>.</param>
    /// <param name="reason">Why it changed. The event omits the clause when this is absent.</param>
    /// <param name="sourceChatSessionId">
    ///     The session that made the edit, which is excluded from the fan-out.
    /// </param>
    /// <param name="revisionFile">
    ///     The revision file written, e.g. <c>004.md</c>. Deduplicates a retried report; a summary
    ///     fingerprint stands in when the edit wrote no revision.
    /// </param>
    /// <param name="origin">
    ///     Where the edit originated from. Defaults to <see cref="PlanEditOrigin.Chat"/>.
    /// </param>
    Task NotifyPlanEditAsync(
        string planFolderName,
        string summary,
        string? reason = null,
        string? sourceChatSessionId = null,
        string? revisionFile = null,
        PlanEditOrigin origin = PlanEditOrigin.Chat);
    Task ForceSendMessageAsync(
        string sessionId,
        string prompt,
        IReadOnlyList<ChatAttachmentDto>? attachments = null,
        string? agentId = null,
        string? modelId = null,
        string? effort = null,
        CancellationToken ct = default);
}
