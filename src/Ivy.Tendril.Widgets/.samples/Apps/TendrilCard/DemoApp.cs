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
            new CardItem("1", "Planning", 0, "Refactor the authentication middleware to support token rotation and refresh",
                "Loader", true, "Ivy-Tendril", "#6366f1", "Analyzing repositories...", "CornerDownRight",
                [new("FileText", "01520", "OpenPlan"), new("Timer", "1m 12s"), new("Coins", "6K")]),
            new CardItem("2", "Draft", 0, "Showcase team member contributions",
                "Eye", false, "Ivy-Tendril", "#6366f1", "Awaiting approval", "Eye",
                [new("FileText", "01515", "OpenPlan"), new("Timer", "5m 20s"), new("Coins", "14K")]),
            new CardItem("3", "Draft", 1, "Develop a communication plan",
                "Eye", false, "MyCRMDashboard", "#22a06b", "Awaiting approval", "Eye",
                [new("FileText", "01518", "OpenPlan"), new("Timer", "4m 30s"), new("Coins", "17K")]),
            new CardItem("4", "Implementing", 0, "Include a timeline for project phases",
                "Loader", true, "Ivy-Tendril", "#6366f1", "Editing files...", "CornerDownRight",
                [new("FileText", "01513", "OpenPlan"), new("Timer", "4m 45s"), new("Coins", "15K")]),
            new CardItem("5", "Review", 0, "Sign engagement letter for Strömberg Industri",
                "Eye", false, "MyCRMDashboard", "#22a06b", "Ready for review", "Eye",
                [new("FileText", "01509", "OpenPlan"), new("Timer", "12m 02s"), new("Coins", "88K")]),
        });

        var board = items.Value
            .ToKanban(
                x => x.Column,
                x => x.Id,
                x => x.Order)
            .Columns("Planning", "Draft", "Implementing", "Review")
            .ColumnIcon(c => c switch
            {
                "Planning" => "ScanLine",
                "Draft" => "Feather",
                "Implementing" => "Hammer",
                "Review" => "ThumbsUp",
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
                .WithOnMetaClick(tag => client.Toast(tag, "Meta clicked").Info())
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
