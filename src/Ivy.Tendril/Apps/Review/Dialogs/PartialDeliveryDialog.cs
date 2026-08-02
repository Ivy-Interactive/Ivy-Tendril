using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Apps.Review.Dialogs;

/// <summary>
///     Shown when completing a plan is blocked by a failed verification (see plan 00090). Offers the
///     one sanctioned way past the block: record the plan as a deliberate partial delivery, which
///     stamps <see cref="PlanYaml.PartialDelivery" /> so the possibly-missing deliverable stays
///     visible to duplicate detection instead of reading as done.
/// </summary>
public class PartialDeliveryDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IReadOnlyList<string> failedVerifications,
    IPlanReaderService planService,
    Action refreshPlans) : ViewBase
{
    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        var names = string.Join(", ", failedVerifications);

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Verification Failed"),
            new DialogBody(
                Layout.Vertical().Gap(3)
                | Text.P($"{names} failed for plan #{selectedPlan.Id}, so it cannot be completed.")
                | Text.Muted("Re-run the verification, or set it to Skipped with a reason, if the work is " +
                             "actually done. Only override if you are deliberately shipping this plan " +
                             "incomplete.")
                | Text.Muted("Overriding marks the plan as a partial delivery so future plans are not " +
                             "discarded as duplicates of work that never landed.")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Complete as Partial Delivery").Destructive().OnClick(() =>
                {
                    planService.CompleteWithPartialDelivery(selectedPlan.FolderName);
                    refreshPlans();
                    dialogOpen.Set(false);
                })
            )
        );
    }
}
