using System.Text;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Apps.Views.Dialogs;

public class DirtyRepoDialog(
    IState<bool> dialogOpen,
    PreflightResult preflightResult,
    string proceedLabel,
    string contextMessage,
    Action onSyncRepos,
    Action onProceed) : ViewBase
{
    private const int MaxItemsShown = 3;

    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        var md = new StringBuilder();
        var repos = preflightResult.DirtyRepos;
        for (var r = 0; r < repos.Count; r++)
        {
            var (repoPath, baseBranch, dirtyState) = repos[r];
            if (r > 0)
                md.AppendLine();

            md.AppendLine($"**{Path.GetFileName(repoPath)}**").AppendLine();
            md.AppendLine($"`{repoPath}`").AppendLine();

            foreach (var reason in dirtyState.Reasons)
                AppendReason(md, reason, baseBranch);

            md.AppendLine();
            md.AppendLine(contextMessage.Replace("origin/<baseBranch>", $"`origin/{baseBranch}`"));
        }

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Local Changes Detected"),
            new DialogBody(Text.Markdown(md.ToString())),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
                | new Button(proceedLabel).Outline().OnClick(() =>
                {
                    dialogOpen.Set(false);
                    onProceed();
                })
                | new Button("Sync Repos").Primary().Icon(Icons.RefreshCw).OnClick(() =>
                {
                    dialogOpen.Set(false);
                    onSyncRepos();
                })
            )
        ).Width(Size.Rem(40));
    }

    private static void AppendReason(StringBuilder md, DirtyReasonDetail reason, string baseBranch)
    {
        md.AppendLine($"- {SummarizeReason(reason, baseBranch)}");

        // AheadOfOrigin lists commit subjects; every other reason lists file paths.
        var isCommits = reason.Reason == DirtyReason.AheadOfOrigin && reason.Commits.Count > 0;
        var items = isCommits ? reason.Commits : reason.Files;

        var shown = Math.Min(MaxItemsShown, items.Count);
        for (var i = 0; i < shown; i++)
            // File paths (repo-relative, e.g. src/Program.cs) render as inline code. For commits
            // ("<sha> <subject>") only the sha is code; the subject stays plain text.
            md.AppendLine(isCommits ? $"    - {FormatCommit(items[i])}" : $"    - `{items[i]}`");

        if (items.Count > MaxItemsShown)
            md.AppendLine($"    - +{items.Count - MaxItemsShown} more");
    }

    private static string FormatCommit(string commit)
    {
        var space = commit.IndexOf(' ');
        return space <= 0
            ? $"`{commit}`"
            : $"`{commit[..space]}` {commit[(space + 1)..]}";
    }

    private static string SummarizeReason(DirtyReasonDetail detail, string baseBranch) => detail.Reason switch
    {
        DirtyReason.UncommittedChanges => detail.Files.Count == 1
            ? "1 uncommitted change"
            : $"{detail.Files.Count} uncommitted changes",
        DirtyReason.UntrackedFiles => detail.Files.Count == 1
            ? "1 untracked file"
            : $"{detail.Files.Count} untracked files",
        DirtyReason.AheadOfOrigin when detail.Commits.Count > 0 => detail.Commits.Count == 1
            ? $"1 commit ahead of `origin/{baseBranch}`"
            : $"{detail.Commits.Count} commits ahead of `origin/{baseBranch}`",
        DirtyReason.AheadOfOrigin => detail.Message,
        DirtyReason.DetachedHead => "Detached HEAD",
        DirtyReason.NoRemoteConfigured => "No remote configured",
        _ => detail.Message
    };
}
