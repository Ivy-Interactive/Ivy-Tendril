using System;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Announces a plan edit the user made by hand in the UI. Fire-and-forget and silent on failure, like
///     EmitManualExecutionEvent: the edit is already on disk, and a chat that cannot be told is no reason
///     to fail a button click.
/// </summary>
public static class PlanEditAnnouncer
{
    /// <summary>
    ///     Announces a plan edit made through the UI to all attached chat sessions.
    /// </summary>
    /// <param name="chatExecution">The chat execution service, or null.</param>
    /// <param name="plan">The plan that was edited.</param>
    /// <param name="summary">What changed (e.g., "state set to Skipped").</param>
    /// <param name="revisionFile">Optional revision file name if a revision was written.</param>
    public static void Announce(
        IChatExecutionService? chatExecution,
        PlanFile plan,
        string summary,
        string? revisionFile = null)
    {
        if (chatExecution == null || string.IsNullOrWhiteSpace(summary)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await AnnounceAsync(chatExecution, plan, summary, revisionFile);
            }
            catch
            {
                // Swallow - the edit is already on disk
            }
        });
    }

    /// <summary>
    ///     Internal async implementation for testability.
    /// </summary>
    internal static async Task AnnounceAsync(
        IChatExecutionService? chatExecution,
        PlanFile plan,
        string summary,
        string? revisionFile = null)
    {
        if (chatExecution == null || string.IsNullOrWhiteSpace(summary)) return;

        try
        {
            await chatExecution.NotifyPlanEditAsync(
                plan.FolderName,
                summary,
                reason: null,
                sourceChatSessionId: null,
                revisionFile,
                PlanEditOrigin.UserInterface);
        }
        catch
        {
            // Swallow - the edit is already on disk
        }
    }
}
