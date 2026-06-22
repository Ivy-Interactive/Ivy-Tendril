using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class CustomPrDialog(
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
        var customPrSolveMergeConflicts = UseState(true);
        var customPrMerge = UseState(false);
        var customPrDeleteBranch = UseState(false);
        var customPrIncludeArtifacts = UseState(false);
        var customPrAssignee = UseState<string?>(null);
        var customPrComment = UseState("");
        var customPrDraft = UseState(false);

        UseEffect(() =>
        {
            if (!customPrMerge.Value) customPrDeleteBranch.Set(false);
        }, customPrMerge);

        if (!dialogOpen.Value) return null;

        var multipleBranches = selectedPlan.Repos.Count > 1;

        return new Dialog(
            _ =>
            {
                isCreating.Set(false);
                customPrSolveMergeConflicts.Set(true);
                customPrMerge.Set(true);
                customPrDeleteBranch.Set(true);
                customPrIncludeArtifacts.Set(true);
                customPrAssignee.Set(null);
                customPrComment.Set("");
                customPrDraft.Set(false);
                dialogOpen.Set(false);
            },
            new DialogHeader($"Custom PR for #{selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical().Gap(2)
                | customPrSolveMergeConflicts.ToBoolInput("Solve Merge Conflicts").AutoFocus()
                | customPrMerge.ToBoolInput("Merge")
                | customPrDeleteBranch
                    .ToBoolInput(multipleBranches ? "Delete branches" : "Delete branch")
                    .Description(multipleBranches
                        ? "Deletes the branches pushed to origin after successful merge."
                        : "Deletes the branch pushed to origin after successful merge.")
                    .Disabled(!customPrMerge.Value)
                | customPrIncludeArtifacts.ToBoolInput("Include Artifacts")
                | customPrDraft.ToBoolInput("Create as Draft")
                | customPrAssignee.ToSelectInput((assigneesQuery.Value ?? Array.Empty<string>()).ToOptions())
                    .Nullable().WithField().Label("Assignee")
                | (assigneesError.Value is { } err
                    ? Text.Danger(err).Small()
                    : null)
                | customPrComment.ToTextareaInput("Comment").Rows(3)
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() =>
                {
                    isCreating.Set(false);
                    customPrSolveMergeConflicts.Set(true);
                    customPrMerge.Set(true);
                    customPrDeleteBranch.Set(true);
                    customPrIncludeArtifacts.Set(true);
                    customPrAssignee.Set(null);
                    customPrComment.Set("");
                    customPrDraft.Set(false);
                    dialogOpen.Set(false);
                }),
                new Button("Create PR").Primary().Disabled(isCreating.Value).ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    if (!isCreating.Value)
                    {
                        isCreating.Set(true);
                        jobService.StartJob(new CreatePrArgs(
                            selectedPlan.FolderPath,
                            SolveMergeConflicts: customPrSolveMergeConflicts.Value,
                            Merge: customPrMerge.Value,
                            DeleteBranch: customPrDeleteBranch.Value && customPrMerge.Value,
                            IncludeArtifacts: customPrIncludeArtifacts.Value,
                            Assignee: customPrAssignee.Value,
                            Comment: string.IsNullOrEmpty(customPrComment.Value) ? null : customPrComment.Value,
                            Draft: customPrDraft.Value));
                        // Plan transition (and pre-state snapshot) handled by JobService.StartJob.
                        refreshPlans();
                        isCreating.Set(false);
                        customPrSolveMergeConflicts.Set(true);
                        customPrMerge.Set(true);
                        customPrDeleteBranch.Set(true);
                        customPrIncludeArtifacts.Set(true);
                        customPrAssignee.Set(null);
                        customPrComment.Set("");
                        customPrDraft.Set(false);
                        dialogOpen.Set(false);
                    }
                })
            )
        ).Width(Size.Rem(30));
    }
}
