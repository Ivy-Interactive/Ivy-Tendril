using System.Globalization;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps;

[App(title: "Dashboard", icon: Icons.ChartBar, group: ["Apps"], order: Constants.Dashboard)]
public class DashboardApp : ViewBase
{
    private const int ActivityMonths = 16;
    private const int TrendMonthsBack = 24;

    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var refreshToken = UseRefreshToken();
        var processView = Context.UseTendrilProcess();
        UseInterval(() => { refreshToken.Refresh(); },
            planService.IsDatabaseReady ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(2));

        if (!planService.IsDatabaseReady)
        {
            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(2)
                   | Text.Muted("Loading Dashboard Data...");
        }

        var stats = planService.GetDashboardData(null);
        var activity = planService.GetDashboardActivity(TrendMonthsBack);

        if (stats.TotalCount == 0 && activity.Months.All(m => m.PlansCreated == 0))
        {
            return new NoContentView("No plans yet", "Create your first plan to get started", new NewPlanButton().Width(Size.Fit()));
        }

        var today = DateTime.UtcNow.Date;
        var firstActivityMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(ActivityMonths - 1));
        var prDays = planService.GetCompletedPrsByDay((today - firstActivityMonth).Days + 1);

        var now = DateTime.Now;

        return new TendrilDashboard(processView)
            .DateText($"{now.ToString("dddd", CultureInfo.InvariantCulture)}, {Ordinal(now.Day)} {now.ToString("MMMM", CultureInfo.InvariantCulture)}")
            .Greeting(BuildGreeting(now))
            .Headline("What Are We Producing Today?")
            .DraftCount(stats.DraftCount)
            .InProgressCount(stats.InProgressCount)
            .ReviewCount(stats.ReviewCount)
            .CompletedCount(stats.CompletedCount)
            .FailedCount(stats.FailedCount)
            .Kpis(BuildKpis(stats, activity, prDays, today))
            .Trend(BuildTrend(activity, today.Year))
            .PullRequests(activity.Months
                .TakeLast(6)
                .Select(m => new DashboardMonthValueDto(MonthLabel(m.Month), m.PrsMerged))
                .ToList())
            .Activity(BuildActivityMonths(prDays, firstActivityMonth));
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

    internal static DashboardTrendDto BuildTrend(DashboardActivityStats activity, int year)
    {
        var thisYear = activity.Months.Where(m => m.Year == year).OrderBy(m => m.Month).ToList();
        var lastYear = activity.Months.Where(m => m.Year == year - 1).OrderBy(m => m.Month).ToList();

        return new DashboardTrendDto(
            Enumerable.Range(1, 12).Select(MonthLabel).ToList(),
            thisYear.Select(m => (double)m.Cost).ToList(),
            lastYear.Select(m => (double)m.Cost).ToList(),
            thisYear.Select(m => (double)m.PlansCreated).ToList(),
            lastYear.Select(m => (double)m.PlansCreated).ToList());
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
