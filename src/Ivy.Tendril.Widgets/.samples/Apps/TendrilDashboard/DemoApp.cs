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
            "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

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

        return new TendrilDashboardWidget(processView)
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
                [12400, 5100, 12800, 24500, 28900, 19600, 23800],
                [3400, 14200, 20600, 6100, 10800, 22100, 24800, 26300, 27100, 28600, 29800, 31400],
                [42, 18, 45, 88, 102, 71, 85],
                [12, 51, 74, 22, 39, 79, 89, 94, 97, 102, 107, 112]))
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
