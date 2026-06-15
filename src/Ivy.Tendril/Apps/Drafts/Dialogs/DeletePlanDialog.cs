using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Drafts.Dialogs;

public class DeletePlanDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IState<PlanFile?> selectedPlanState,
    IPlanReaderService planService,
    Action refreshPlans) : ViewBase
{
    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Delete Plan"),
            new DialogBody(
                Text.P($"What would you like to do with plan #{selectedPlan.Id}?")
            ),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
                | new Button("Move to Skipped").Outline().ShortcutKey("s").OnClick(() =>
                {
                    // Optimistically update UI state before disk I/O
                    var optimisticPlan = selectedPlan with
                    {
                        Metadata = selectedPlan.Metadata with { State = PlanStatus.Skipped }
                    };
                    selectedPlanState.Set(optimisticPlan);

                    planService.TransitionState(selectedPlan.FolderName, PlanStatus.Skipped);
                    refreshPlans();
                    dialogOpen.Set(false);
                })
                | new Button("Move to Icebox").Outline().ShortcutKey("b").OnClick(() =>
                {
                    // Optimistically update UI state before disk I/O
                    var optimisticPlan = selectedPlan with
                    {
                        Metadata = selectedPlan.Metadata with { State = PlanStatus.Icebox }
                    };
                    selectedPlanState.Set(optimisticPlan);

                    planService.TransitionState(selectedPlan.FolderName, PlanStatus.Icebox);
                    refreshPlans();
                    dialogOpen.Set(false);
                })
                | new Button("Delete").Destructive().ShortcutKey("Enter").AutoFocus().OnClick(() =>
                {
                    planService.DeletePlan(selectedPlan.FolderName);
                    refreshPlans();
                    dialogOpen.Set(false);
                })
            )
        ).Width(Size.Rem(40));
    }
}
