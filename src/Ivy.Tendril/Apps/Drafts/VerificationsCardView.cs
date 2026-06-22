using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Apps.Drafts;

/// <summary>
///     Sticky-sidebar card listing a plan's verifications as checkboxes. Toggling a checkbox
///     persists the new status (Pending when checked, Skipped when unchecked) to plan.yaml
///     immediately. Editing is only allowed while the plan is in Draft — once it has run the
///     checkboxes are disabled and show the real Pass/Fail/Skipped status. The list is the
///     plan.yaml snapshot; the Required flag is read live from the project config.
/// </summary>
public class VerificationsCardView(
    PlanFile selectedPlan,
    IPlanReaderService planService,
    IConfigService config) : ViewBase
{
    public override object Build()
    {
        var editable = selectedPlan.Status == PlanStatus.Draft;

        var projectVerifications = config.GetProject(selectedPlan.Project)?.Verifications
                                   ?? new List<ProjectVerificationRef>();

        // Always present in project-config order, regardless of plan.yaml storage order.
        var verifications = PlanCommandHelpers.OrderByProjectConfig(selectedPlan.Verifications, projectVerifications);

        bool IsRequired(string name) => projectVerifications
            .Any(pv => pv.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && pv.Required);

        var inner = Layout.Vertical().Gap(0)
                    | new Box(Text.Block("Verifications").Bold()).Margin(0, 0, 2, 0);

        if (verifications.Count == 0)
        {
            inner |= Text.Muted("No verifications");
        }
        else
        {
            foreach (var v in verifications)
                inner |= new VerificationRowView(
                    v,
                    IsRequired(v.Name),
                    editable,
                    status => planService.SetVerificationStatus(selectedPlan.FolderName, v.Name, status));
        }

        return new Card(inner).Width(Size.Px(280));
    }
}

/// <summary>
///     A single verification row: a checkbox bound to the verification's status plus a status
///     badge. Kept as its own view so each row owns its checkbox state (no hooks in a loop).
/// </summary>
public class VerificationRowView(
    PlanVerificationEntry verification,
    bool required,
    bool editable,
    Action<VerificationStatus> onStatusChange) : ViewBase
{
    public override object Build()
    {
        var isChecked = UseState(verification.Status != VerificationStatus.Skipped);
        var previousChecked = UseState(verification.Status != VerificationStatus.Skipped);

        // Persist on user toggle (skip the initial render and disabled/non-editable rows).
        UseEffect(() =>
        {
            if (!editable || isChecked.Value == previousChecked.Value) return;
            previousChecked.Set(isChecked.Value);
            onStatusChange(isChecked.Value ? VerificationStatus.Pending : VerificationStatus.Skipped);
        }, isChecked);

        // While editable the status tracks the checkbox (Pending/Skipped); once run it reflects
        // the real persisted status (Pass/Fail/…).
        var status = editable
            ? (isChecked.Value ? VerificationStatus.Pending : VerificationStatus.Skipped)
            : verification.Status;

        var checkbox = isChecked.ToBoolInput(verification.Name, !editable);
        if (editable && required && !isChecked.Value)
            checkbox = checkbox.Invalid("This verification is required according to project settings");

        var row = Layout.Horizontal().Gap(2).Width(Size.Full()) | checkbox;

        // Only surface a badge for terminal outcomes; Pending/Skipped are conveyed by the checkbox.
        if (status is VerificationStatus.Pass or VerificationStatus.Fail)
            row |= new Badge(status.ToString()).Variant(
                Constants.VerificationStatusBadgeVariants.GetValueOrDefault(status, BadgeVariant.Outline));

        return row;
    }
}
