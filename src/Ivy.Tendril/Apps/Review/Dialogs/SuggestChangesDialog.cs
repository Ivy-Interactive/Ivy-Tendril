using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class SuggestChangesDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IJobService jobService,
    IPlanReaderService planService,
    Action refreshPlans) : ViewBase
{
    private readonly IState<bool> _dialogOpen = dialogOpen;
    private readonly IJobService _jobService = jobService;
    private readonly IPlanReaderService _planService = planService;
    private readonly Action _refreshPlans = refreshPlans;
    private readonly PlanFile _selectedPlan = selectedPlan;

    public override object? Build()
    {
        var isCreating = UseState(false);
        var suggestText = UseState("");
        if (!_dialogOpen.Value) return null;

        return new Dialog(
            _ =>
            {
                isCreating.Set(false);
                suggestText.Set("");
                _dialogOpen.Set(false);
            },
            new DialogHeader($"Suggest Changes for Plan #{_selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical()
                | Text.P("Provide suggestions for changes to the plan.")
                | suggestText.ToTextareaInput("Enter your suggestions...").Rows(6).AutoFocus()
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() =>
                {
                    isCreating.Set(false);
                    suggestText.Set("");
                    _dialogOpen.Set(false);
                }),
                new Button("Submit Suggestions").Primary().Disabled(isCreating.Value || string.IsNullOrWhiteSpace(suggestText.Value)).ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    if (!string.IsNullOrWhiteSpace(suggestText.Value) && !isCreating.Value)
                    {
                        isCreating.Set(true);

                        _planService.ResetVerificationsForRetry(_selectedPlan.FolderName);
                        _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Executing);
                        _jobService.StartJob(new RetryPlanArgs(_selectedPlan.FolderPath, suggestText.Value));
                        _refreshPlans();
                        isCreating.Set(false);
                        suggestText.Set("");
                        _dialogOpen.Set(false);
                    }
                })
            )
        ).Width(Size.Rem(30));
    }
}