using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Drafts.Dialogs;

public class UpdatePlanDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IState<PlanFile?> selectedPlanState,
    IJobService jobService,
    Action refreshPlans) : ViewBase
{
    private readonly IState<bool> _dialogOpen = dialogOpen;
    private readonly IJobService _jobService = jobService;
    private readonly Action _refreshPlans = refreshPlans;
    private readonly PlanFile _selectedPlan = selectedPlan;
    private readonly IState<PlanFile?> _selectedPlanState = selectedPlanState;

    public override object? Build()
    {
        var isCreating = UseState(false);
        var updateText = UseState("");
        if (!_dialogOpen.Value) return null;

        // Check if there's already an UpdatePlan job running for this plan
        var hasActiveJob = _jobService.GetJobs().Any(j =>
            j.TypedArgs is UpdatePlanArgs &&
            j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.TypedArgs?.PlanFolder != null &&
            j.TypedArgs.PlanFolder.Equals(_selectedPlan.FolderPath, StringComparison.OrdinalIgnoreCase));

        return new Dialog(
            _ =>
            {
                updateText.Set("");
                isCreating.Set(false);
                _dialogOpen.Set(false);
            },
            new DialogHeader($"Update Plan #{_selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical()
                | Text.P("Provide instructions for revising this draft plan.")
                | (hasActiveJob ? Text.P("⚠️ UpdatePlan is already running for this plan. Please wait...").Color(Colors.Warning) : null)
                | updateText.ToTextareaInput("Enter update instructions...").Rows(6).AutoFocus()
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() =>
                {
                    updateText.Set("");
                    isCreating.Set(false);
                    _dialogOpen.Set(false);
                }),
                new Button("Submit Update").Primary().Disabled(hasActiveJob || isCreating.Value || string.IsNullOrWhiteSpace(updateText.Value)).ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    if (!string.IsNullOrWhiteSpace(updateText.Value) && !isCreating.Value)
                    {
                        isCreating.Set(true);

                        // Optimistically update UI state before disk I/O
                        var optimisticPlan = _selectedPlan with
                        {
                            Metadata = _selectedPlan.Metadata with { State = PlanStatus.Updating }
                        };
                        _selectedPlanState.Set(optimisticPlan);

                        // Plan transition (and pre-state snapshot) handled by JobService.StartJob.
                        _jobService.StartJob(new UpdatePlanArgs(_selectedPlan.FolderPath, updateText.Value));
                        _refreshPlans();
                        updateText.Set("");
                        isCreating.Set(false);
                        _dialogOpen.Set(false);
                    }
                })
            )
        ).Width(Size.Rem(30));
    }
}
