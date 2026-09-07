using System.Globalization;
using Ivy.Tendril.Database;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Apps.Views.Sheets;

/// <summary>
/// Displays detailed calculation breakdown, formula explanation, comparison window,
/// and underlying data records for each of the four dashboard KPI metrics.
/// </summary>
public class KpiBreakdownSheet(
    string kpiKey,
    DashboardModels stats,
    DashboardActivityStats activity,
    List<(DateOnly Date, int Count)> prDays,
    DateTime today,
    IPlanReaderService planService) : ViewBase
{
    public override object Build()
    {
        return kpiKey switch
        {
            "dailyPrs" => BuildDailyPrsBreakdown(),
            "avgCostMonth" => BuildAvgCostMonthBreakdown(),
            "forecastMonth" => BuildForecastMonthBreakdown(),
            "avgCostPlan" => BuildAvgCostPlanBreakdown(),
            _ => Layout.Vertical().Gap(4)
                 | Callout.Info($"No calculation breakdown available for key '{kpiKey}'.", "Unknown Metric")
        };
    }

    private object BuildDailyPrsBreakdown()
    {
        var last30Start = DateOnly.FromDateTime(today.AddDays(-29));
        var prev30Start = DateOnly.FromDateTime(today.AddDays(-59));
        var last30Count = prDays.Where(p => p.Date >= last30Start).Sum(p => p.Count);
        var prev30Count = prDays.Where(p => p.Date >= prev30Start && p.Date < last30Start).Sum(p => p.Count);
        var dailyPrs = last30Count / 30m;
        var prevDailyPrs = prev30Count / 30m;
        var deltaText = CalculateDelta(dailyPrs, prevDailyPrs);

        var details = new
        {
            Metric = "Average Daily Pull Requests",
            Formula = "Total PRs merged in last 30 calendar days / 30",
            Last30DaysMergedPrs = last30Count.ToString(CultureInfo.InvariantCulture),
            Last30DaysDailyAverage = dailyPrs.ToString("0.#", CultureInfo.InvariantCulture),
            Prior30DaysMergedPrs = prev30Count.ToString(CultureInfo.InvariantCulture),
            Prior30DaysDailyAverage = prevDailyPrs.ToString("0.#", CultureInfo.InvariantCulture),
            PeriodComparisonDelta = deltaText
        }
            .ToDetails()
            .Label(x => x.Metric, "Metric")
            .Label(x => x.Formula, "Formula")
            .Label(x => x.Last30DaysMergedPrs, "Last 30 Days (Merged PRs)")
            .Label(x => x.Last30DaysDailyAverage, "Last 30 Days (Daily Avg)")
            .Label(x => x.Prior30DaysMergedPrs, "Prior 30 Days (Merged PRs)")
            .Label(x => x.Prior30DaysDailyAverage, "Prior 30 Days (Daily Avg)")
            .Label(x => x.PeriodComparisonDelta, "30-Day Period Delta");

        var recentPrs = planService.GetRecentMergedPrs(50);
        object tableContent = recentPrs.Count == 0
            ? Callout.Info("No merged pull requests recorded yet.", "No Data")
            : recentPrs
                .Select(p => new PrRow
                {
                    PlanId = p.PlanId.ToString("D5", CultureInfo.InvariantCulture),
                    Title = p.PlanTitle,
                    Repository = string.IsNullOrEmpty(p.Repo) ? "None" : Path.GetFileName(p.Repo),
                    MergedDate = p.MergedDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    Url = p.PrUrl
                })
                .ToTable()
                .Header(x => x.PlanId, "Plan")
                .Header(x => x.Title, "Title")
                .Header(x => x.Repository, "Repo")
                .Header(x => x.MergedDate, "Merged")
                .Header(x => x.Url, "PR URL")
                .Width(Size.Full());

        return Layout.Vertical().Gap(4)
               | Callout.Info("Average Daily PRs measures pull request delivery throughput across a rolling 30-day window. Total merged pull requests from the last 30 calendar days are divided by 30.", "Throughput Metric")
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Recent Merged Pull Requests")
                  | tableContent);
    }

    private object BuildAvgCostMonthBreakdown()
    {
        var completeMonths = activity.Months.Count > 0
            ? activity.Months.Take(activity.Months.Count - 1).ToList()
            : [];

        var costMonths = completeMonths.TakeLast(6).Where(m => m.Cost > 0).ToList();
        var avgMonthCost = costMonths.Count > 0
            ? costMonths.Average(m => m.Cost)
            : (activity.Months.Count > 0 ? activity.Months[^1].Cost : 0);
        var (lastCost, prevCost) = LastTwo(completeMonths, m => m.Cost);
        var deltaText = CalculateDelta(lastCost, prevCost);

        var details = new
        {
            Metric = "Average Cost per Month",
            Methodology = "Arithmetic mean of up to 6 complete historical months with spend > $0",
            AverageCost = FormatCost(avgMonthCost),
            IncludedMonthsCount = $"{costMonths.Count} completed month(s)",
            LastCompletedMonthSpend = FormatCost(lastCost),
            PriorCompletedMonthSpend = FormatCost(prevCost),
            MonthOverMonthDelta = deltaText
        }
            .ToDetails()
            .Label(x => x.Metric, "Metric")
            .Label(x => x.Methodology, "Methodology")
            .Label(x => x.AverageCost, "6-Month Historical Avg")
            .Label(x => x.IncludedMonthsCount, "Completed Months in Divisor")
            .Label(x => x.LastCompletedMonthSpend, "Last Completed Month")
            .Label(x => x.PriorCompletedMonthSpend, "Prior Completed Month")
            .Label(x => x.MonthOverMonthDelta, "Month-over-Month Delta");

        var monthRows = activity.Months
            .Select((m, idx) =>
            {
                var isInFlight = idx == activity.Months.Count - 1;
                var isIncluded = costMonths.Contains(m);
                var status = isInFlight
                    ? "Excluded (In Flight)"
                    : isIncluded
                        ? "Included in Divisor"
                        : "Excluded ($0 spend)";

                return new MonthRow
                {
                    Month = $"{m.Year}-{m.Month:D2}",
                    Spend = FormatCost(m.Cost),
                    Tokens = FormatHelper.FormatCount(m.Tokens),
                    PlansCreated = m.PlansCreated.ToString(CultureInfo.InvariantCulture),
                    PrsMerged = m.PrsMerged.ToString(CultureInfo.InvariantCulture),
                    Status = status
                };
            })
            .ToList();

        var table = monthRows
            .ToTable()
            .Header(x => x.Month, "Month")
            .Header(x => x.Spend, "Spend")
            .Header(x => x.Tokens, "Tokens")
            .Header(x => x.PlansCreated, "Plans")
            .Header(x => x.PrsMerged, "PRs Merged")
            .Header(x => x.Status, "Divisor Status")
            .Width(Size.Full());

        return Layout.Vertical().Gap(4)
               | Callout.Info("Average Cost/Month shows retrospective monthly spend across complete historical calendar months, strictly excluding the currently in-flight month to prevent partial-month bias.", "Retrospective Spend")
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Historical Monthly Breakdown")
                  | table);
    }

    private object BuildForecastMonthBreakdown()
    {
        var forecast = CostForecastCalculator.Project(activity.DailyCosts ?? [], today);
        var mtdSpend = activity.DailyCosts?
            .Where(d => d.Date.Year == today.Year && d.Date.Month == today.Month)
            .Sum(d => d.Cost) ?? 0m;
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var daysRemaining = Math.Max(0, daysInMonth - today.Day);

        var details = new
        {
            Metric = "Forecast This Month",
            MonthToDateSpend = FormatCost(mtdSpend),
            DaysRemainingInMonth = $"{daysRemaining} day(s)",
            DaysInCurrentMonth = $"{daysInMonth} day(s)",
            ObservedCalendarDays = $"{forecast.CalendarDays} day(s)",
            ActiveSpendDays = $"{forecast.ActivityDays} day(s)",
            TotalSpendInWindow = FormatCost(forecast.TotalSpend),
            CalendarBasisProjection = forecast.CalendarProjection.HasValue ? FormatCost(forecast.CalendarProjection.Value) : "No data",
            ActivityBasisProjection = forecast.ActivityProjection.HasValue ? FormatCost(forecast.ActivityProjection.Value) : "No data"
        }
            .ToDetails()
            .Label(x => x.Metric, "Metric")
            .Label(x => x.MonthToDateSpend, "Month-to-Date Spend")
            .Label(x => x.DaysRemainingInMonth, "Days Remaining")
            .Label(x => x.DaysInCurrentMonth, "Days in Month")
            .Label(x => x.ObservedCalendarDays, "Calendar Days in Window")
            .Label(x => x.ActiveSpendDays, "Active Days with Spend")
            .Label(x => x.TotalSpendInWindow, "Total Window Spend")
            .Label(x => x.CalendarBasisProjection, "Calendar Basis (Lower Bound)")
            .Label(x => x.ActivityBasisProjection, "Activity Basis (Upper Bound)");

        var cutoff = DateOnly.FromDateTime(today.AddDays(-29));
        var daily = (activity.DailyCosts ?? [])
            .Where(d => d.Date >= cutoff)
            .OrderByDescending(d => d.Date)
            .Select(d => new DailyCostRow
            {
                Date = d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Spend = FormatCost(d.Cost),
                Tokens = FormatHelper.FormatCount(d.Tokens)
            })
            .ToList();

        object tableContent = daily.Count == 0
            ? Callout.Info("No daily spend records found in the 30-day window.", "No Data")
            : daily
                .ToTable()
                .Header(x => x.Date, "Date")
                .Header(x => x.Spend, "Daily Spend")
                .Header(x => x.Tokens, "Tokens")
                .Width(Size.Full());

        return Layout.Vertical().Gap(4)
               | Callout.Info("Forecast This Month projects month-end spend using daily activity over the last 30 days. Dual projections indicate uncertainty: Calendar Basis assumes idle days continue at the observed rate, while Activity Basis projects from active spend days only.", "Dual Projections")
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Daily Spend (Last 30 Days)")
                  | tableContent);
    }

    private object BuildAvgCostPlanBreakdown()
    {
        var currentAvg = stats.AvgCostPerPlan;
        var prevAvg = activity.PrevWeekAvgCostPerPlan;
        var deltaText = CalculateDelta(currentAvg, prevAvg);

        var details = new
        {
            Metric = "Average Cost per Plan",
            RollingWindow = "7 days (plans created in last 7 days)",
            EligibleStates = "Completed, Failed, Review",
            Current7DayAverage = FormatHelper.FormatCost(currentAvg),
            Prior7DayAverage = FormatHelper.FormatCost(prevAvg),
            PeriodDelta = deltaText
        }
            .ToDetails()
            .Label(x => x.Metric, "Metric")
            .Label(x => x.RollingWindow, "Window")
            .Label(x => x.EligibleStates, "Plan States Included")
            .Label(x => x.Current7DayAverage, "Current 7-Day Average")
            .Label(x => x.Prior7DayAverage, "Prior 7-Day Average (Days 8-14)")
            .Label(x => x.PeriodDelta, "7-Day Period Delta");

        var recentPlans = planService.GetRecentPlanCosts(7);
        object tableContent = recentPlans.Count == 0
            ? Callout.Info("No plans created in the 7-day rolling window.", "No Data")
            : recentPlans
                .Select(p => new PlanCostRow
                {
                    PlanId = p.PlanId.ToString("D5", CultureInfo.InvariantCulture),
                    Title = p.PlanTitle,
                    State = p.State,
                    CreatedDate = p.CreatedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Tokens = FormatHelper.FormatCount(p.Tokens),
                    Cost = p.Cost.HasValue ? FormatHelper.FormatCost(p.Cost.Value) : "Unpriced"
                })
                .ToTable()
                .Header(x => x.PlanId, "Plan")
                .Header(x => x.Title, "Title")
                .Header(x => x.State, "State")
                .Header(x => x.CreatedDate, "Created")
                .Header(x => x.Tokens, "Tokens")
                .Header(x => x.Cost, "Total Cost")
                .Width(Size.Full());

        return Layout.Vertical().Gap(4)
               | Callout.Info("Average Cost per Plan calculates the mean execution and promptware spend for plans created in the last 7 days that reached Completed, Failed, or Review state. Unpriced plans (e.g. subscription runs where cost is null) are excluded from the divisor so they do not artificially deflate the average.", "7-Day Rolling Average")
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Plans in Rolling Window (Last 7 Days)")
                  | tableContent);
    }

    private static (decimal Last, decimal Previous) LastTwo(
        List<DashboardMonthStats> completeMonths, Func<DashboardMonthStats, decimal> value)
    {
        var lastIndex = completeMonths.FindLastIndex(m => value(m) > 0);
        return lastIndex > 0
            ? (value(completeMonths[lastIndex]), value(completeMonths[lastIndex - 1]))
            : (0, 0);
    }

    private static string CalculateDelta(decimal current, decimal previous)
    {
        if (previous <= 0 || current <= 0) return "N/A";
        var pct = (current - previous) / previous * 100m;
        var magnitude = Math.Abs(pct) >= 10
            ? Math.Round(Math.Abs(pct)).ToString("0", CultureInfo.InvariantCulture)
            : Math.Abs(pct).ToString("0.##", CultureInfo.InvariantCulture);
        return (pct >= 0 ? "+" : "-") + magnitude + "%";
    }

    private static string FormatCost(decimal cost) =>
        cost >= 100 ? FormatHelper.FormatCost(Math.Round(cost), 0) : FormatHelper.FormatCost(cost);

    private sealed record PrRow
    {
        public required string PlanId { get; init; }
        public required string Title { get; init; }
        public required string Repository { get; init; }
        public required string MergedDate { get; init; }
        public required string Url { get; init; }
    }

    private sealed record MonthRow
    {
        public required string Month { get; init; }
        public required string Spend { get; init; }
        public required string Tokens { get; init; }
        public required string PlansCreated { get; init; }
        public required string PrsMerged { get; init; }
        public required string Status { get; init; }
    }

    private sealed record DailyCostRow
    {
        public required string Date { get; init; }
        public required string Spend { get; init; }
        public required string Tokens { get; init; }
    }

    private sealed record PlanCostRow
    {
        public required string PlanId { get; init; }
        public required string Title { get; init; }
        public required string State { get; init; }
        public required string CreatedDate { get; init; }
        public required string Tokens { get; init; }
        public required string Cost { get; init; }
    }
}
