using Ivy.Tendril.Apps;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Test.Apps;

public class DashboardAppViewModelTests
{
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(31, "31st")]
    public void Ordinal_FormatsDaySuffix(int day, string expected)
    {
        Assert.Equal(expected, DashboardApp.Ordinal(day));
    }

    [Fact]
    public void Kpi_FormatsPositiveDelta()
    {
        var kpi = DashboardApp.Kpi("Label", "54", 54m, 24.2m);

        Assert.Equal("+123%", kpi.Delta);
        Assert.Equal("up", kpi.Direction);
    }

    [Fact]
    public void Kpi_FormatsSmallNegativeDeltaWithDecimals()
    {
        var kpi = DashboardApp.Kpi("Label", "$0.98", 0.9799m, 0.98m);

        Assert.Equal("-0.01%", kpi.Delta);
        Assert.Equal("down", kpi.Direction);
    }

    [Fact]
    public void Kpi_OmitsDeltaWithoutBaseline()
    {
        var kpi = DashboardApp.Kpi("Label", "80,720", 80720m, 0m);

        Assert.Null(kpi.Delta);
        Assert.Null(kpi.Direction);
    }

    /// <summary>A daily cost row per day in the inclusive range, priced by <paramref name="cost" />.</summary>
    private static List<DashboardDailyCost> DailyCosts(DateOnly from, DateOnly to, Func<DateOnly, decimal> cost) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => from.AddDays(i))
            .Select(day => new DashboardDailyCost(day, cost(day), 0))
            .ToList();

    /// <summary>Period 11 against a window of 7, so the rolling mean cannot come out flat.</summary>
    private static decimal Sawtooth(DateOnly date) => (date.DayNumber % 11) * 10m;

    /// <summary>A different value on every day, so an assertion can pin which day was read.</summary>
    private static decimal Unique(DateOnly date) => date.DayNumber;

    [Fact]
    public void BuildTrend_ProjectsAYearOfDailyPoints()
    {
        var today = new DateTime(2026, 9, 6);
        var dataStart = new DateOnly(2024, 9, 1);
        var activity = new DashboardActivityStats(
            [], 0m, DailyCosts(dataStart, new DateOnly(2026, 9, 6), Sawtooth), null, dataStart);

        var trend = DashboardApp.BuildTrend(activity, today);

        Assert.NotNull(trend);
        Assert.Equal(365, trend.Dates.Count);
        Assert.Equal("2025-09-07", trend.Dates[0]);
        Assert.Equal("2026-09-06", trend.Dates[^1]);
        Assert.All(trend.Dates, date => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", date));
        Assert.Equal(365, trend.Cost.Count);
        Assert.Equal(365, trend.RollingCost.Count);

        // The point of the whole exercise: a curve that moves, rather than the one constant the old
        // reference line drew.
        Assert.True(trend.RollingCost.Distinct().Count() > 1, "the rolling series is flat");

        // Index 0's window is the six days before the displayed range plus the first day of it.
        var leading = new DateOnly(2025, 9, 1);
        var expected = (double)Enumerable.Range(0, 7).Select(i => Sawtooth(leading.AddDays(i))).Average();
        Assert.NotNull(trend.RollingCost[0]);
        Assert.Equal(expected, trend.RollingCost[0]!.Value, 6);
    }

    [Fact]
    public void BuildTrend_ComparesTheSameCalendarDayAYearEarlier()
    {
        var today = new DateTime(2026, 9, 6);
        var dataStart = new DateOnly(2025, 1, 1);
        var activity = new DashboardActivityStats(
            [], 0m, DailyCosts(dataStart, new DateOnly(2026, 9, 6), Unique), null, dataStart);

        var trend = DashboardApp.BuildTrend(activity, today);

        Assert.NotNull(trend);
        // A year before the last displayed day, read by date rather than by bucket offset.
        Assert.Equal((double)Unique(new DateOnly(2025, 9, 6)), trend.PrevCost[^1]);

        // The comparison day for 2026-01-01 is exactly the first recorded day, so it is known.
        var firstOfYear = trend.Dates.IndexOf("2026-01-01");
        Assert.Equal((double)Unique(dataStart), trend.PrevCost[firstOfYear]);

        // One day earlier the comparison falls before any record: unknown, not zero.
        Assert.Null(trend.PrevCost[firstOfYear - 1]);
        Assert.Null(trend.PrevCost[0]);
    }

    [Fact]
    public void BuildTrend_MapsLeapDayOntoTheTwentyEighth()
    {
        // 2027 has no 29th of February. Clamping to the 28th is the calendar behaviour wanted, and the
        // alternative (a gap in the comparison every fourth year) would read as missing data.
        var today = new DateTime(2028, 3, 1);
        var dataStart = new DateOnly(2026, 1, 1);
        var activity = new DashboardActivityStats(
            [], 0m, DailyCosts(dataStart, new DateOnly(2028, 3, 1), Unique), null, dataStart);

        var trend = DashboardApp.BuildTrend(activity, today);

        Assert.NotNull(trend);
        var leapDay = trend.Dates.IndexOf("2028-02-29");
        Assert.True(leapDay >= 0, "the displayed range should contain the leap day");
        Assert.Equal((double)Unique(new DateOnly(2027, 2, 28)), trend.PrevCost[leapDay]);
    }

    [Fact]
    public void BuildTrend_WithoutADailySeries_IsAbsent()
    {
        // No monthly fallback: monthly buckets cannot carry a 7 day average or a date axis, so the card
        // is better absent than plotted from them.
        var months = new List<DashboardMonthStats>
        {
            new(2026, 7, 3, 0, 120m, 0),
            new(2026, 8, 5, 0, 250m, 0)
        };

        Assert.Null(DashboardApp.BuildTrend(new DashboardActivityStats(months, 0), new DateTime(2026, 9, 6)));
    }

    [Fact]
    public void BuildTrend_EmptyDailySeries_IsAllZerosWithNoRollingAverage()
    {
        // A fresh install: the series exists but holds nothing, so every day reads 0 and the curve has
        // nothing to draw rather than a confident flat line at zero.
        var activity = new DashboardActivityStats([], 0m, [], new Dictionary<DateOnly, int>());

        var trend = DashboardApp.BuildTrend(activity, new DateTime(2026, 9, 6));

        Assert.NotNull(trend);
        Assert.All(trend.Cost, cost => Assert.Equal(0d, cost));
        Assert.All(trend.Plans, plans => Assert.Equal(0d, plans));
        Assert.All(trend.RollingCost, rolling => Assert.Null(rolling));
        Assert.All(trend.RollingPlans, rolling => Assert.Null(rolling));
        Assert.All(trend.PrevCost, prev => Assert.Null(prev));
    }

    [Fact]
    public void BuildActivityMonths_BucketsDaysIntoCalendarWeeks()
    {
        // June 2026: the 1st is a Monday, so weeks align exactly with rows.
        var firstMonth = new DateTime(2026, 6, 1);
        var prDays = new List<(DateOnly Date, int Count)>
        {
            (new DateOnly(2026, 6, 1), 2),
            (new DateOnly(2026, 6, 7), 3),
            (new DateOnly(2026, 6, 8), 5),
            (new DateOnly(2026, 6, 30), 1)
        };

        var months = DashboardApp.BuildActivityMonths(prDays, firstMonth);

        Assert.Equal(16, months.Count);
        var june = months[0];
        Assert.Equal("Jun", june.Label);
        Assert.Equal(5, june.Weeks.Count);
        Assert.Equal(5, june.Weeks[0]);
        Assert.Equal(5, june.Weeks[1]);
        Assert.Equal(0, june.Weeks[2]);
        Assert.Equal(1, june.Weeks[4]);
    }

    [Fact]
    public void BuildActivityMonths_OffsetsWeeksByFirstWeekday()
    {
        // August 2026: the 1st is a Saturday, so the first calendar week holds only two days.
        var firstMonth = new DateTime(2026, 8, 1);
        var prDays = new List<(DateOnly Date, int Count)>
        {
            (new DateOnly(2026, 8, 1), 4),
            (new DateOnly(2026, 8, 3), 7)
        };

        var months = DashboardApp.BuildActivityMonths(prDays, firstMonth);

        var august = months[0];
        Assert.Equal(4, august.Weeks[0]);
        Assert.Equal(7, august.Weeks[1]);
    }

    [Fact]
    public void BuildActiveJobs_KeepsOnlyActiveStatuses()
    {
        var jobs = new List<JobItem>
        {
            new() { Id = "job-1", Status = JobStatus.Running, PlanFile = "00001-a" },
            new() { Id = "job-2", Status = JobStatus.Completed, PlanFile = "00002-b" },
            new() { Id = "job-3", Status = JobStatus.Queued, PlanFile = "00003-c" },
            new() { Id = "job-4", Status = JobStatus.Failed, PlanFile = "00004-d" },
            new() { Id = "job-5", Status = JobStatus.Blocked, PlanFile = "00005-e" },
            new() { Id = "job-6", Status = JobStatus.Pending, PlanFile = "00006-f" },
            new() { Id = "job-7", Status = JobStatus.Stopped, PlanFile = "00007-g" }
        };

        var result = DashboardApp.BuildActiveJobs(jobs, new FakePlanReaderService());

        Assert.Equal(["job-1", "job-3", "job-5", "job-6"], result.Select(j => j.Id));
        Assert.Equal(["running", "queued", "blocked", "pending"], result.Select(j => j.Status));
    }

    [Fact]
    public void BuildActiveJobs_MapsPlanIdWithReportedFallback()
    {
        var jobs = new List<JobItem>
        {
            new() { Id = "job-1", Status = JobStatus.Running, PlanFile = "00042-fix-tests" },
            new() { Id = "job-2", Status = JobStatus.Running, PlanFile = "", ReportedPlanId = "00043" },
            new() { Id = "job-3", Status = JobStatus.Running, PlanFile = "" }
        };

        var result = DashboardApp.BuildActiveJobs(jobs, new FakePlanReaderService());

        Assert.Equal(["00042", "00043", ""], result.Select(j => j.PlanId));
    }

    [Fact]
    public void BuildActiveJobs_CapsListLength()
    {
        var jobs = Enumerable.Range(1, 12)
            .Select(i => new JobItem { Id = $"job-{i}", Status = JobStatus.Queued, PlanFile = "" })
            .ToList();

        var result = DashboardApp.BuildActiveJobs(jobs, new FakePlanReaderService());

        Assert.Equal(8, result.Count);
    }

    [Fact]
    public void BuildKpis_UsesThirtyDayPrWindows()
    {
        var today = new DateTime(2026, 8, 31);
        var prDays = new List<(DateOnly Date, int Count)>
        {
            (DateOnly.FromDateTime(today.AddDays(-5)), 30),
            (DateOnly.FromDateTime(today.AddDays(-40)), 15)
        };
        var stats = new DashboardModels(1, 0, 0, 0, 1, 0, 0m, [], []);
        var activity = new DashboardActivityStats([], 0);

        var kpis = DashboardApp.BuildKpis(stats, activity, prDays, today);

        Assert.Equal(4, kpis.Count);
        Assert.Equal("Avg Daily PR count", kpis[0].Label);
        Assert.Equal("1", kpis[0].Value);
        Assert.Equal("+100%", kpis[0].Delta);
        Assert.Equal("Avg Cost/Month", kpis[1].Label);
        // The projection sits next to the retrospective average it is read against.
        Assert.Equal("Forecast This Month", kpis[2].Label);
        Assert.Equal("Avg Cost/Plan", kpis[3].Label);
    }

    [Fact]
    public void BuildKpis_ForecastComputesCalendarProjection()
    {
        // August 2026 has 31 days. Ten days of history, three of which cost anything: the calendar
        // basis divides by 10.
        var today = new DateTime(2026, 8, 31);
        var dailyCosts = new List<DashboardDailyCost>
        {
            new(DateOnly.FromDateTime(today.AddDays(-9)), 10m, 1000),
            new(DateOnly.FromDateTime(today.AddDays(-5)), 20m, 2000),
            new(DateOnly.FromDateTime(today), 30m, 3000)
        };
        var stats = new DashboardModels(1, 0, 0, 0, 1, 0, 0m, [], []);
        var activity = new DashboardActivityStats([], 0, dailyCosts);

        var forecast = DashboardApp.BuildKpis(stats, activity, [], today)
            .Single(k => k.Label == "Forecast This Month");

        // 60 over 10 days times 31, rounded by FormatCost above 100.
        Assert.Equal("$186", forecast.Value);
        Assert.Null(forecast.Hint);
    }

    [Fact]
    public void BuildKpis_ForecastWithoutDailyCosts_RendersNoDataState()
    {
        // Null DailyCosts is what every fake supplies, and what GetActivityStats returned before this
        // series existed. It has to read as "nothing to project from", not as a confident $0.00.
        var stats = new DashboardModels(1, 0, 0, 0, 1, 0, 0m, [], []);
        var activity = new DashboardActivityStats([], 0);

        var forecast = DashboardApp.BuildKpis(stats, activity, [], new DateTime(2026, 8, 31))
            .Single(k => k.Label == "Forecast This Month");

        Assert.Equal("-", forecast.Value);
        Assert.Equal("No cost data in the last 30 days", forecast.Hint);
        Assert.Null(forecast.Delta);
    }

    [Fact]
    public void BuildWeeklyTrend_Projects28DaysWithComparison()
    {
        var today = new DateTime(2026, 9, 6);
        var dailyCosts = new List<DashboardDailyCost>
        {
            new(new DateOnly(2026, 9, 6), 28.31m, 2500),
            new(new DateOnly(2026, 9, 5), 18.69m, 1700),
            new(new DateOnly(2026, 9, 3), 7.53m, 700),
            // 28 days before Sept 6 is Aug 9
            new(new DateOnly(2026, 8, 9), 12.50m, 1200)
        };
        var dailyPlans = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2026, 9, 6)] = 5,
            [new DateOnly(2026, 8, 9)] = 2
        };

        var activity = new DashboardActivityStats([], 0m, dailyCosts, dailyPlans);
        var trend = DashboardApp.BuildWeeklyTrend(activity, today);

        Assert.NotNull(trend);
        Assert.Equal(28, trend.Dates.Count);
        Assert.Equal(28, trend.Cost.Count);
        Assert.Equal(28, trend.Plans.Count);
        Assert.Equal(28, trend.PrevCost.Count);
        Assert.Equal(28, trend.PrevPlans.Count);
        Assert.Equal(28, trend.RollingCost.Count);

        // 27 days before today through today, as dates rather than labels.
        Assert.Equal("2026-08-10", trend.Dates[0]);
        Assert.Equal("2026-09-06", trend.Dates[^1]);

        Assert.Equal(28.31, trend.Cost[^1]);
        Assert.Equal(5.0, trend.Plans[^1]);
        // Zero-filled: Sept 4 has no rows at all and is present as 0, not missing.
        Assert.Equal(0d, trend.Cost[trend.Dates.IndexOf("2026-09-04")]);

        // Sept 6 compared to 28 days earlier (Aug 9)
        Assert.Equal(12.50, trend.PrevCost[^1]);
        Assert.Equal(2.0, trend.PrevPlans[^1]);

        // Records begin Aug 9 (the fallback for a mock with no DailyDataStart), so the window clears it
        // on Aug 15 and every day after that has a mean.
        Assert.Null(trend.RollingCost[trend.Dates.IndexOf("2026-08-14")]);
        Assert.NotNull(trend.RollingCost[trend.Dates.IndexOf("2026-08-15")]);
        Assert.True(trend.RollingCost.Distinct().Count() > 1, "the rolling series is flat");
    }

    [Fact]
    public void BuildWeeklyTrend_WithoutADailySeries_IsAbsent()
    {
        // Weekly buckets are gone from the model entirely: with no daily series there is nothing to
        // fall back to, so the card is absent rather than plotted from coarser data.
        Assert.Null(DashboardApp.BuildWeeklyTrend(
            new DashboardActivityStats([], 0m), new DateTime(2026, 9, 6)));
    }

    [Fact]
    public void BuildActivityMonths_IncludesDailyBreakdown()
    {
        var firstMonth = new DateTime(2026, 9, 1);
        var prDays = new List<(DateOnly Date, int Count)>
        {
            (new DateOnly(2026, 9, 6), 3)
        };

        var months = DashboardApp.BuildActivityMonths(prDays, firstMonth);
        var sep = months[0];

        Assert.NotNull(sep.Days);
        Assert.Equal(30, sep.Days!.Count);
        var day6 = sep.Days.First(d => d.Date == "2026-09-06");
        Assert.Equal(3, day6.Count);
    }
}
