namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     Thrown when a plan is asked to move to <see cref="Models.PlanStatus.Completed" /> while one or
///     more of its verifications are in the Fail state. See plan 00090: a plan that reaches Completed
///     over a failed gate becomes invisible to duplicate detection, so the work it never delivered is
///     silently trashed the next time someone asks for it.
/// </summary>
public class PlanTransitionBlockedException(string folderName, IReadOnlyList<string> failedVerifications)
    : Exception(BuildMessage(folderName, failedVerifications))
{
    public string FolderName { get; } = folderName;
    public IReadOnlyList<string> FailedVerifications { get; } = failedVerifications;

    private static string BuildMessage(string folderName, IReadOnlyList<string> failedVerifications) =>
        $"Plan {folderName} cannot be Completed: verification(s) {string.Join(", ", failedVerifications)} failed. " +
        "Re-run them, set them to Skipped with an explicit reason, or use --allow-failed-verifications " +
        "to record deliberate partial delivery.";
}
