using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ivy.Tendril.Agents.Services;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Apps.Jobs.Sheets;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Tunnel;
using Ivy.Tendril.Widgets;
using Ivy.Widgets.QRCode;
using JobsApp = Ivy.Tendril.Apps.Jobs.JobsApp;

namespace Ivy.Tendril.Apps;

[App(title: "Dashboard", icon: Icons.ChartBar, group: ["Apps"], order: Constants.Dashboard)]
public class DashboardApp : ViewBase
{
    private const int ActivityMonths = 16;
    private const int TrendMonthsBack = 24;
    private const int ActiveJobsShown = 8;
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var statusService = UseService<ITendrilProcessStatusService>();
        var client = UseService<IClientProvider>();
        var tunnelService = UseService<ICloudflaredService>();
        var usage = UseService<AgentUsageService>();
        var config = UseService<ConfigService>();
        var copyToClipboard = UseClipboard();
        var navigator = UseNavigation();
        var refreshToken = UseRefreshToken();
        var processView = Context.UseTendrilProcess();
        var tunnelStatus = UseState(tunnelService.Status);
        var tunnelUrl = UseState<string?>(tunnelService.TunnelUrl);

        var usageQuery = UseQuery<AgentUsageSnapshot?, string>(
            config.CodingAgent,
            (agentId, ct) => usage.GetUsageAsync(agentId, ct),
            options: new QueryOptions { RefreshInterval = TimeSpan.FromSeconds(60) });

        // Same agent-output sheet as the Jobs app, opened from the Active Jobs card.
        var (outputSheet, showOutput) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            var job = jobService.GetJob(jobId);
            var title = job is not null ? $"{job.Type} {JobsApp.ExtractPlanId(job.PlanFile)}" : "Job Output";
            return new Sheet(
                () => isOpen.Set(false),
                new OutputSheet(jobId, jobService),
                title
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var (kpiSheet, showKpiDetail) = UseTrigger<string>((isOpen, kpiKey) =>
        {
            if (!isOpen.Value) return null;
            var currentToday = DateTime.UtcNow.Date;
            var currentFirstActivityMonth = new DateTime(currentToday.Year, currentToday.Month, 1).AddMonths(-(ActivityMonths - 1));
            var currentStats = planService.GetDashboardData(null);
            var currentActivity = planService.GetDashboardActivity(TrendMonthsBack);
            var currentPrDays = planService.GetCompletedPrsByDay((currentToday - currentFirstActivityMonth).Days + 1);
            var currentFeatureDays = planService.GetShippedFeaturesByDay(60);
            var title = GetKpiSheetTitle(kpiKey);
            return new Sheet(
                () => isOpen.Set(false),
                new KpiBreakdownSheet(kpiKey, currentStats, currentActivity, currentPrDays, currentFeatureDays, currentToday, planService),
                title
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        UseEffect(() => JobsApp.JobChangeHookDisposable(jobService, refreshToken));
        // Skip(1): the status stream is a BehaviorSubject and replays its
        // current value on subscribe, which would refresh in a loop.
        UseEffect(() => statusService.Status.Skip(1).Subscribe(_ => refreshToken.Refresh()));
        UseInterval(() => { refreshToken.Refresh(); },
            planService.IsDatabaseReady ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(2));
        UseEffect(() =>
        {
            void OnStatusChanged(TunnelStatus newStatus)
            {
                tunnelStatus.Set(newStatus);
                tunnelUrl.Set(tunnelService.TunnelUrl);
            }

            tunnelService.StatusChanged += OnStatusChanged;

            tunnelStatus.Set(tunnelService.Status);
            tunnelUrl.Set(tunnelService.TunnelUrl);

            return Disposable.Create(() => tunnelService.StatusChanged -= OnStatusChanged);
        });

        if (!planService.IsDatabaseReady)
        {
            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(2)
                   | Text.Muted("Loading Dashboard Data...");
        }

        var stats = planService.GetDashboardData(null);
        var activity = planService.GetDashboardActivity(TrendMonthsBack);
        // Status strip counts come from the same sources as the apps they
        // navigate to: plan counts as shown by the Drafts/Review apps and
        // the shell badges, job counts as shown by the Jobs app.
        var processStatus = statusService.Current;
        var jobs = jobService.GetJobs();

        var today = DateTime.UtcNow.Date;
        var firstActivityMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(ActivityMonths - 1));
        var prDays = planService.GetCompletedPrsByDay((today - firstActivityMonth).Days + 1);
        var featureDays = planService.GetShippedFeaturesByDay(60);

        var now = DateTime.Now;

        object? tunnelQr = null;
        object? tunnelMenu = null;
        if (tunnelStatus.Value == TunnelStatus.Connected && tunnelUrl.Value is { } tunnelAddress)
        {
            tunnelQr = new QRCode { Value = tunnelAddress, PixelSize = 160, ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium };
            tunnelMenu = TunnelUiHelper.BuildTunnelMenu(client, copyToClipboard, tunnelAddress, () =>
            {
                tunnelStatus.Set(TunnelStatus.Disabled);
                client.Toast("Tunnel stopped", "Deactivated");
                _ = tunnelService.DeactivateAsync();
            });
        }

        var dashboard = new TendrilDashboard(processView, new UpdateNoticeView(compact: true), tunnelQr, tunnelMenu)
            .DateText($"{now.ToString("dddd", CultureInfo.InvariantCulture)}, {Ordinal(now.Day)} {now.ToString("MMMM", CultureInfo.InvariantCulture)}")
            .Greeting(BuildGreeting(now))
            .Headline("What Are We Producing Today?")
            .DraftCount(processStatus.DraftCount)
            .InProgressCount(processStatus.JobCount)
            .ReviewCount(processStatus.ReviewCount)
            .CompletedCount(jobs.Count(j => j.Status == JobStatus.Completed))
            .FailedCount(jobs.Count(j => j.Status == JobStatus.Failed))
            .Kpis(BuildKpis(stats, activity, prDays, featureDays, today, usageQuery.Data))
            .Trend(BuildTrend(activity, today))
            .TrendWeekly(BuildWeeklyTrend(activity, today))
            .PullRequests(BuildMonthlyPullRequests(activity.Months))
            .PullRequestsWeekly(BuildWeeklyPullRequests(prDays, today))
            .Activity(BuildActivityMonths(prDays, firstActivityMonth))
            .Jobs(BuildActiveJobs(jobs, planService))
            .OnDrafts(() => navigator.Navigate<PlansApp>())
            .OnReview(() => navigator.Navigate<ReviewApp>())
            .OnJobs(() => navigator.Navigate<JobsApp>())
            .OnJob(showOutput)
            .OnSelectKpi(showKpiDetail);

        return new Fragment(dashboard, outputSheet, kpiSheet);
    }

    internal static string GetKpiSheetTitle(string kpiKey) => kpiKey switch
    {
        "featuresShipped" => "Features Shipped",
        "costPerFeature" => "Avg Cost Per Feature",
        "forecastMonth" => "Forecast This Month",
        "usageWindow" => "Agent Usage Window",
        "avgCostPlan" => "Avg Cost/Plan",
        _ => "KPI Breakdown"
    };

    internal static List<DashboardJobDto> BuildActiveJobs(List<JobItem> jobs, IPlanReaderService planService)
    {
        return jobs
            .Where(j => j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked)
            .Take(ActiveJobsShown)
            .Select(j =>
            {
                var planId = JobsApp.ExtractPlanId(j.PlanFile);
                if (string.IsNullOrEmpty(planId) && !string.IsNullOrEmpty(j.ReportedPlanId))
                    planId = j.ReportedPlanId;
                return new DashboardJobDto(
                    j.Id,
                    planId,
                    JobsApp.GetPromptDisplay(j, planService),
                    j.Status.ToString().ToLowerInvariant());
            })
            .ToList();
    }

    internal static string BuildGreeting(DateTime now)
    {
        var word = now.Hour switch
        {
            >= 5 and < 12 => "Morning",
            >= 12 and < 17 => "Afternoon",
            _ => "Evening"
        };
        var user = Environment.UserName;
        var name = string.IsNullOrWhiteSpace(user) ? null : char.ToUpperInvariant(user[0]) + user[1..];
        return name == null ? $"Good {word}!" : $"Good {word}, {name}!";
    }

    internal static string Ordinal(int day)
    {
        var suffix = day is 11 or 12 or 13
            ? "th"
            : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return day.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string MonthLabel(int month) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month);

    internal static List<DashboardMonthValueDto> BuildMonthlyPullRequests(
        IEnumerable<DashboardMonthStats> months, int count = 6)
    {
        return months
            .TakeLast(count)
            .Select(m => new DashboardMonthValueDto(
                MonthLabel(m.Month),
                m.PrsMerged,
                m.Year,
                m.Month,
                1,
                $"{m.Year:D4}-{m.Month:D2}-01"))
            .ToList();
    }

    internal static List<DashboardMonthValueDto> BuildWeeklyPullRequests(
        List<(DateOnly Date, int Count)> prDays, DateTime today, int weeks = 6)
    {
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var currentWeekMonday = DateOnly.FromDateTime(today).AddDays(-daysSinceMonday);
        var result = new List<DashboardMonthValueDto>(weeks);

        for (var i = weeks - 1; i >= 0; i--)
        {
            var weekStart = currentWeekMonday.AddDays(-i * 7);
            var weekEnd = weekStart.AddDays(6);
            var count = prDays.Where(p => p.Date >= weekStart && p.Date <= weekEnd).Sum(p => p.Count);
            var label = $"{MonthLabel(weekStart.Month)} {weekStart.Day}";
            result.Add(new DashboardMonthValueDto(
                label,
                count,
                weekStart.Year,
                weekStart.Month,
                weekStart.Day,
                weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return result;
    }

    internal static List<DashboardKpiDto> BuildKpis(
        DashboardModels stats,
        DashboardActivityStats activity,
        List<(DateOnly Date, int Count)> prDays,
        List<(DateOnly Date, int Count)> featureDays,
        DateTime today,
        AgentUsageSnapshot? usageSnapshot)
    {
        var kpis = new List<DashboardKpiDto>();

        // Card 1: Features shipped (absolute count, last 30 days vs previous 30 days)
        var last30Start = DateOnly.FromDateTime(today.AddDays(-29));
        var prev30Start = DateOnly.FromDateTime(today.AddDays(-59));
        var features30 = featureDays.Where(p => p.Date >= last30Start).Sum(p => p.Count);
        var prevFeatures30 = featureDays.Where(p => p.Date >= prev30Start && p.Date < last30Start).Sum(p => p.Count);
        kpis.Add(Kpi("Features shipped", FormatHelper.FormatCount(features30), features30, prevFeatures30, "featuresShipped",
            hint: "merged PRs and solved issues, last 30 days"));

        // Card 2: Avg cost per Feature
        var dailyCosts = activity.DailyCosts;
        if (dailyCosts != null && dailyCosts.Count > 0 && features30 > 0)
        {
            var cost30 = dailyCosts.Where(c => c.Date >= last30Start).Sum(c => c.TotalCost);
            var prevCost30 = dailyCosts.Where(c => c.Date >= prev30Start && c.Date < last30Start).Sum(c => c.TotalCost);
            var costPerFeature = cost30 / features30;
            var prevCostPerFeature = prevFeatures30 > 0 ? prevCost30 / prevFeatures30 : 0;
            var hint = $"{FormatHelper.FormatCost(cost30)} over {FormatHelper.FormatCount(features30)} features";
            kpis.Add(Kpi("Avg cost per Feature", FormatHelper.FormatCost(costPerFeature), costPerFeature, prevCostPerFeature, "costPerFeature", hint: hint));
        }
        else if (features30 == 0)
        {
            kpis.Add(new DashboardKpiDto("Avg cost per Feature", "n/a", Hint: "No features shipped in the last 30 days", Id: "costPerFeature"));
        }
        else
        {
            kpis.Add(new DashboardKpiDto("Avg cost per Feature", "n/a", Hint: "No cost data available", Id: "costPerFeature"));
        }

        // Card 3: Forecast This Month (unchanged)
        kpis.Add(BuildForecastKpi(activity.DailyCosts, today));

        // Card 4: Usage window (with fallback to avgCostPlan)
        if (usageSnapshot?.Windows is { Count: > 0 } windows)
        {
            var tightestWindow = windows.OrderBy(w => w.WindowMinutes).First();
            var label = $"{UsageWindowCalculator.FormatWindow(tightestWindow.WindowMinutes)} window";
            var value = $"{tightestWindow.RemainingPercent:0.#}% remaining";
            var hint = tightestWindow.ResetsAt.HasValue
                ? $"resets in {UsageWindowCalculator.FormatCountdown(tightestWindow.ResetsAt.Value - DateTimeOffset.UtcNow)}"
                : null;
            kpis.Add(new DashboardKpiDto(label, value, Hint: hint, Id: "usageWindow"));
        }
        else
        {
            kpis.Add(Kpi("Avg Cost/Plan", FormatHelper.FormatCost(stats.AvgCostPerPlan),
                stats.AvgCostPerPlan, activity.PrevWeekAvgCostPerPlan, "avgCostPlan"));
        }

        return kpis;
    }

    /// <summary>
    ///     What this month is heading for. No delta, because there is nothing prior to compare a projection against.
    /// </summary>
    internal static DashboardKpiDto BuildForecastKpi(List<DashboardDailyCost>? dailyCosts, DateTime today)
    {
        const string label = "Forecast This Month";

        var forecast = CostForecastCalculator.Project(dailyCosts ?? [], today);
        if (forecast.CalendarProjection is not { } totalProjection)
            return new DashboardKpiDto(label, "-", Hint: "No cost data in the last 30 days", Id: "forecastMonth");

        if (forecast.SubsidizedTokenPercent > 0)
        {
            var apiVal = forecast.ApiCalendarProjection is { } apiProjection && forecast.TotalApiSpend > 0
                ? FormatCost(apiProjection)
                : "$0";

            return new DashboardKpiDto(
                label,
                apiVal,
                Hint: $"{forecast.SubsidizedTokenPercent:0}% subsidized via subscription",
                Id: "forecastMonth");
        }

        return new DashboardKpiDto(
            label,
            FormatCost(totalProjection),
            Id: "forecastMonth");
    }

    private static (decimal Last, decimal Previous) LastTwo(
        List<DashboardMonthStats> completeMonths, Func<DashboardMonthStats, decimal> value)
    {
        var lastIndex = completeMonths.FindLastIndex(m => value(m) > 0);
        return lastIndex > 0
            ? (value(completeMonths[lastIndex]), value(completeMonths[lastIndex - 1]))
            : (0, 0);
    }

    internal static DashboardKpiDto Kpi(string label, string value, decimal current, decimal previous, string? id = null, string? hint = null)
    {
        if (previous <= 0 || current <= 0)
            return new DashboardKpiDto(label, value, Hint: hint, Id: id);

        var pct = (current - previous) / previous * 100m;
        var magnitude = Math.Abs(pct) >= 10
            ? Math.Round(Math.Abs(pct)).ToString("0", CultureInfo.InvariantCulture)
            : Math.Abs(pct).ToString("0.##", CultureInfo.InvariantCulture);
        var delta = (pct >= 0 ? "+" : "-") + magnitude + "%";
        return new DashboardKpiDto(label, value, delta, pct >= 0 ? "up" : "down", hint, Id: id);
    }

    private static string FormatCost(decimal cost) =>
        cost >= 100 ? FormatHelper.FormatCost(Math.Round(cost), 0) : FormatHelper.FormatCost(cost);

    /// <summary>Days the long range plots. A year of them, compared against the same day a year back.</summary>
    internal const int TrendDailyShownDays = 365;

    /// <summary>Days the short range plots, and the offset it compares against.</summary>
    internal const int TrendDailyWindowDays = 28;

    /// <summary>
    ///     The last twelve months as daily points. Null when no daily series is available.
    /// </summary>
    internal static DashboardTrendDto? BuildTrend(
        DashboardActivityStats activity, DateTime? todayOverride = null) =>
        BuildDailyTrend(activity, TrendDailyShownDays, todayOverride);

    /// <summary>
    ///     The last four weeks as daily points. Null when no daily series is available.
    /// </summary>
    internal static DashboardTrendDto? BuildWeeklyTrend(
        DashboardActivityStats activity, DateTime? todayOverride = null) =>
        BuildDailyTrend(activity, TrendDailyWindowDays, todayOverride);

    /// <summary>
    ///     One trend card's worth of contiguous daily points ending today, with a rolling 7 day mean.
    /// </summary>
    /// <remarks>
    ///     Returns null when neither daily series is present rather than falling back to weekly or
    ///     monthly buckets. Buckets cannot carry a true 7 day average or a date axis, and plotting them
    ///     under a "7-day average" label would be the misleading result this contract exists to avoid;
    ///     the widget renders no trend card at all instead.
    /// </remarks>
    internal static DashboardTrendDto? BuildDailyTrend(
        DashboardActivityStats activity,
        int days,
        DateTime? todayOverride = null)
    {
        if (activity.DailyCosts == null && activity.DailyPlans == null)
            return null;

        var today = DateOnly.FromDateTime((todayOverride ?? DateTime.UtcNow).Date);
        var costsByDay = activity.DailyCosts?.ToDictionary(d => d.Date, d => (double)d.Cost) ?? [];
        var plansByDay = activity.DailyPlans ?? [];

        // A mock or a fake supplies a daily series without saying where records begin. The earliest day
        // it holds is the best stand-in; leaving it null would blank every rolling point.
        var dataStart = activity.DailyDataStart ?? EarliestRecordedDay(costsByDay, plansByDay);

        var dates = new List<DateOnly>(days);
        for (var i = 0; i < days; i++)
            dates.Add(today.AddDays(-days + 1 + i));

        // Zero-filled, so a day with no rows is a plotted 0 rather than a missing point.
        double CostAt(DateOnly date) => costsByDay.GetValueOrDefault(date, 0.0);
        double PlansAt(DateOnly date) => plansByDay.GetValueOrDefault(date, 0);

        return new DashboardTrendDto(
            dates.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList(),
            dates.Select(CostAt).ToList(),
            dates.Select(PlansAt).ToList(),
            RollingAverageCalculator.Compute(dates, CostAt, dataStart),
            RollingAverageCalculator.Compute(dates, PlansAt, dataStart));
    }

    private static DateOnly? EarliestRecordedDay(
        Dictionary<DateOnly, double> costsByDay, Dictionary<DateOnly, int> plansByDay)
    {
        var recorded = costsByDay.Keys.Concat(plansByDay.Keys).ToList();
        return recorded.Count > 0 ? recorded.Min() : null;
    }

    internal static List<DashboardActivityMonthDto> BuildActivityMonths(
        List<(DateOnly Date, int Count)> prDays, DateTime firstMonth)
    {
        var byDay = prDays.ToDictionary(p => p.Date, p => p.Count);
        var months = new List<DashboardActivityMonthDto>(ActivityMonths);

        for (var i = 0; i < ActivityMonths; i++)
        {
            var monthStart = firstMonth.AddMonths(i);
            var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
            var offset = ((int)monthStart.DayOfWeek + 6) % 7;
            var weeks = new int[(offset + daysInMonth + 6) / 7];
            var days = new List<DashboardActivityDayDto>(daysInMonth);

            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = new DateOnly(monthStart.Year, monthStart.Month, day);
                var count = byDay.GetValueOrDefault(date, 0);
                weeks[(offset + day - 1) / 7] += count;
                days.Add(new DashboardActivityDayDto(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), count));
            }

            months.Add(new DashboardActivityMonthDto(MonthLabel(monthStart.Month), weeks.ToList(), days));
        }

        return months;
    }

}
