using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Apps.Inbox;

/// <summary>
/// Builds the chat prompt sent when the user opens Chat for the issues selected in the Inbox.
/// </summary>
internal static class InboxChatPrompt
{
    /// Issues rendered in full before the prompt collapses the rest into a single line.
    public const int MaxDetailedIssues = 20;

    /// Characters of each issue body kept in the prompt.
    public const int BodyPreviewLength = 500;

    public static string Build(IReadOnlyList<GitHubIssue> issues)
    {
        if (issues.Count == 0) return string.Empty;

        var blocks = new List<string>
        {
            $"Let's discuss {issues.Count} GitHub issue{(issues.Count == 1 ? "" : "s")} I selected in the Tendril Inbox. Read them and help me decide what to do."
        };

        foreach (var issue in issues.Take(MaxDetailedIssues))
        {
            blocks.Add(BuildIssueBlock(issue));
        }

        if (issues.Count > MaxDetailedIssues)
        {
            blocks.Add($"Plus {issues.Count - MaxDetailedIssues} more selected issues, which you can fetch with gh issue view.");
        }

        return string.Join("\n\n", blocks);
    }

    public static string? Title(IReadOnlyList<GitHubIssue> issues) => issues.Count switch
    {
        0 => null,
        1 => $"#{issues[0].Number}",
        _ => $"{issues.Count} issues"
    };

    private static string BuildIssueBlock(GitHubIssue issue)
    {
        var lines = new List<string>
        {
            string.IsNullOrEmpty(issue.Repository)
                ? $"## #{issue.Number}: {issue.Title}"
                : $"## {issue.Repository}#{issue.Number}: {issue.Title}"
        };

        if (InboxApp.ResolveIssueUrl(issue) is { } url)
        {
            lines.Add($"URL: {url}");
        }

        var labels = issue.Labels.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (labels.Length > 0)
        {
            lines.Add($"Labels: {string.Join(", ", labels)}");
        }

        var assignees = issue.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
        if (assignees.Length > 0)
        {
            lines.Add($"Assignees: {string.Join(", ", assignees)}");
        }

        var body = InboxApp.TruncateBody(issue.Body, BodyPreviewLength);
        lines.Add(string.IsNullOrEmpty(body) ? "No description provided." : body);

        return string.Join("\n", lines);
    }
}
