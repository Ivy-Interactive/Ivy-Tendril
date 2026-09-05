using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class DashboardActivityStatsTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PlanDatabaseService _db;

    public DashboardActivityStatsTests()
    {
        var dbPath = Path.Combine(_tempDir.Path, $"tendril-test-{Guid.NewGuid()}.db");
        _db = new PlanDatabaseService(dbPath, NullLogger<PlanDatabaseService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    private static PlanFile CreateTestPlan(int id, PlanStatus status, DateTime created, DateTime updated,
        int prCount = 0)
    {
        var prs = Enumerable.Range(1, prCount)
            .Select(i => $"https://github.com/test/repo/pull/{id * 10 + i}")
            .ToList();

        var metadata = new PlanMetadata(
            id, "Tendril", "NiceToHave", $"Plan {id}", status,
            new List<string> { "D:\\Repos\\Test" },
            new List<string>(),
            prs,
            new List<PlanVerificationEntry>(),
            new List<string>(),
            new List<string>(),
            created,
            updated,
            null,
            null
        );

        return new PlanFile(metadata, "# Content", $"D:\\Plans\\{id:D5}-Plan", "state: Draft");
    }

    [Fact]
    public void GetActivityStats_AggregatesByCalendarMonth()
    {
        var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var twoMonthsAgo = thisMonth.AddMonths(-2).AddDays(3);

        var oldPlan = CreateTestPlan(1500, PlanStatus.Completed, twoMonthsAgo, twoMonthsAgo, prCount: 1);
        var newPlan = CreateTestPlan(1501, PlanStatus.Completed, DateTime.UtcNow, DateTime.UtcNow, prCount: 2);
        var draftPlan = CreateTestPlan(1502, PlanStatus.Draft, DateTime.UtcNow, DateTime.UtcNow);
        _db.UpsertPlan(oldPlan);
        _db.UpsertPlan(newPlan);
        _db.UpsertPlan(draftPlan);
        _db.UpsertCosts(1500, [new CostEntry("CreatePlan", 1000, 5m, twoMonthsAgo)]);
        _db.UpsertCosts(1501, [new CostEntry("CreatePlan", 500, 3.5m, DateTime.UtcNow)]);

        var stats = _db.GetActivityStats(monthsBack: 24);

        Assert.Equal(24, stats.Months.Count);

        var current = stats.Months[^1];
        Assert.Equal(thisMonth.Year, current.Year);
        Assert.Equal(thisMonth.Month, current.Month);
        Assert.Equal(2, current.PlansCreated);
        Assert.Equal(2, current.PrsMerged);
        Assert.Equal(3.5m, current.Cost);
        Assert.Equal(500, current.Tokens);

        var older = stats.Months.Single(m => m.Year == twoMonthsAgo.Year && m.Month == twoMonthsAgo.Month);
        Assert.Equal(1, older.PlansCreated);
        Assert.Equal(1, older.PrsMerged);
        Assert.Equal(5m, older.Cost);
        Assert.Equal(1000, older.Tokens);

        var empty = stats.Months[0];
        Assert.Equal(0, empty.PlansCreated);
        Assert.Equal(0, empty.PrsMerged);
        Assert.Equal(0m, empty.Cost);
    }

    [Fact]
    public void GetActivityStats_ExcludesIncompletePlansFromPrCounts()
    {
        var reviewPlan = CreateTestPlan(1500, PlanStatus.Review, DateTime.UtcNow, DateTime.UtcNow, prCount: 3);
        _db.UpsertPlan(reviewPlan);

        var stats = _db.GetActivityStats(monthsBack: 3);

        Assert.Equal(3, stats.Months.Count);
        Assert.All(stats.Months, m => Assert.Equal(0, m.PrsMerged));
        Assert.Equal(1, stats.Months[^1].PlansCreated);
    }

    [Fact]
    public void GetActivityStats_ComputesPrevWeekAvgCostPerPlan()
    {
        var inWindow = DateTime.UtcNow.Date.AddDays(-10);
        var beforeWindow = DateTime.UtcNow.Date.AddDays(-20);

        var plan = CreateTestPlan(1500, PlanStatus.Review, inWindow, inWindow);
        var otherPlan = CreateTestPlan(1501, PlanStatus.Completed, beforeWindow, beforeWindow);
        _db.UpsertPlan(plan);
        _db.UpsertPlan(otherPlan);
        _db.UpsertCosts(1500, [new CostEntry("CreatePlan", 100, 4m, inWindow)]);
        _db.UpsertCosts(1501, [new CostEntry("CreatePlan", 100, 99m, beforeWindow)]);

        var stats = _db.GetActivityStats();

        Assert.Equal(4m, stats.PrevWeekAvgCostPerPlan);
    }

    [Fact]
    public void GetActivityStats_PrevWeekAvg_IgnoresPlansThatCouldNotBePriced()
    {
        // An unpriceable plan in the divisor would halve the average and report a figure nobody spent.
        var inWindow = DateTime.UtcNow.Date.AddDays(-10);

        _db.UpsertPlan(CreateTestPlan(1500, PlanStatus.Review, inWindow, inWindow));
        _db.UpsertPlan(CreateTestPlan(1501, PlanStatus.Review, inWindow, inWindow));
        _db.UpsertCosts(1500, [new CostEntry("CreatePlan", 100, 4m, inWindow)]);
        _db.UpsertCosts(1501, [new CostEntry("CreatePlan", 100, null, inWindow)]);

        Assert.Equal(4m, _db.GetActivityStats().PrevWeekAvgCostPerPlan);
    }

    [Fact]
    public void GetActivityStats_MonthOfOnlyUnknownCosts_ReportsZeroWithoutThrowing()
    {
        // SUM over a group of NULLs is NULL, and the reader's Convert.ToDecimal would throw on it.
        var now = DateTime.UtcNow;
        _db.UpsertPlan(CreateTestPlan(1500, PlanStatus.Completed, now, now));
        _db.UpsertCosts(1500, [new CostEntry("ExecutePlan", 150_000, null, now)]);

        var stats = _db.GetActivityStats(monthsBack: 3);

        Assert.Equal(0m, stats.Months[^1].Cost);
        // The tokens survive even though the cost does not: they are what the backfill prices from.
        Assert.Equal(150_000, stats.Months[^1].Tokens);
    }

    [Fact]
    public void GetHourlyTokenBurn_WindowOfOnlyUnknownCosts_DoesNotThrow()
    {
        var now = DateTime.UtcNow;
        _db.UpsertPlan(CreateTestPlan(1500, PlanStatus.Completed, now, now));
        _db.UpsertCosts(1500, [new CostEntry("ExecutePlan", 150_000, null, now)]);

        var burn = _db.GetHourlyTokenBurn();

        Assert.Equal(0m, Assert.Single(burn).Cost);
    }

    [Fact]
    public void GetActivityStats_DailyCosts_BucketByTheCostRowTimestampNotThePlanUpdate()
    {
        // A plan touched today whose spend happened five days ago belongs on the day it was spent,
        // otherwise a long-running plan dumps weeks of cost onto one day and the forecast reads wrong.
        var now = DateTime.UtcNow;
        var fiveDaysAgo = now.Date.AddDays(-5);
        _db.UpsertPlan(CreateTestPlan(1500, PlanStatus.Completed, now.AddDays(-6), now));
        _db.UpsertCosts(1500, [new CostEntry("ExecutePlan", 1000, 7m, fiveDaysAgo)]);

        var dailyCosts = _db.GetActivityStats().DailyCosts;

        Assert.NotNull(dailyCosts);
        var day = Assert.Single(dailyCosts);
        Assert.Equal(DateOnly.FromDateTime(fiveDaysAgo), day.Date);
        Assert.Equal(7m, day.Cost);
        Assert.Equal(1000, day.Tokens);
    }

    [Fact]
    public void GetActivityStats_DailyCosts_NullTimestampFallsBackToThePlanUpdate()
    {
        var updated = DateTime.UtcNow.Date.AddDays(-3);
        _db.UpsertPlan(CreateTestPlan(1500, PlanStatus.Completed, updated, updated));
        _db.UpsertCosts(1500, [new CostEntry("ExecutePlan", 1000, 7m, null)]);

        var dailyCosts = _db.GetActivityStats().DailyCosts;

        Assert.NotNull(dailyCosts);
        Assert.Equal(DateOnly.FromDateTime(updated), Assert.Single(dailyCosts).Date);
    }

    [Fact]
    public void GetActivityStats_DailyCosts_IncludeAnExecutingPlan()
    {
        // The in-flight case the state-filtered monthly query drops. Money an Executing plan has spent
        // is already spent, and leaving it out is a large part of why the monthly figures read low.
        var now = DateTime.UtcNow;
        _db.UpsertPlan(CreateTestPlan(1500, PlanStatus.Executing, now, now));
        _db.UpsertCosts(1500, [new CostEntry("ExecutePlan", 1000, 12m, now)]);

        var stats = _db.GetActivityStats();

        Assert.NotNull(stats.DailyCosts);
        Assert.Equal(12m, Assert.Single(stats.DailyCosts).Cost);
        // Contrast: the monthly series still excludes it, which is the behaviour being worked around.
        Assert.Equal(0m, stats.Months[^1].Cost);
    }
}
