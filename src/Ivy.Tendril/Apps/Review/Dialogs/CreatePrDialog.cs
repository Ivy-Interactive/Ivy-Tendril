using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class CreatePrDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IJobService jobService,
    Action refreshPlans,
    IConfigService config,
    IGithubService githubService) : ViewBase
{
    public override object? Build()
    {
        var isCreating = UseState(false);
        var createPrSolveMergeConflicts = UseState(true);
        var createPrMerge = UseState(true);
        var createPrDeleteBranch = UseState(true);
        var createPrIncludeArtifacts = UseState(false);
        var createPrDraft = UseState(false);
        var createPrReviewers = UseState(Array.Empty<string>());
        var createPrComment = UseState("");
        var assigneesError = UseState<string?>(null);

        var assigneesQuery = UseQuery<string[], string>(
            selectedPlan?.Project ?? "",
            async (_, _) =>
            {
                if (selectedPlan is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repos = selectedPlan.GetEffectiveRepoPaths(config);
                var repoPath = repos.FirstOrDefault();
                if (repoPath is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repoConfig = githubService.GetRepoConfigFromPathCached(repoPath);
                if (repoConfig is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var (assignees, error) = await githubService.GetAssigneesAsync(repoConfig.Owner, repoConfig.Name);
                assigneesError.Set(error);
                return assignees.ToArray();
            },
            initialValue: Array.Empty<string>()
        );

        UseEffect(() =>
        {
            if (!createPrMerge.Value) createPrDeleteBranch.Set(false);
        }, createPrMerge);

        if (!dialogOpen.Value) return null;

        var multipleBranches = selectedPlan.Repos.Count > 1;

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Create PR for #{selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical().Gap(2)
                | createPrSolveMergeConflicts.ToBoolInput("Solve Merge Conflicts").AutoFocus()
                | createPrMerge.ToBoolInput("Merge")
                | createPrDeleteBranch
                    .ToBoolInput(multipleBranches ? "Delete Branches" : "Delete Branch")
                    .Description(multipleBranches
                        ? "Deletes the branches pushed to origin after successful merge."
                        : "Deletes the branch pushed to origin after successful merge.")
                    .Disabled(!createPrMerge.Value)
                | createPrIncludeArtifacts.ToBoolInput("Include Artifacts")
                | createPrDraft.ToBoolInput("Create as Draft")
                | createPrReviewers.ToSelectInput((assigneesQuery.Value ?? Array.Empty<string>()).ToOptions())
                    .Placeholder("Select reviewers...")
                    .WithField().Label("Reviewers")
                | (assigneesError.Value is { } err
                    ? Text.Danger(err).Small()
                    : null)
                | createPrComment.ToTextareaInput("Comment").Rows(3)
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Create PR").Primary().Disabled(isCreating.Value).ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    if (!isCreating.Value)
                    {
                        isCreating.Set(true);
                        jobService.StartJob(new CreatePrArgs(
                            selectedPlan.FolderPath,
                            SolveMergeConflicts: createPrSolveMergeConflicts.Value,
                            Merge: createPrMerge.Value,
                            DeleteBranch: createPrDeleteBranch.Value && createPrMerge.Value,
                            IncludeArtifacts: createPrIncludeArtifacts.Value,
                            Reviewers: createPrReviewers.Value,
                            Comment: string.IsNullOrEmpty(createPrComment.Value) ? null : createPrComment.Value,
                            Draft: createPrDraft.Value));
                        // Plan transition (and pre-state snapshot) handled by JobService.StartJob.
                        refreshPlans();
                        dialogOpen.Set(false);
                    }
                })
            )
        ).Width(Size.Rem(30));
    }
}
