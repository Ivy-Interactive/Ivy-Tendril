using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class CreatePrDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IJobService jobService,
    Action refreshPlans,
    QueryResult<string[]> assigneesQuery,
    IState<string?> assigneesError) : ViewBase
{
    public override object? Build()
    {
        var isCreating = UseState(false);
        var createPrSolveMergeConflicts = UseState(true);
        var createPrMerge = UseState(true);
        var createPrDeleteBranch = UseState(true);
        var createPrIncludeArtifacts = UseState(false);
        var createPrDraft = UseState(false);
        var createPrReviewer = UseState<string?>(null);
        var createPrComment = UseState("");
        
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
                | createPrReviewer.ToSelectInput((assigneesQuery.Value ?? Array.Empty<string>()).ToOptions())
                    .Nullable().WithField().Label("Reviewer")
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
                            Reviewer: createPrReviewer.Value,
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
