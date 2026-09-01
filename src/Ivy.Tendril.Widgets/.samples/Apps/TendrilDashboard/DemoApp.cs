using Ivy;
using Ivy.Tendril.Widgets;
using TendrilDashboardWidget = Ivy.Tendril.Widgets.TendrilDashboard;
using TendrilProcessViewerWidget = Ivy.Tendril.Widgets.TendrilProcessViewer;

namespace WidgetSamples.Apps.TendrilDashboard;

[App(title: "Dashboard", icon: Icons.ChartBar, group: ["TendrilDashboard"])]
class DemoApp : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

        var processView = new TendrilProcessViewerWidget()
            .DraftCount(13)
            .ReviewCount(8)
            .CreatingPlansCount(13)
            .UpdatingPlansCount(2)
            .ExecutingPlansCount(19)
            .RetryingPlansCount(9)
            .CreatingPrCount(14)
            .OnCreate(() => client.Toast("Create clicked", "OnCreate").Info())
            .OnDrafts(() => client.Toast("Drafts clicked", "OnDrafts").Info())
            .OnReview(() => client.Toast("Review clicked", "OnReview").Info())
            .OnJobs(() => client.Toast("Jobs clicked", "OnJobs").Info());

        var months = new List<string>
        {
            "Sep", "Oct", "Nov", "Dec", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug"
        };

        var updateNotice = new Card(
                Layout.Vertical()
                | Text.Block("v1.4.2 is available (you have v1.4.0)").Small()
                | (Layout.Horizontal().Gap(2)
                   | new Button("Update Now", () => client.Toast("Update clicked", "OnUpdate").Info()).Small()
                   | new Button("Dismiss", () => client.Toast("Dismiss clicked", "OnDismiss").Info())
                       .Variant(ButtonVariant.Secondary).Small()))
            .Header("Update Available", null, Icons.CircleArrowUp);

        var tunnelQr = new Box("QR").Width(Size.Units(40)).Height(Size.Units(40));
        var tunnelMenu = new Button().Icon(Icons.Ellipsis).Ghost().Small().WithDropDown(
            new MenuItem("Copy to Clipboard", Icon: Icons.ClipboardCopy, Tag: "copy")
                .OnSelect(() => client.Toast("Copy clicked", "Tunnel").Info()));

        var random = new Random(7);
        var activity = new List<DashboardActivityMonthDto>();
        var activityLabels = new[]
        {
            "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
            "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug"
        };
        foreach (var label in activityLabels)
        {
            var weekCount = random.Next(3, 7);
            var weeks = Enumerable.Range(0, weekCount).Select(_ => random.Next(0, 24)).ToList();
            activity.Add(new DashboardActivityMonthDto(label, weeks));
        }

        return new TendrilDashboardWidget(processView, updateNotice, tunnelQr, tunnelMenu)
            .DateText("Thursday, 20th August")
            .Greeting("Good Evening, Joel!")
            .Headline("What Are We Producing Today?")
            .DraftCount(12)
            .InProgressCount(34)
            .ReviewCount(5)
            .CompletedCount(70)
            .FailedCount(2)
            .Kpis(
            [
                new DashboardKpiDto("Avg Daily PR count", "54", "+123%", "up"),
                new DashboardKpiDto("Avg Cost/Month", "$9043", "-23%", "down"),
                new DashboardKpiDto("Avg Tokens/Month", "80,720"),
                new DashboardKpiDto("Avg Cost/Plan", "$0.98", "-0.01%", "down")
            ])
            .Trend(new DashboardTrendDto(
                months,
                [12400, 5100, 0, 12800, 24500, 28900, 19600, 23800, 21200, 26500, 24100, 29400],
                [42, 18, 0, 45, 88, 102, 71, 85, 64, 91, 78, 96]))
            .OnDrafts(() => client.Toast("Drafts clicked", "OnDrafts").Info())
            .OnReview(() => client.Toast("Review clicked", "OnReview").Info())
            .OnJobs(() => client.Toast("Jobs clicked", "OnJobs").Info())
            .PullRequests(
            [
                new DashboardMonthValueDto("Jul", 24),
                new DashboardMonthValueDto("Aug", 101),
                new DashboardMonthValueDto("Sep", 62),
                new DashboardMonthValueDto("Oct", 118),
                new DashboardMonthValueDto("Nov", 28),
                new DashboardMonthValueDto("Dec", 84)
            ])
            .Activity(activity);
    }
}
