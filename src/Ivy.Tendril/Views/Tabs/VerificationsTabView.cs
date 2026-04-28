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
        var rows = verifications.Select(v => new VerificationRow(
            v.Status,
            v.Name,
            verificationReports.TryGetValue(v.Name, out var exists) && exists
        )).ToList();

        var reportLookup = rows.ToDictionary(r => r.Name, r => r.HasReport);

        return new TableBuilder<VerificationRow>(rows)
            .Order(t => t.Status, t => t.Name)
            .Builder(t => t.Status, f => f.Func<VerificationRow, string>(status =>
                new Badge(status).Variant(
                    StatusMappings.VerificationStatusBadgeVariants.TryGetValue(status, out var variant)
                        ? variant
                        : BadgeVariant.Outline)))
            .Builder(t => t.Name, f => f.Func<VerificationRow, string>(name =>
                reportLookup.GetValueOrDefault(name)
                    ? new Button(name).Inline().OnClick(() => openVerification(name))
                    : (object)Text.Block(name)))
            .Remove(t => t.HasReport);
    }

    private record VerificationRow(string Status, string Name, bool HasReport);
}
