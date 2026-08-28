namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     Thrown when a revision contains a malformed <c>questions</c> block. Carries every error at
///     once — the caller is an agent that has to fix them all in one edit — and renders them one per
///     line so the message can go straight to stderr or an API response.
/// </summary>
public class QuestionValidationException(IReadOnlyList<QuestionIssue> issues)
    : Exception(FormatMessage(issues))
{
    /// <summary>Every error that blocked the write, in document order.</summary>
    public IReadOnlyList<QuestionIssue> Issues { get; } = issues;

    private static string FormatMessage(IReadOnlyList<QuestionIssue> issues)
    {
        var header = issues.Count == 1
            ? "Invalid questions block:"
            : $"Invalid questions blocks ({issues.Count} errors):";

        return string.Join(Environment.NewLine, new[] { header }.Concat(issues.Select(i => i.ToString())));
    }
}
