using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Review;

/// <summary>
///     The Verifications dropdown of a plan under review: each verification's outcome, with the
///     name opening the report when one was written.
/// </summary>
public class ReviewVerificationsPanelView(
    List<PlanVerificationEntry> verifications,
    Dictionary<string, bool> verificationReports,
    Action<string> openVerification) : ViewBase
{
    public override object Build()
    {
        if (verifications.Count == 0)
            return Text.Muted("No verifications");

        var grid = Layout
            .Grid()
            .Columns(2)
            .ColumnWidths(Size.Auto(), Size.Fraction(1f))
            .Gap(2);

        foreach (var v in verifications)
        {
            var badge = new Badge(v.Status.ToString()).Variant(
                Constants.VerificationStatusBadgeVariants.GetValueOrDefault(v.Status, BadgeVariant.Outline));

            object name = verificationReports.GetValueOrDefault(v.Name)
                ? new Button(v.Name).Inline().OnClick(() => openVerification(v.Name))
                : Text.Block(v.Name);

            grid |= badge;
            grid |= name;
        }

        return grid;
    }
}
