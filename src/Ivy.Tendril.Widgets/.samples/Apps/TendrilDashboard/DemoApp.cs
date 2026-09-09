using System.Globalization;
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
        var showTunnel = UseState(true);
        var showUpdate = UseState(true);

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

        // A deterministic daily series ending on the date in the header. Weekdays cost noticeably more
        // than weekends, which is the day-to-day noise the rolling curve is there to smooth.
        const int trendDays = 365;
        var trendEnd = new DateOnly(2026, 8, 20);
        var trendRandom = new Random(11);
        var trendDates = new List<string>(trendDays);
        var trendCost = new List<double>(trendDays);
        var trendPlans = new List<double>(trendDays);
        for (var i = 0; i < trendDays; i++)
        {
            var date = trendEnd.AddDays(-(trendDays - 1) + i);
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            trendDates.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            trendCost.Add(weekend ? trendRandom.Next(0, 120) : trendRandom.Next(200, 1400));
            trendPlans.Add(weekend ? trendRandom.Next(0, 2) : trendRandom.Next(2, 12));
        }


        // Mirrors UpdateNoticeView's compact layout: alert + actions, filling
        // the dashboard's fixed 120px update slot.
        var updateNotice = new Card(
                Layout.Vertical().Gap(2)
                | Text.Rich().Bold("Update Available").Run(" — v1.4.2").Small()
                | (Layout.Horizontal().Gap(2)
                   | new Button("Copy Command", () => client.Toast("Copy clicked", "OnCopy").Info()).Small()
                   | new Button("Show Details", () => client.Toast("Details clicked", "OnDetails").Info())
                       .Variant(ButtonVariant.Secondary).Small()))
            .Height(Size.Full());

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

        var jobs = new List<DashboardJobDto>
        {
            new("job-14", "00051", "Make content input widget responsive to theming color variables", "running"),
            new("job-13", "00050", "Remove action column from review actions in project settings", "running"),
            new("job-12", "00049", "Add retry backoff to the plan verification loop", "queued"),
            new("job-11", "00048", "Fix flaky navigation test in the shell tab router", "queued"),
            new("job-10", "", "Sync repository ivy-interactive/tendril", "blocked"),
        };

        var dashboard = new TendrilDashboardWidget(
                processView,
                showUpdate.Value ? updateNotice : null,
                showTunnel.Value ? tunnelQr : null,
                showTunnel.Value ? tunnelMenu : null)
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
                trendDates,
                trendCost,
                trendPlans,
                Rolling(trendCost),
                Rolling(trendPlans)))
            // The short range is the tail of the same series, so its rolling curve starts six days in
            // and the gap that leaves is visible in the demo.
            .TrendWeekly(new DashboardTrendDto(
                trendDates.TakeLast(28).ToList(),
                trendCost.TakeLast(28).ToList(),
                trendPlans.TakeLast(28).ToList(),
                Rolling(trendCost.TakeLast(28).ToList()),
                Rolling(trendPlans.TakeLast(28).ToList())))
            .OnDrafts(() => client.Toast("Drafts clicked", "OnDrafts").Info())
            .OnReview(() => client.Toast("Review clicked", "OnReview").Info())
            .OnJobs(() => client.Toast("Jobs clicked", "OnJobs").Info())
            .OnJob(jobId => client.Toast($"Job {jobId} clicked", "OnJob").Info())
            .PullRequests(
            [
                new DashboardMonthValueDto("Jul", 24),
                new DashboardMonthValueDto("Aug", 101),
                new DashboardMonthValueDto("Sep", 62),
                new DashboardMonthValueDto("Oct", 118),
                new DashboardMonthValueDto("Nov", 28),
                new DashboardMonthValueDto("Dec", 84)
            ])
            .Activity(activity)
            .Jobs(jobs);

        // Demo-only toggles for the two optional pieces that change the grid.
        var toggles = new FloatingPanel(
                Layout.Horizontal().Gap(2)
                | new Button($"Tunnel: {(showTunnel.Value ? "on" : "off")}",
                    () => showTunnel.Set(!showTunnel.Value)).Small().Variant(ButtonVariant.Secondary)
                | new Button($"Update: {(showUpdate.Value ? "on" : "off")}",
                    () => showUpdate.Set(!showUpdate.Value)).Small().Variant(ButtonVariant.Secondary))
            .Offset(new Thickness(0, 0, 8, 8));

        return new Fragment(dashboard, toggles);
    }

    /// <summary>
    ///     Mirrors the server's rolling mean for demo data: null until a full window of days exists, so
    ///     the curve starts where the history does.
    /// </summary>
    private static List<double?> Rolling(List<double> values, int window = 7) =>
        values
            .Select((_, i) => i < window - 1
                ? (double?)null
                : values.Skip(i - window + 1).Take(window).Average())
            .ToList();
}
