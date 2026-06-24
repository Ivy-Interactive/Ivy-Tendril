using Ivy;
using Ivy.Tendril.Widgets;
using TendrilCardWidget = Ivy.Tendril.Widgets.TendrilCard;

namespace WidgetSamples.Apps.TendrilCard;

[App(title: "Card Board", icon: Icons.LayoutGrid, group: ["TendrilCard"])]
class DemoApp : ViewBase
{
    public record CardItem(
        string Id,
        string Column,
        int Order,
        string Title,
        string Badge,
        string BadgeIcon,
        string Assignee,
        string AssigneeColor,
        string Footer
    );

    public override object Build()
    {
        var client = UseService<IClientProvider>();

        var items = UseState(() => new[]
        {
            new CardItem("1", "Partner sign-off", 0, "Sign engagement letter for Strömberg Industri",
                "Engagement", "ScanLine", "JP", "#e11d8f", "Awaiting approval"),
            new CardItem("2", "Partner sign-off", 1, "Sign Q4 compliance report — advisory",
                "Compliance", "ScanLine", "JP", "#e11d8f", "Awaiting approval"),
            new CardItem("3", "Filed", 0, "FY25 audit-prep binder filed with regulator",
                "Audit", "ScanLine", "SA", "#f97316", "Exported and submitted 2026-04-28"),
            new CardItem("4", "Filed", 1, "Cash-handling SOP v3.2 published",
                "SOP", "ScanLine", "MH", "#14b8a6", "Partner-approved, dependent flows repointed"),
            new CardItem("5", "Filed", 2, "Fortnox accounting renewal signed",
                "Renewal", "ScanLine", "AK", "#0ea5e9", "1-year renewal at flat rate, archived"),
            new CardItem("6", "Filed", 3, "Lindgren & Co onboarding live",
                "Onboarding", "ScanLine", "JP", "#e11d8f", "KYC cleared, engagement signed 2026-04"),
        });

        var board = items.Value
            .ToKanban(
                x => x.Column,
                x => x.Id,
                x => x.Order)
            .ColumnWidth(Size.Units(80))
            .CardBuilder((CardItem item) => (object)new TendrilCardWidget(item.Title)
                .WithBadge(item.Badge, item.BadgeIcon)
                .WithAssignee(item.Assignee, item.AssigneeColor)
                .WithFooter(item.Footer)
                .WithOnClick(() => client.Toast(item.Title, "Card clicked").Info()))
            .OnMove(e =>
            {
                var (cardId, toColumn, _) = e.Value;
                items.Set(items.Value
                    .Select(x => x.Id == cardId?.ToString() ? x with { Column = toColumn } : x)
                    .ToArray());
            });

        return Layout.Vertical().Height(Size.Full()).Padding(4)
            | Text.H3("Tendril Card Board")
            | board;
    }
}
