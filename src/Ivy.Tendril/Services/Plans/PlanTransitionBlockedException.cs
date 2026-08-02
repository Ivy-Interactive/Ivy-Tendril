using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     Thrown by <see cref="PlanReaderService.TransitionState" /> when a plan cannot transition to
///     Completed because either (a) one or more verifications failed (plan 00090), or (b) the plan
///     failed pre-execution validation and has no commits/PRs (plan 00103). Callers are UI actions,
///     so they must catch this and surface <see cref="Message" /> to the user rather than letting
///     the throw disappear into a fire-and-forget handler.
/// </summary>
public class PlanTransitionBlockedException : Exception
{
    /// <summary>
    ///     Creates a block exception for failed verifications (plan 00090).
    /// </summary>
    public PlanTransitionBlockedException(string folderName, IReadOnlyList<string> failedVerifications)
        : base(BuildVerificationMessage(folderName, failedVerifications))
    {
        FolderName = folderName;
        RequestedState = PlanStatus.Completed;
        FailedVerifications = failedVerifications;
    }

    /// <summary>
    ///     Creates a block exception for a general reason (plan 00103 pre-execution block).
    /// </summary>
    public PlanTransitionBlockedException(string folderName, PlanStatus requestedState, string reason)
        : base(reason)
    {
        FolderName = folderName;
        RequestedState = requestedState;
        FailedVerifications = Array.Empty<string>();
    }

    public string FolderName { get; }
    public PlanStatus RequestedState { get; }
    public IReadOnlyList<string> FailedVerifications { get; }

    private static string BuildVerificationMessage(string folderName, IReadOnlyList<string> failedVerifications) =>
        $"Plan {folderName} cannot be Completed: verification(s) {string.Join(", ", failedVerifications)} failed. " +
        "Re-run them, set them to Skipped with an explicit reason, or use --allow-failed-verifications " +
        "to record deliberate partial delivery.";
}
