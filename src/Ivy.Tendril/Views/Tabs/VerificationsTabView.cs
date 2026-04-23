using Ivy.Tendril.Apps;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Views.Tabs;

public class VerificationsTabView(
    List<PlanVerificationEntry> verifications,
    Dictionary<string, bool> verificationReports,
    Action<string> openVerification) : ViewBase
{
    public override object Build()
    {
        var table = new Table(
            new TableRow(
                    new TableCell("Status").IsHeader(),
                    new TableCell("Name").IsHeader()
                )
            { IsHeader = true }
        );

        foreach (var v in verifications)
        {
            var hasReport = verificationReports.TryGetValue(v.Name, out var exists) && exists;
            var nameCapture = v.Name;
            var nameCell = hasReport
                ? new Button(v.Name).Inline().OnClick(() => openVerification(nameCapture))
                : (object)Text.Block(v.Name);

            table |= new TableRow(
                new TableCell(new Badge(v.Status).Variant(
                    StatusMappings.VerificationStatusBadgeVariants.TryGetValue(v.Status, out var variant)
                        ? variant
                        : BadgeVariant.Outline)),
                new TableCell(nameCell)
            );
        }

        return table;
    }
}
