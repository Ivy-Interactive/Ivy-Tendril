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
        string Icon,
        bool IconSpin,
        string Project,
        string ProjectColor,
        string Status,
        string StatusIcon,
        TendrilCardMeta[] Meta
    );

    public override object Build()
    {
        var client = UseService<IClientProvider>();

        var items = UseState(() => new[]
        {
            new CardItem("1", "Draft", 0, "Showcase team member contributions",
                "Loader", true, "Ivy-Tendril", "#6366f1", "Tracking roles...", "CornerDownRight",
                [new("FileText", "01515"), new("Timer", "5m 20s"), new("Coins", "14K")]),
            new CardItem("2", "Draft", 1, "Develop a communication plan",
                "Eye", false, "MyCRMDashboard", "#22a06b", "Awaiting approval", "Eye",
                [new("FileText", "01518"), new("Timer", "4m 30s"), new("Coins", "17K")]),
            new CardItem("3", "Draft", 2, "Include a timeline for project phases",
                "Eye", false, "Ivy-Tendril", "#6366f1", "Awaiting approval", "Eye",
                [new("FileText", "01513"), new("Timer", "4m 45s"), new("Coins", "15K")]),
            new CardItem("4", "Review", 0, "Sign engagement letter for Strömberg Industri",
                "Eye", false, "MyCRMDashboard", "#22a06b", "Ready for review", "Eye",
                [new("FileText", "01509"), new("Timer", "12m 02s"), new("Coins", "88K")]),
            new CardItem("5", "PR", 0, "Cash-handling SOP v3.2 published",
                "GitPullRequest", false, "Ivy-Tendril", "#6366f1", "PR #1686", "GitPullRequest",
                [new("FileText", "01502"), new("Timer", "22m 10s"), new("Coins", "131K")]),
        });

        var board = items.Value
            .ToKanban(
                x => x.Column,
                x => x.Id,
                x => x.Order)
            .Columns("Draft", "Review", "PR")
            .ColumnIcon(c => c switch
            {
                "Draft" => "Feather",
                "Review" => "ThumbsUp",
                "PR" => "GitPullRequest",
                _ => "ScanLine"
            })
            .ColumnWidth(Size.Units(80))
            .CardBuilder((CardItem item) => (object)new TendrilCardWidget(item.Title)
                .WithIcon(item.Icon, item.IconSpin)
                .WithProject(item.Project, item.ProjectColor)
                .WithStatus(item.Status, item.StatusIcon)
                .WithMeta(item.Meta)
                .WithMenu(
                    new TendrilCardMenuItem("Update", "Update", "WandSparkles"),
                    new TendrilCardMenuItem("Delete", "Delete", "Trash", Destructive: true))
                .WithOnMenuSelect(tag => client.Toast(tag, "Menu action").Info())
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
