using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Apps.Jobs.Sheets;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
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
    private const int TrendMonthsShown = 12;
    private const int ActiveJobsShown = 8;

    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var statusService = UseService<ITendrilProcessStatusService>();
        var client = UseService<IClientProvider>();
        var tunnelService = UseService<ICloudflaredService>();
        var copyToClipboard = UseClipboard();
        var navigator = UseNavigation();
        var refreshToken = UseRefreshToken();
        var processView = Context.UseTendrilProcess();
        var tunnelStatus = UseState(tunnelService.Status);
        var tunnelUrl = UseState<string?>(tunnelService.TunnelUrl);

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

        if (stats.TotalCount == 0 && activity.Months.All(m => m.PlansCreated == 0))
        {
            return new NoContentView("No plans yet", "Create your first plan to get started", new NewPlanButton().Width(Size.Fit()));
        }

        var today = DateTime.UtcNow.Date;
        var firstActivityMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(ActivityMonths - 1));
        var prDays = planService.GetCompletedPrsByDay((today - firstActivityMonth).Days + 1);

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
            .Kpis(BuildKpis(stats, activity, prDays, today))
            .Trend(BuildTrend(activity))
            .PullRequests(activity.Months
                .TakeLast(6)
                .Select(m => new DashboardMonthValueDto(MonthLabel(m.Month), m.PrsMerged))
                .ToList())
            .Activity(BuildActivityMonths(prDays, firstActivityMonth))
            .Jobs(BuildActiveJobs(jobs, planService))
            .OnDrafts(() => navigator.Navigate<DraftsApp>())
            .OnReview(() => navigator.Navigate<ReviewApp>())
            .OnJobs(() => navigator.Navigate<JobsApp>())
            .OnJob(showOutput);

        return new Fragment(dashboard, outputSheet);
    }

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

    private static string BuildGreeting(DateTime now)
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

    internal static List<DashboardKpiDto> BuildKpis(
        DashboardModels stats,
        DashboardActivityStats activity,
        List<(DateOnly Date, int Count)> prDays,
        DateTime today)
    {
        var kpis = new List<DashboardKpiDto>();

        // Daily PR average over the last 30 days, compared to the 30 days before that.
        var last30Start = DateOnly.FromDateTime(today.AddDays(-29));
        var prev30Start = DateOnly.FromDateTime(today.AddDays(-59));
        var dailyPrs = prDays.Where(p => p.Date >= last30Start).Sum(p => p.Count) / 30m;
        var prevDailyPrs = prDays.Where(p => p.Date >= prev30Start && p.Date < last30Start).Sum(p => p.Count) / 30m;
        kpis.Add(Kpi("Avg Daily PR count", dailyPrs.ToString("0.#", CultureInfo.InvariantCulture), dailyPrs, prevDailyPrs));

        // Monthly cost/token averages over recent complete months with data; the delta
        // compares the two most recent complete months.
        var completeMonths = activity.Months.Count > 0
            ? activity.Months.Take(activity.Months.Count - 1).ToList()
            : [];

        var costMonths = completeMonths.TakeLast(6).Where(m => m.Cost > 0).ToList();
        var avgMonthCost = costMonths.Count > 0
            ? costMonths.Average(m => m.Cost)
            : activity.Months.Count > 0 ? activity.Months[^1].Cost : 0;
        var (lastCost, prevCost) = LastTwo(completeMonths, m => m.Cost);
        kpis.Add(Kpi("Avg Cost/Month", FormatCost(avgMonthCost), lastCost, prevCost));

        var tokenMonths = completeMonths.TakeLast(6).Where(m => m.Tokens > 0).ToList();
        var avgMonthTokens = tokenMonths.Count > 0
            ? (long)tokenMonths.Average(m => m.Tokens)
            : activity.Months.Count > 0 ? activity.Months[^1].Tokens : 0;
        var (lastTokens, prevTokens) = LastTwo(completeMonths, m => m.Tokens);
        kpis.Add(Kpi("Avg Tokens/Month", FormatTokenAverage(avgMonthTokens), lastTokens, prevTokens));

        kpis.Add(Kpi("Avg Cost/Plan", FormatHelper.FormatCost(stats.AvgCostPerPlan),
            stats.AvgCostPerPlan, activity.PrevWeekAvgCostPerPlan));

        return kpis;
    }

    private static (decimal Last, decimal Previous) LastTwo(
        List<DashboardMonthStats> completeMonths, Func<DashboardMonthStats, decimal> value)
    {
        var lastIndex = completeMonths.FindLastIndex(m => value(m) > 0);
        return lastIndex > 0
            ? (value(completeMonths[lastIndex]), value(completeMonths[lastIndex - 1]))
            : (0, 0);
    }

    internal static DashboardKpiDto Kpi(string label, string value, decimal current, decimal previous)
    {
        if (previous <= 0 || current <= 0)
            return new DashboardKpiDto(label, value);

        var pct = (current - previous) / previous * 100m;
        var magnitude = Math.Abs(pct) >= 10
            ? Math.Round(Math.Abs(pct)).ToString("0", CultureInfo.InvariantCulture)
            : Math.Abs(pct).ToString("0.##", CultureInfo.InvariantCulture);
        var delta = (pct >= 0 ? "+" : "-") + magnitude + "%";
        return new DashboardKpiDto(label, value, delta, pct >= 0 ? "up" : "down");
    }

    private static string FormatCost(decimal cost) =>
        cost >= 100 ? FormatHelper.FormatCost(Math.Round(cost), 0) : FormatHelper.FormatCost(cost);

    private static string FormatTokenAverage(long tokens) =>
        tokens >= 1_000_000
            ? FormatHelper.FormatTokens((int)Math.Min(tokens, int.MaxValue))
            : FormatHelper.FormatCount(tokens);

    internal static DashboardTrendDto BuildTrend(DashboardActivityStats activity)
    {
        var all = activity.Months;
        var start = Math.Max(0, all.Count - TrendMonthsShown);
        var window = all.Skip(start).ToList();

        var prevCost = new List<double?>(window.Count);
        var prevPlans = new List<double?>(window.Count);
        for (var i = 0; i < window.Count; i++)
        {
            var prevIndex = start + i - 12;
            prevCost.Add(prevIndex >= 0 ? (double)all[prevIndex].Cost : null);
            prevPlans.Add(prevIndex >= 0 ? all[prevIndex].PlansCreated : null);
        }

        return new DashboardTrendDto(
            window.Select(m => MonthLabel(m.Month)).ToList(),
            window.Select(m => (double)m.Cost).ToList(),
            window.Select(m => (double)m.PlansCreated).ToList(),
            prevCost,
            prevPlans);
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

            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = new DateOnly(monthStart.Year, monthStart.Month, day);
                if (byDay.TryGetValue(date, out var count))
                    weeks[(offset + day - 1) / 7] += count;
            }

            months.Add(new DashboardActivityMonthDto(MonthLabel(monthStart.Month), weeks.ToList()));
        }

        return months;
    }
}
