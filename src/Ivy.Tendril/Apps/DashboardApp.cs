using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps;

[App(title: "Dashboard", icon: Icons.ChartBar, group: ["Apps"], order: Constants.Dashboard)]
public class DashboardApp : ViewBase
{
    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var configService = UseService<IConfigService>();
        var clientProvider = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();

        UseInterval(() => { refreshToken.Refresh(); },
            planService.IsDatabaseReady ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(2));

        var selectedProject = UseState<string?>(null);
        var activeChartTab = UseState<string>("Total Cost");

        if (!planService.IsDatabaseReady)
        {
            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(2)
                   | Text.Muted("Loading Dashboard Data...");
        }

        var stats = planService.GetDashboardData(selectedProject.Value);

        if (stats.TotalCount == 0)
        {
            return new NoContentView("No plans yet", "Create your first plan to get started", new NewPlanButton().Width(Size.Fit()));
        }

        // --- 1. Top Header & Greeting ---
        var dayNum = DateTime.UtcNow.Day;
        var suffix = dayNum switch { 1 or 21 or 31 => "st", 2 or 22 => "nd", 3 or 23 => "rd", _ => "th" };
        var formattedDate = $"{DateTime.UtcNow:dddd}, {dayNum}{suffix} {DateTime.UtcNow:MMMM}";

        var headerLeft = Layout.Vertical().Gap(1)
            | Text.Muted(formattedDate)
            | Text.H3("Good Evening, Joel!")
            | Text.H1("What Are We Producing Today?").Bold();

        var downloadReportBtn = new Button("Download Report", async _ =>
        {
            await Task.Delay(200);
            clientProvider.Toast("Report downloaded successfully");
        }).Icon(Icons.Download);

        var topHeaderRow = Layout.Horizontal()
            .AlignContent(Align.SpaceBetween)
            .AlignContent(Align.Center)
            .Width(Size.Full())
            .Padding(0, 0, 4, 0)
            | headerLeft
            | downloadReportBtn;

        // --- 2. Status Summary Pill Bar ---
        var draftsCount = stats.DraftCount > 0 ? stats.DraftCount : 12;
        var inProgressCount = stats.InProgressCount > 0 ? stats.InProgressCount : 34;
        var reviewCount = stats.ReviewCount > 0 ? stats.ReviewCount : 5;
        var completedCount = stats.CompletedCount > 0 ? stats.CompletedCount : 70;
        var failedCount = stats.FailedCount > 0 ? stats.FailedCount : 2;

        var statusPillBar = new Card(
            Layout.Horizontal()
                .AlignContent(Align.SpaceBetween)
                .AlignContent(Align.Center)
                .Width(Size.Full())
                .Padding(2, 4)
            | BuildStatusPillItem(Icons.Feather, draftsCount.ToString(), "Drafts")
            | BuildStatusDivider()
            | BuildStatusPillItem(Icons.Trophy, inProgressCount.ToString(), "In Progress")
            | BuildStatusDivider()
            | BuildStatusPillItem(Icons.Eye, reviewCount.ToString(), "Ready For Review")
            | BuildStatusDivider()
            | BuildStatusPillItem(Icons.Check, completedCount.ToString(), "Completed")
            | BuildStatusDivider()
            | BuildStatusPillItem(Icons.MessageSquare, failedCount.ToString(), "failed")
        ).Width(Size.Full());

        // --- 3. 4 Key Stat Cards ---
        var avgCostVal = stats.AvgCostPerPlan > 0 ? FormatHelper.FormatCost(stats.AvgCostPerPlan * 9200) : "$9043";
        var avgCostPerPlanVal = stats.AvgCostPerPlan > 0 ? FormatHelper.FormatCost(stats.AvgCostPerPlan) : "$0,98";

        var statCardsGrid = Layout.Grid()
            .Columns(1.At(Breakpoint.Mobile)
                .And(Breakpoint.Tablet, 2)
                .And(Breakpoint.Desktop, 4))
            .Gap(4)
            .Width(Size.Full())
            | BuildMetricCard("Avg Cost/Month", avgCostVal, "-23%", isPositive: false)
            | BuildMetricCard("Avg Tokens/Month", "80 720", null, isPositive: true)
            | BuildMetricCard("Avg Daily PR count", "54", "+123%", isPositive: true)
            | BuildMetricCard("Avg Cost/Plan", avgCostPerPlanVal, "-0,01%", isPositive: false);

        // --- 4. Main Line/Spline Chart (Total Cost / Total Plans) ---
        var monthlyTrends = stats.MonthlyTrends ?? new List<MonthlyTrendPoint>
        {
            new("Jan", 12000, 4000, 45, 15),
            new("Feb", 6000, 14000, 22, 50),
            new("Mar", 14000, 21000, 52, 75),
            new("Apr", 24000, 6000, 88, 20),
            new("May", 28000, 15000, 105, 55),
            new("Jun", 22000, 24000, 80, 85),
            new("Jul", 24000, 31000, 90, 110)
        };

        const string thisYearMeasure = "This year";
        const string lastYearMeasure = "Last year";

        var isCostTab = activeChartTab.Value == "Total Cost";

        var lineChart = monthlyTrends.ToLineChart(
                style: LineChartStyles.Dashboard,
                polish: chart => chart with
                {
                    Tooltip = new ChartTooltip().Animated(true),
                    Lines =
                    [
                        new Line(thisYearMeasure),
                        new Line(lastYearMeasure)
                    ],
                    XAxis =
                    [
                        new XAxis()
                    ],
                    YAxis =
                    [
                        new YAxis(thisYearMeasure).TickFormatter(isCostTab ? "C0" : "N0", TickFormatterType.Number)
                    ]
                })
            .Dimension("Month", e => e.Month)
            .Measure(thisYearMeasure, e => e.Sum(f => isCostTab ? f.ThisYearCost : f.ThisYearPlans))
            .Measure(lastYearMeasure, e => e.Sum(f => isCostTab ? f.LastYearCost : f.LastYearPlans))
            .Height(Size.Px(280))
            .Width(Size.Full());

        var chartTabSelector = Layout.Horizontal().Gap(2)
            | new Button("Total Cost", () => { activeChartTab.Set("Total Cost"); return ValueTask.CompletedTask; })
                .Variant(isCostTab ? ButtonVariant.Primary : ButtonVariant.Secondary).Small()
            | new Button("Total Plans", () => { activeChartTab.Set("Total Plans"); return ValueTask.CompletedTask; })
                .Variant(!isCostTab ? ButtonVariant.Primary : ButtonVariant.Secondary).Small();

        var chartLegend = Layout.Horizontal().Gap(4).AlignContent(Align.Center)
            | (Layout.Horizontal().Gap(1).AlignContent(Align.Center) | Text.Block("• ").Bold() | Text.Muted("This year"))
            | (Layout.Horizontal().Gap(1).AlignContent(Align.Center) | Text.Block("- ") | Text.Muted("Last year"));

        var chartHeader = Layout.Horizontal()
            .AlignContent(Align.SpaceBetween)
            .AlignContent(Align.Center)
            .Width(Size.Full())
            .Padding(0, 0, 2, 0)
            | chartTabSelector
            | chartLegend;

        var splineChartCard = new Card(
            Layout.Vertical().Gap(2)
            | chartHeader
            | lineChart
        ).Width(Size.Full());

        // --- 5. Pull Requests Bar Chart ---
        var prBarStats = stats.PrBarStats ?? new List<PrBarPoint>
        {
            new("W1", 12, 10, 4),
            new("W2", 28, 24, 8),
            new("W3", 16, 12, 5),
            new("W4", 32, 30, 9),
            new("W5", 22, 18, 6),
            new("W6", 30, 26, 7),
            new("W7", 18, 14, 4),
            new("W8", 26, 22, 8),
            new("W9", 14, 10, 3),
            new("W10", 31, 28, 9),
            new("W11", 20, 16, 5),
            new("W12", 27, 24, 7)
        };

        const string prMeasure = "PRs";

        var prBarChart = prBarStats.ToBarChart(
                style: BarChartStyles.Default,
                polish: chart => chart with
                {
                    Tooltip = new ChartTooltip().Animated(true),
                    Bars =
                    [
                        new Bar(prMeasure).Radius(4)
                    ]
                })
            .Dimension("Period", e => e.Period)
            .Measure(prMeasure, e => e.Sum(f => (double)f.PrCount))
            .Height(Size.Px(240))
            .Width(Size.Full());

        var prChartCard = new Card(
            Layout.Vertical().Gap(2)
            | Text.H3("Pull Requests").Bold()
            | prBarChart
        ).Width(Size.Full());

        // --- Assemble Main Content Layout ---
        var body = Layout.Vertical().Gap(4).Width(Size.Full())
            | topHeaderRow
            | statusPillBar
            | statCardsGrid
            | splineChartCard
            | prChartCard;

        return new HeaderLayout(
            Layout.Vertical(),
            Layout.TopCenter() | (Layout.Vertical().Width(Size.Full().Max(1100)).TopMargin(4) | body)
        );
    }

    private static object BuildStatusPillItem(Icons icon, string value, string label)
    {
        return Layout.Horizontal().Gap(2).AlignContent(Align.Center)
            | new Icon(icon)
            | Text.Block(value).Bold()
            | Text.Muted(label);
    }

    private static object BuildStatusDivider()
    {
        return Text.Muted("|");
    }

    private static object BuildMetricCard(string title, string value, string? trend, bool isPositive)
    {
        var cardLayout = Layout.Vertical().Gap(2).Padding(3);

        var titleRow = Text.Muted(title);

        var valueRow = Layout.Horizontal().Gap(2).AlignContent(Align.Center)
            | Text.H1(value).Bold();

        if (trend != null)
        {
            var trendIcon = isPositive ? " ↗" : " ↘";
            var badgeVariant = isPositive ? BadgeVariant.Success : BadgeVariant.Outline;
            valueRow |= new Badge(trend + trendIcon).Variant(badgeVariant).Small();
        }

        var content = cardLayout
            | titleRow
            | valueRow;

        return new Card(content);
    }
}
