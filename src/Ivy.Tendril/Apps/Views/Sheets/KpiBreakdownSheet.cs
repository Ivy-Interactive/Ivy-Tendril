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
    List<(DateOnly Date, int Count)> featureDays,
    DateTime today,
    IPlanReaderService planService) : ViewBase
{
    public override object Build()
    {
        return kpiKey switch
        {
            "featuresShipped" or "dailyPrs" => BuildFeaturesShippedBreakdown(),
            "costPerFeature" or "avgCostMonth" => BuildCostPerFeatureBreakdown(),
            "forecastMonth" => BuildForecastMonthBreakdown(),
            "usageWindow" => BuildUsageWindowBreakdown(),
            "avgCostPlan" => BuildAvgCostPlanBreakdown(),
            _ => Layout.Vertical().Gap(4)
                 | Callout.Info($"No calculation breakdown available for key '{kpiKey}'.", "Unknown Metric")
        };
    }

    private object BuildFeaturesShippedBreakdown()
    {
        var last30Start = DateOnly.FromDateTime(today.AddDays(-29));
        var prev30Start = DateOnly.FromDateTime(today.AddDays(-59));
        var last30Count = featureDays.Where(p => p.Date >= last30Start).Sum(p => p.Count);
        var prev30Count = featureDays.Where(p => p.Date >= prev30Start && p.Date < last30Start).Sum(p => p.Count);
        var deltaText = CalculateDelta(last30Count, prev30Count);

        var details = new
        {
            Metric = "Features Shipped",
            Formula = "Merged PRs + solved issues, last 30 days",
            Last30DaysFeaturesShipped = FormatHelper.FormatCount(last30Count),
            Prior30DaysFeaturesShipped = FormatHelper.FormatCount(prev30Count),
            PeriodComparisonDelta = deltaText
        }
            .ToDetails()
            .Label(x => x.Metric, "Metric")
            .Label(x => x.Formula, "Formula")
            .Label(x => x.Last30DaysFeaturesShipped, "Last 30 Days")
            .Label(x => x.Prior30DaysFeaturesShipped, "Prior 30 Days")
            .Label(x => x.PeriodComparisonDelta, "30-Day Period Delta");

        var featureTable = featureDays
            .Where(d => d.Date >= last30Start)
            .OrderByDescending(d => d.Date)
            .Select(d => new
            {
                Date = d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Count = d.Count.ToString(CultureInfo.InvariantCulture)
            })
            .AsQueryable()
            .ToDataTable(x => x.Date)
            .Header(x => x.Date, "Date")
            .Header(x => x.Count, "Features")
            .Width(Size.Full())
            .Height(Size.Px(240))
            .Config(c =>
            {
                c.AllowSorting = true;
                c.SelectionMode = SelectionModes.None;
                c.ShowIndexColumn = false;
                c.ShowSearch = false;
            });

        var recentPrs = planService.GetRecentMergedPrs(50);
        object prTableContent = recentPrs.Count == 0
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
                .AsQueryable()
                .ToDataTable(x => x.Url)
                .Header(x => x.PlanId, "Plan")
                .Header(x => x.Title, "Title")
                .Header(x => x.Repository, "Repo")
                .Header(x => x.MergedDate, "Merged")
                .Header(x => x.Url, "PR URL")
                .Width(Size.Full())
                .Height(Size.Px(360))
                .Config(c =>
                {
                    c.AllowSorting = true;
                    c.SelectionMode = SelectionModes.None;
                    c.ShowIndexColumn = false;
                    c.ShowSearch = false;
                });

        return Layout.Vertical().Gap(4)
               | Callout.Info("Features Shipped counts merged PRs and solved issues without a PR over the last 30 days. A plan with three PRs counts three features; an issue-only plan counts one.", "Output Metric")
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Features by Day (Last 30 Days)")
                  | featureTable)
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Recent Merged Pull Requests")
                  | prTableContent);
    }

    private object BuildCostPerFeatureBreakdown()
    {
        var last30Start = DateOnly.FromDateTime(today.AddDays(-29));
        var prev30Start = DateOnly.FromDateTime(today.AddDays(-59));

        var dailyCosts = activity.DailyCosts ?? [];
        var features30 = featureDays.Where(p => p.Date >= last30Start).Sum(p => p.Count);
        var prevFeatures30 = featureDays.Where(p => p.Date >= prev30Start && p.Date < last30Start).Sum(p => p.Count);

        var cost30 = dailyCosts.Where(c => c.Date >= last30Start).Sum(c => c.Cost);
        var prevCost30 = dailyCosts.Where(c => c.Date >= prev30Start && c.Date < last30Start).Sum(c => c.Cost);

        var costPerFeature = features30 > 0 ? cost30 / features30 : 0m;
        var prevCostPerFeature = prevFeatures30 > 0 ? prevCost30 / prevFeatures30 : 0m;
        var deltaText = CalculateDelta(costPerFeature, prevCostPerFeature);

        var details = new
        {
            Metric = "Avg Cost per Feature",
            Formula = "30-day spend / 30-day features shipped",
            Last30DaysSpend = FormatHelper.FormatCost(cost30),
            Last30DaysFeatures = FormatHelper.FormatCount(features30),
            Last30DaysCostPerFeature = features30 > 0 ? FormatHelper.FormatCost(costPerFeature) : "n/a",
            Prior30DaysSpend = FormatHelper.FormatCost(prevCost30),
            Prior30DaysFeatures = FormatHelper.FormatCount(prevFeatures30),
            Prior30DaysCostPerFeature = prevFeatures30 > 0 ? FormatHelper.FormatCost(prevCostPerFeature) : "n/a",
            PeriodComparisonDelta = deltaText
        }
            .ToDetails()
            .Label(x => x.Metric, "Metric")
            .Label(x => x.Formula, "Formula")
            .Label(x => x.Last30DaysSpend, "Last 30 Days (Spend)")
            .Label(x => x.Last30DaysFeatures, "Last 30 Days (Features)")
            .Label(x => x.Last30DaysCostPerFeature, "Last 30 Days (Cost/Feature)")
            .Label(x => x.Prior30DaysSpend, "Prior 30 Days (Spend)")
            .Label(x => x.Prior30DaysFeatures, "Prior 30 Days (Features)")
            .Label(x => x.Prior30DaysCostPerFeature, "Prior 30 Days (Cost/Feature)")
            .Label(x => x.PeriodComparisonDelta, "30-Day Period Delta");

        var agentSection = BuildAgentBreakdownSection(30);

        return Layout.Vertical().Gap(4)
               | Callout.Info("Avg Cost per Feature divides 30-day spend by 30-day features shipped, so the card shows the exact quotient of the two cards beside it.", "Unit Cost")
               | details
               | agentSection;
    }

    private object BuildForecastMonthBreakdown()
    {
        var forecast = CostForecastCalculator.Project(activity.DailyCosts ?? [], today);
        var mtdRecords = (activity.DailyCosts ?? [])
            .Where(d => d.Date.Year == today.Year && d.Date.Month == today.Month)
            .ToList();
        var mtdTotalSpend = mtdRecords.Sum(d => d.Cost);
        var mtdApiSpend = mtdRecords.Sum(d => d.ApiCost);
        var mtdSubsidizedSpend = mtdRecords.Sum(d => d.SubsidizedCost);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var daysRemaining = Math.Max(0, daysInMonth - today.Day);

        var calloutMessage = forecast.SubsidizedTokenPercent > 0
            ? $"Forecast This Month projects month-end spend using daily activity over the last 30 days. {forecast.SubsidizedTokenPercent:0}% of tokens in this window were subsidized via subscription ({FormatCost(forecast.TotalSubsidizedSpend)} equivalent value) with {FormatCost(forecast.TotalApiSpend)} in direct API charges."
            : "Forecast This Month projects month-end spend using daily activity over the last 30 days. Direct API projections estimate billed out-of-pocket spend, while total projections reflect overall token market value.";

        var details = new
        {
            Metric = "Forecast This Month",
            DirectApiCalendarProjection = forecast.ApiCalendarProjection.HasValue ? FormatCost(forecast.ApiCalendarProjection.Value) : "$0",
            DirectApiActivityProjection = forecast.ApiActivityProjection.HasValue ? FormatCost(forecast.ApiActivityProjection.Value) : "$0",
            TotalCalendarProjection = forecast.CalendarProjection.HasValue ? FormatCost(forecast.CalendarProjection.Value) : "No data",
            TotalActivityProjection = forecast.ActivityProjection.HasValue ? FormatCost(forecast.ActivityProjection.Value) : "No data",
            SubsidizedTokenShare = $"{forecast.SubsidizedTokenPercent:0}% of tokens",
            SubsidizedCostShare = $"{forecast.SubsidizedCostPercent:0}% of value",
            MonthToDateApiSpend = FormatCost(mtdApiSpend),
            MonthToDateSubsidizedValue = FormatCost(mtdSubsidizedSpend),
            MonthToDateTotalSpend = FormatCost(mtdTotalSpend),
            ObservedCalendarDays = $"{forecast.CalendarDays} day(s)",
            ActiveSpendDays = $"{forecast.ActivityDays} day(s)",
            DaysRemainingInMonth = $"{daysRemaining} day(s)",
            DaysInCurrentMonth = $"{daysInMonth} day(s)"
        }
            .ToDetails()
            .Label(x => x.Metric, "Metric")
            .Label(x => x.DirectApiCalendarProjection, "Direct API Forecast (Calendar Basis)")
            .Label(x => x.DirectApiActivityProjection, "Direct API Forecast (Activity Basis)")
            .Label(x => x.TotalCalendarProjection, "Total Forecast (Calendar Basis)")
            .Label(x => x.TotalActivityProjection, "Total Forecast (Activity Basis)")
            .Label(x => x.SubsidizedTokenShare, "Subsidized Token Share")
            .Label(x => x.SubsidizedCostShare, "Subsidized Value Share")
            .Label(x => x.MonthToDateApiSpend, "Month-to-Date Direct API Spend")
            .Label(x => x.MonthToDateSubsidizedValue, "Month-to-Date Subsidized Value")
            .Label(x => x.MonthToDateTotalSpend, "Month-to-Date Total Market Value")
            .Label(x => x.ObservedCalendarDays, "Calendar Days in Window")
            .Label(x => x.ActiveSpendDays, "Active Days with Spend")
            .Label(x => x.DaysRemainingInMonth, "Days Remaining")
            .Label(x => x.DaysInCurrentMonth, "Days in Month");

        var cutoff = DateOnly.FromDateTime(today.AddDays(-29));
        var daily = (activity.DailyCosts ?? [])
            .Where(d => d.Date >= cutoff)
            .OrderByDescending(d => d.Date)
            .Select(d =>
            {
                var totalTokens = d.Tokens > 0 ? d.Tokens : d.ApiTokens + d.SubsidizedTokens;
                var subPct = totalTokens > 0
                    ? (double)d.SubsidizedTokens / totalTokens * 100.0
                    : (d.Cost > 0 ? (double)(d.SubsidizedCost / d.Cost) * 100.0 : 0.0);

                return new DailyCostRow
                {
                    Date = d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TotalSpend = FormatCost(d.Cost),
                    ApiSpend = FormatCost(d.ApiCost),
                    SubsidizedSpend = FormatCost(d.SubsidizedCost),
                    TotalTokens = FormatHelper.FormatCount(totalTokens),
                    SubsidizedPercent = $"{subPct:0}%"
                };
            })
            .ToList();

        object tableContent = daily.Count == 0
            ? Callout.Info("No daily spend records found in the 30-day window.", "No Data")
            : daily
                .AsQueryable()
                .ToDataTable(x => x.Date)
                .Header(x => x.Date, "Date")
                .Header(x => x.TotalSpend, "Total Spend")
                .Header(x => x.ApiSpend, "API Spend")
                .Header(x => x.SubsidizedSpend, "Subsidized Spend")
                .Header(x => x.TotalTokens, "Total Tokens")
                .Header(x => x.SubsidizedPercent, "Subsidized %")
                .Width(Size.Full())
                .Height(Size.Px(360))
                .Config(c =>
                {
                    c.AllowSorting = true;
                    c.SelectionMode = SelectionModes.None;
                    c.ShowIndexColumn = false;
                    c.ShowSearch = false;
                });

        var agentSection = BuildAgentBreakdownSection(30);

        return Layout.Vertical().Gap(4)
               | Callout.Info(calloutMessage, "Usage & Subsidized Analysis")
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Daily Spend (Last 30 Days)")
                  | tableContent)
               | agentSection;
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
                .AsQueryable()
                .ToDataTable(x => x.PlanId)
                .Header(x => x.PlanId, "Plan")
                .Header(x => x.Title, "Title")
                .Header(x => x.State, "State")
                .Header(x => x.CreatedDate, "Created")
                .Header(x => x.Tokens, "Tokens")
                .Header(x => x.Cost, "Total Cost")
                .Width(Size.Full())
                .Height(Size.Px(360))
                .Config(c =>
                {
                    c.AllowSorting = true;
                    c.SelectionMode = SelectionModes.None;
                    c.ShowIndexColumn = false;
                    c.ShowSearch = false;
                });

        var agentSection = BuildAgentBreakdownSection(7);

        return Layout.Vertical().Gap(4)
               | Callout.Info("Average Cost per Plan calculates the mean execution and promptware spend for plans created in the last 7 days that reached Completed, Failed, or Review state. Unpriced plans (e.g. subscription runs where cost is null) are excluded from the divisor so they do not artificially deflate the average.", "7-Day Rolling Average")
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Plans in Rolling Window (Last 7 Days)")
                  | tableContent)
               | agentSection;
    }

    private object BuildUsageWindowBreakdown()
    {
        return Layout.Vertical().Gap(4)
               | Callout.Info("Usage window information is not currently available. This typically means the agent provider does not report usage windows, or no usage data has been fetched yet.", "No Usage Data");
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
        public required string TotalSpend { get; init; }
        public required string ApiSpend { get; init; }
        public required string SubsidizedSpend { get; init; }
        public required string TotalTokens { get; init; }
        public required string SubsidizedPercent { get; init; }
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

    private sealed record AgentCostRow
    {
        public required string Agent { get; init; }
        public required string Spend { get; init; }
        public required string SpendShare { get; init; }
        public required string Tokens { get; init; }
        public required string TokenShare { get; init; }
        public required string Plans { get; init; }
    }

    private object BuildAgentBreakdownSection(int days)
    {
        var agentCosts = planService.GetAgentCostBreakdown(days);

        if (agentCosts.Count == 0)
        {
            return Layout.Vertical().Gap(2)
                   | Text.H4("Spend by Coding Agent")
                   | Callout.Info("No agent-attributed spend in this window.", "No Data");
        }

        var totalCost = agentCosts.Sum(a => a.Cost);
        var totalTokens = agentCosts.Sum(a => a.Tokens);
        var hasUnknown = agentCosts.Any(a => a.Agent == "Unknown");

        var rows = agentCosts
            .Select(a => new AgentCostRow
            {
                Agent = FormatHelper.FormatAgent(a.Agent),
                Spend = FormatCost(a.Cost),
                SpendShare = totalCost > 0 ? $"{(a.Cost / totalCost * 100m):0.#}%" : "N/A",
                Tokens = FormatHelper.FormatCount(a.Tokens),
                TokenShare = totalTokens > 0 ? $"{((decimal)a.Tokens / totalTokens * 100m):0.#}%" : "N/A",
                Plans = a.PlanCount.ToString(CultureInfo.InvariantCulture)
            })
            .AsQueryable()
            .ToDataTable(x => x.Agent)
            .Header(x => x.Agent, "Coding Agent")
            .Header(x => x.Spend, "Spend")
            .Header(x => x.SpendShare, "Spend Share")
            .Header(x => x.Tokens, "Tokens")
            .Header(x => x.TokenShare, "Token Share")
            .Header(x => x.Plans, "Plans")
            .Width(Size.Full())
            .Height(Size.Px(360))
            .Config(c =>
            {
                c.AllowSorting = true;
                c.SelectionMode = SelectionModes.None;
                c.ShowIndexColumn = false;
                c.ShowSearch = false;
            });

        var result = Layout.Vertical().Gap(2)
                     | Text.H4("Spend by Coding Agent")
                     | rows;

        if (hasUnknown)
        {
            result = result | Callout.Info("Rows predating agent capture appear as Unknown and cannot be attributed to a specific agent.", "Partial Attribution");
        }

        return result;
    }
}
