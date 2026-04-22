using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Plans.Dialogs;

public class UpdatePlanDialog(
    IState<bool> dialogOpen,
    IState<string> updateText,
    PlanFile selectedPlan,
    IState<PlanFile?> selectedPlanState,
    IJobService jobService,
    IPlanReaderService planService,
    Action refreshPlans) : ViewBase
{
    private readonly IState<bool> _dialogOpen = dialogOpen;
    private readonly IJobService _jobService = jobService;
    private readonly IPlanReaderService _planService = planService;
    private readonly Action _refreshPlans = refreshPlans;
    private readonly PlanFile _selectedPlan = selectedPlan;
    private readonly IState<PlanFile?> _selectedPlanState = selectedPlanState;
    private readonly IState<string> _updateText = updateText;

    public override object? Build()
    {
        var isCreating = UseState(false);
        if (!_dialogOpen.Value) return null;

        // Check if there's already an UpdatePlan job running for this plan
        var hasActiveJob = _jobService.GetJobs().Any(j =>
            j.Type == "UpdatePlan" &&
            j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.Args.Length > 0 &&
            j.Args[0].Equals(_selectedPlan.FolderPath, StringComparison.OrdinalIgnoreCase));

        return new Dialog(
            _ =>
            {
                _updateText.Set("");
                _dialogOpen.Set(false);
            },
            new DialogHeader($"Update Plan #{_selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical()
                | Text.P("Provide instructions for revising this draft plan.")
                | (hasActiveJob ? Text.P("⚠️ UpdatePlan is already running for this plan. Please wait...").Color(Colors.Warning) : null)
                | _updateText.ToTextareaInput("Enter update instructions...").Rows(6).AutoFocus()
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() =>
                {
                    _updateText.Set("");
                    _dialogOpen.Set(false);
                }),
                new Button("Submit Update").Primary().Disabled(hasActiveJob || isCreating.Value || string.IsNullOrWhiteSpace(_updateText.Value)).ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    if (!string.IsNullOrWhiteSpace(_updateText.Value) && !isCreating.Value)
                    {
                        isCreating.Set(true);

                        // Optimistically update UI state before disk I/O
                        var optimisticPlan = _selectedPlan with
                        {
                            Metadata = _selectedPlan.Metadata with { State = PlanStatus.Updating }
                        };
                        _selectedPlanState.Set(optimisticPlan);

                        // Append >> comments to the latest revision so UpdatePlan can process them
                        var currentContent = _planService.ReadLatestRevision(_selectedPlan.FolderName);
                        var comments = string.Join("\n", _updateText.Value
                            .Split('\n')
                            .Select(line => $">> {line}"));
                        _planService.SavePlan(_selectedPlan.FolderName, currentContent + "\n\n" + comments + "\n");

                        _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Updating);
                        _jobService.StartJob("UpdatePlan", _selectedPlan.FolderPath);
                        _refreshPlans();
                        _updateText.Set("");
                        _dialogOpen.Set(false);
                    }
                })
            )
        ).Width(Size.Rem(30));
    }
}