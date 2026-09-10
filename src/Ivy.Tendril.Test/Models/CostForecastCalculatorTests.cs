using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test.Models;

/// <summary>
/// Every case fixes <c>today</c> explicitly. The calculator takes no clock of its own, which is the
/// point: a month's projection is arithmetic, and arithmetic that depends on when the suite runs
/// cannot be pinned to a figure.
/// </summary>
public class CostForecastCalculatorTests
{
    /// <summary>August 2026, 31 days, so the month length is visible in the projections.</summary>
    private static readonly DateTime August = new(2026, 8, 31);

    private static DashboardDailyCost Day(DateTime today, int daysAgo, decimal cost) =>
        new(DateOnly.FromDateTime(today.AddDays(-daysAgo)), cost, 1000);

    private static List<DashboardDailyCost> Days(DateTime today, int count, decimal costPerDay) =>
        Enumerable.Range(0, count).Select(i => Day(today, i, costPerDay)).ToList();

    [Fact]
    public void ThirtyDaysOfSteadySpend_ProjectsTheSameOnBothBases()
    {
        var result = CostForecastCalculator.Project(Days(August, 30, 10m), August);

        // Every day had spend, so the two bases divide by the same 30 days and agree.
        Assert.Equal(310m, result.CalendarProjection);
        Assert.Equal(310m, result.ActivityProjection);
        Assert.Equal(30, result.CalendarDays);
        Assert.Equal(30, result.ActivityDays);
        Assert.Equal(300m, result.TotalSpend);
        Assert.Equal(31, result.DaysInMonth);
    }

    [Fact]
    public void TwelveDaysOfHistory_DividesByTwelve()
    {
        var result = CostForecastCalculator.Project(Days(August, 12, 10m), August);

        Assert.Equal(12, result.CalendarDays);
        Assert.Equal(120m / 12 * 31, result.CalendarProjection);
    }

    [Fact]
    public void SingleDay_ProjectsThatDayAcrossTheMonth()
    {
        var result = CostForecastCalculator.Project([Day(August, 0, 5m)], August);

        Assert.Equal(1, result.CalendarDays);
        Assert.Equal(1, result.ActivityDays);
        Assert.Equal(5m * 31, result.CalendarProjection);
        Assert.Equal(5m * 31, result.ActivityProjection);
    }

    [Fact]
    public void FirstRecordHoursOld_StillCountsAsOneWholeDay()
    {
        // A fractional divisor would project an absurd month from the first job of the day, so the
        // day count is floored at 1 rather than measured in hours.
        var today = new DateTime(2026, 8, 31, 21, 40, 0);
        var result = CostForecastCalculator.Project(
            [new DashboardDailyCost(DateOnly.FromDateTime(today), 5m, 1000)], today);

        Assert.Equal(1, result.CalendarDays);
        Assert.Equal(5m * 31, result.CalendarProjection);
    }

    [Fact]
    public void EmptyList_ReportsNoProjection()
    {
        var result = CostForecastCalculator.Project([], August);

        Assert.Null(result.CalendarProjection);
        Assert.Null(result.ActivityProjection);
        Assert.Equal(0, result.CalendarDays);
        Assert.Equal(0, result.ActivityDays);
        Assert.Equal(0m, result.TotalSpend);
    }

    [Fact]
    public void AllDaysZero_ReportsNoProjectionRatherThanZero()
    {
        // A window whose cost rows were all unknown sums to 0. That is not a $0.00 month, so the
        // caller has to get the same no-data answer as for an empty window.
        var result = CostForecastCalculator.Project(Days(August, 5, 0m), August);

        Assert.Null(result.CalendarProjection);
        Assert.Null(result.ActivityProjection);
        Assert.Equal(0, result.CalendarDays);
        Assert.Equal(0, result.ActivityDays);
        Assert.Equal(0m, result.TotalSpend);
    }

    [Fact]
    public void LongerMonth_ProjectsMoreFromTheSameDailyRate()
    {
        var february = new DateTime(2026, 2, 28);

        var longMonth = CostForecastCalculator.Project(Days(August, 10, 10m), August);
        var shortMonth = CostForecastCalculator.Project(Days(february, 10, 10m), february);

        Assert.Equal(31, longMonth.DaysInMonth);
        Assert.Equal(28, shortMonth.DaysInMonth);
        Assert.True(longMonth.CalendarProjection > shortMonth.CalendarProjection);
    }

    [Fact]
    public void BurstySpend_CalendarProjectionIsBelowActivityProjection()
    {
        // Three spend days scattered across a 20 day span. The gap between the two projections is the
        // whole reason both are reported: neither basis is right on its own here.
        var dailyCosts = new List<DashboardDailyCost>
        {
            Day(August, 19, 30m),
            Day(August, 10, 30m),
            Day(August, 2, 30m)
        };

        var result = CostForecastCalculator.Project(dailyCosts, August);

        Assert.Equal(20, result.CalendarDays);
        Assert.Equal(3, result.ActivityDays);
        Assert.True(result.CalendarProjection < result.ActivityProjection);
        Assert.Equal(90m / 20 * 31, result.CalendarProjection);
        Assert.Equal(90m / 3 * 31, result.ActivityProjection);
    }

    [Fact]
    public void HistoryOlderThanTheWindow_IsCappedAtThirtyDays()
    {
        var result = CostForecastCalculator.Project(Days(August, 100, 10m), August);

        // The repository only ever supplies 30 days, but a caller passing more must not get a divisor
        // that silently disagrees with the window the number was described as covering. The days
        // themselves are dropped too, so the spend being divided matches the divisor.
        Assert.Equal(30, result.CalendarDays);
        Assert.Equal(30, result.ActivityDays);
        Assert.Equal(300m, result.TotalSpend);
        Assert.Equal(310m, result.CalendarProjection);
    }

    [Fact]
    public void CalendarProjectionNeverExceedsActivityProjection()
    {
        // The relationship the two-number presentation rests on: the activity days are a subset of the
        // calendar days, so its divisor is never larger. Checked across shapes rather than asserted
        // once, since the clamping and windowing are where it could quietly stop holding.
        List<DashboardDailyCost>[] shapes =
        [
            Days(August, 1, 5m),
            Days(August, 30, 10m),
            Days(August, 100, 10m),
            [Day(August, 29, 1m), Day(August, 0, 500m)],
            [Day(August, 40, 999m), Day(August, 3, 1m)]
        ];

        foreach (var shape in shapes)
        {
            var result = CostForecastCalculator.Project(shape, August);
            Assert.True(result.CalendarProjection <= result.ActivityProjection);
        }
    }

    [Fact]
    public void HundredPercentApiSpend_ComputesIdenticalDualProjectionsAndZeroSubsidized()
    {
        var daily = new List<DashboardDailyCost>
        {
            new(new DateOnly(2026, 8, 31), 20m, 2000, ApiCost: 20m, ApiTokens: 2000),
            new(new DateOnly(2026, 8, 30), 10m, 1000, ApiCost: 10m, ApiTokens: 1000)
        };

        var result = CostForecastCalculator.Project(daily, August);

        Assert.Equal(2, result.CalendarDays);
        Assert.Equal(2, result.ActivityDays);
        Assert.Equal(30m / 2 * 31, result.CalendarProjection);
        Assert.Equal(30m / 2 * 31, result.ActivityProjection);
        Assert.Equal(result.CalendarProjection, result.ApiCalendarProjection);
        Assert.Equal(result.ActivityProjection, result.ApiActivityProjection);
        Assert.Equal(30m, result.TotalApiSpend);
        Assert.Equal(0m, result.TotalSubsidizedSpend);
        Assert.Equal(3000, result.TotalApiTokens);
        Assert.Equal(0, result.TotalSubsidizedTokens);
        Assert.Equal(0.0, result.SubsidizedTokenPercent);
        Assert.Equal(0.0, result.SubsidizedCostPercent);
    }

    [Fact]
    public void HundredPercentSubsidized_ComputesTotalProjectionWithNullApiProjection()
    {
        var daily = new List<DashboardDailyCost>
        {
            new(new DateOnly(2026, 8, 31), 25m, 5000, ApiCost: 0m, ApiTokens: 0, SubsidizedCost: 25m, SubsidizedTokens: 5000),
            new(new DateOnly(2026, 8, 30), 15m, 3000, ApiCost: 0m, ApiTokens: 0, SubsidizedCost: 15m, SubsidizedTokens: 3000)
        };

        var result = CostForecastCalculator.Project(daily, August);

        Assert.Equal(2, result.CalendarDays);
        Assert.Equal(2, result.ActivityDays);
        Assert.NotNull(result.CalendarProjection);
        Assert.NotNull(result.ActivityProjection);
        Assert.Null(result.ApiCalendarProjection);
        Assert.Null(result.ApiActivityProjection);
        Assert.Equal(0m, result.TotalApiSpend);
        Assert.Equal(40m, result.TotalSubsidizedSpend);
        Assert.Equal(0, result.TotalApiTokens);
        Assert.Equal(8000, result.TotalSubsidizedTokens);
        Assert.Equal(100.0, result.SubsidizedTokenPercent);
        Assert.Equal(100.0, result.SubsidizedCostPercent);
    }

    [Fact]
    public void MixedUsage_CalculatesAccurateSubsidizedPercentsAndDualProjections()
    {
        var daily = new List<DashboardDailyCost>
        {
            // Day 1: 10 API spend (1000 tokens), 30 subsidized value (3000 tokens) -> Total 40 cost, 4000 tokens
            new(new DateOnly(2026, 8, 31), 40m, 4000, ApiCost: 10m, ApiTokens: 1000, SubsidizedCost: 30m, SubsidizedTokens: 3000),
            // Day 2: 0 API spend, 10 subsidized value (1000 tokens) -> Total 10 cost, 1000 tokens
            new(new DateOnly(2026, 8, 30), 10m, 1000, ApiCost: 0m, ApiTokens: 0, SubsidizedCost: 10m, SubsidizedTokens: 1000)
        };

        var result = CostForecastCalculator.Project(daily, August);

        Assert.Equal(2, result.CalendarDays);
        Assert.Equal(2, result.ActivityDays);
        Assert.Equal(50m, result.TotalSpend);
        Assert.Equal(10m, result.TotalApiSpend);
        Assert.Equal(40m, result.TotalSubsidizedSpend);
        Assert.Equal(1000, result.TotalApiTokens);
        Assert.Equal(4000, result.TotalSubsidizedTokens);

        // 4000 subsidized out of 5000 total tokens = 80%
        Assert.Equal(80.0, result.SubsidizedTokenPercent);
        // 40 subsidized out of 50 total spend = 80%
        Assert.Equal(80.0, result.SubsidizedCostPercent);

        // Total projections
        Assert.Equal(50m / 2 * 31, result.CalendarProjection);
        Assert.Equal(50m / 2 * 31, result.ActivityProjection);

        // API projections: 10 API spend across 2 calendar days, 1 active API day
        Assert.Equal(10m / 2 * 31, result.ApiCalendarProjection);
        Assert.Equal(10m / 1 * 31, result.ApiActivityProjection);
    }

    [Fact]
    public void EmptyAndZeroCostWindows_HandleSubsidizedFieldsSafely()
    {
        var emptyResult = CostForecastCalculator.Project([], August);
        Assert.Null(emptyResult.CalendarProjection);
        Assert.Null(emptyResult.ApiCalendarProjection);
        Assert.Equal(0m, emptyResult.TotalSpend);
        Assert.Equal(0m, emptyResult.TotalApiSpend);
        Assert.Equal(0m, emptyResult.TotalSubsidizedSpend);
        Assert.Equal(0, emptyResult.SubsidizedTokenPercent);
        Assert.Equal(0, emptyResult.SubsidizedCostPercent);

        var zeroDaily = new List<DashboardDailyCost>
        {
            new(new DateOnly(2026, 8, 31), 0m, 0),
            new(new DateOnly(2026, 8, 30), 0m, 0)
        };
        var zeroResult = CostForecastCalculator.Project(zeroDaily, August);
        Assert.Null(zeroResult.CalendarProjection);
        Assert.Null(zeroResult.ApiCalendarProjection);
        Assert.Equal(0m, zeroResult.TotalSpend);
        Assert.Equal(0, zeroResult.SubsidizedTokenPercent);
        Assert.Equal(0, zeroResult.SubsidizedCostPercent);
    }
}
