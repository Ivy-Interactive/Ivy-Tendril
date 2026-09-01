using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Shared "move this plan to Completed" action for the UI buttons and menu items. Swallows
///     <see cref="PlanTransitionBlockedException" /> (see plan 00090) and hands the failed gate names
///     back to the caller, because these all run in fire-and-forget event handlers where an unhandled
///     throw would surface as a silent no-op.
/// </summary>
public static class PlanCompletionAction
{
    /// <summary>
    ///     Attempts the transition to Completed. Returns <c>null</c> on success, or the names of the
    ///     verifications that blocked it, in which case nothing changed and the caller must skip its
    ///     follow-up work (refresh, worktree cleanup) rather than pretend the plan is done.
    /// </summary>
    public static IReadOnlyList<string>? TryComplete(IPlanReaderService planService, PlanFile plan)
    {
        try
        {
            planService.TransitionState(plan.FolderName, PlanStatus.Completed);
            return null;
        }
        catch (PlanTransitionBlockedException ex)
        {
            return ex.FailedVerifications;
        }
    }

    /// <summary>
    ///     Reports a blocked transition where there is no override affordance (i.e. everywhere except
    ///     the Review app, which offers a partial-delivery confirmation dialog instead).
    /// </summary>
    public static void ToastBlocked(IClientProvider client, PlanFile plan, IReadOnlyList<string> failed) =>
        client.Toast(
            failed.Count > 0
                ? $"{string.Join(", ", failed)} failed for plan #{plan.Id}. Re-run the verification, or set it " +
                  "to Skipped with a reason, before completing the plan."
                : $"Pre-execution validation failed for plan #{plan.Id} and no changes were implemented. " +
                  "Retire it by setting the state to Skipped instead.",
            failed.Count > 0 ? "Verification Failed" : "Cannot Complete Plan",
            variant: ToastVariant.Destructive);

    /// <summary>
    ///     Transitions a plan to Skipped and initiates background worktree cleanup.
    /// </summary>
    public static void Skip(IPlanReaderService planService, PlanFile plan)
    {
        planService.TransitionState(plan.FolderName, PlanStatus.Skipped);
        WorktreeCleanupService.RemoveWorktreesInBackground(plan.FolderPath);
    }
}
