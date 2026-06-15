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
    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        var body = Layout.Vertical().Gap(3);

        foreach (var (repoPath, baseBranch, dirtyState) in preflightResult.DirtyRepos)
        {
            var repoSection = Layout.Vertical().Gap(1);
            repoSection |= Text.Block(Path.GetFileName(repoPath)).Bold();
            repoSection |= Text.Muted(repoPath);

            foreach (var reason in dirtyState.Reasons)
            {
                repoSection |= Text.Block($"• {FormatReason(reason)}");
            }

            repoSection |= Text.Markdown(contextMessage.Replace("origin/<baseBranch>", $"`origin/{baseBranch}`")).Muted();
            body |= repoSection;
        }

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Local Changes Detected"),
            new DialogBody(body),
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

    private static string FormatReason(DirtyReasonDetail detail) => detail.Reason switch
    {
        DirtyReason.NotOnExpectedBranch => detail.Message,
        DirtyReason.AheadOfOrigin => detail.Message,
        DirtyReason.UncommittedChanges => detail.Message,
        DirtyReason.UntrackedFiles => detail.Message,
        DirtyReason.InProgressOperation => detail.Message,
        DirtyReason.DetachedHead => "Detached HEAD",
        DirtyReason.NoRemoteConfigured => "No remote configured",
        _ => detail.Message
    };
}
