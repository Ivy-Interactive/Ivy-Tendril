using System.Text;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Apps.Views.Dialogs;

public class DirtyRepoDialog(
    IState<bool> dialogOpen,
    PreflightResult preflightResult,
    string proceedLabel,
    string contextMessage,
    Action<UntrackedChangesPolicy> onSyncRepos,
    Action onProceed) : ViewBase
{
    private const int MaxItemsShown = 3;

    public override object? Build()
    {
        // Hooks must run unconditionally before any early return so state survives re-renders.
        var showPolicy = UseState(false);

        if (!dialogOpen.Value) return null;

        var hasUncommitted = HasReason(DirtyReason.UncommittedChanges);
        var hasUntracked = HasReason(DirtyReason.UntrackedFiles);

        if (showPolicy.Value)
            return BuildPolicyDialog(hasUncommitted, hasUntracked);

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
                | new Button(proceedLabel).Primary().OnClick(() =>
                {
                    dialogOpen.Set(false);
                    onProceed();
                })
                | new Button("Sync Repos").Primary().Icon(Icons.RefreshCw).OnClick(() =>
                {
                    // If there is local work to reconcile, ask how to handle it; otherwise
                    // (only ahead-of-origin / branch-mismatch etc.) sync straight away.
                    if (hasUncommitted || hasUntracked)
                    {
                        showPolicy.Set(true);
                    }
                    else
                    {
                        dialogOpen.Set(false);
                        onSyncRepos(UntrackedChangesPolicy.Stash);
                    }
                })
            )
        );
    }

    private object BuildPolicyDialog(bool hasUncommitted, bool hasUntracked)
    {
        // Each repo syncs to its own base branch; only name a specific branch when they all
        // share one, otherwise refer to them generically (never fabricate a joined name).
        var branches = preflightResult.DirtyRepos.Select(r => r.BaseBranch).Distinct().ToList();
        var target = branches.Count == 1 ? $"`{branches[0]}`" : "each repo's base branch";

        var subject = (hasUncommitted, hasUntracked) switch
        {
            (true, true) => "your uncommitted changes and untracked files",
            (false, true) => "your untracked files",
            _ => "your uncommitted changes"
        };

        var description =
            $"How should SyncRepo handle {subject}. " +
            $"Commits will be pushed to {target} and merge issues will be resolved.";

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("SyncRepo"),
            new DialogBody(Text.Markdown(description)),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
                | new Button("Stash Changes").Primary().Icon(Icons.Archive)
                    .OnClick(() => SyncWith(UntrackedChangesPolicy.Stash))
                | new Button("Commit and Push").Primary().Icon(Icons.GitCommitHorizontal)
                    .OnClick(() => SyncWith(UntrackedChangesPolicy.Commit))
                | new Button("Create PR").Primary().Icon(Icons.GitPullRequest)
                    .OnClick(() => SyncWith(UntrackedChangesPolicy.PullRequest))
            )
        ).Width(Size.Rem(40));

        void SyncWith(UntrackedChangesPolicy policy)
        {
            dialogOpen.Set(false);
            onSyncRepos(policy);
        }
    }

    private bool HasReason(DirtyReason reason) =>
        preflightResult.DirtyRepos.Any(r => r.DirtyState.Reasons.Any(x => x.Reason == reason));

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
