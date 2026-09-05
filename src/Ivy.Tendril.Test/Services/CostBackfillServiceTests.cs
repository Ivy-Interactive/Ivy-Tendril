using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Telemetry;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

/// <summary>
/// The backfill repairs jobs whose cost was recorded as nothing before the estimated tier existed.
/// What it must never do matters as much as what it fills: a figure the agent actually charged, and a
/// model nobody has rates for, both have to come out untouched.
/// </summary>
public class CostBackfillServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PlanDatabaseService _db;

    public CostBackfillServiceTests()
    {
        _db = new PlanDatabaseService(
            Path.Combine(_tempDir.Path, $"tendril-test-{Guid.NewGuid()}.db"),
            NullLogger<PlanDatabaseService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    private static readonly ModelPricing ClaudePricing = new()
    {
        Model = "claude-opus-5",
        InputPerMillion = 3m,
        OutputPerMillion = 15m,
        CacheReadPerMillion = 0.3m,
        CacheWritePerMillion = 3.75m,
        Source = "Static catalog (claude)",
    };

    /// <summary>1000 input at $3, 500 output at $15, 90,000 cache read at $0.30, 300 cache write at $3.75.</summary>
    private const decimal ExpectedEstimate =
        1000 * 3m / 1_000_000m + 500 * 15m / 1_000_000m
        + 90_000 * 0.3m / 1_000_000m + 300 * 3.75m / 1_000_000m;

    private CostBackfillService Service() => new(
        _db, new ModelPricingProvider([ClaudePricing]), NullLogger<CostBackfillService>.Instance);

    private static JobItem Job(
        string id = "j1",
        decimal? cost = null,
        string? costSource = null,
        string? model = "claude-opus-5",
        bool withTokens = true,
        JobArgsBase? args = null) => new()
        {
            Id = id,
            Type = "ExecutePlan",
            PlanFile = "01500-Plan",
            Project = "Tendril",
            Provider = "claude",
            Status = JobStatus.Completed,
            CompletedAt = DateTime.UtcNow,
            Model = model,
            Cost = cost,
            CostSource = costSource,
            Tokens = withTokens ? 1500 : 0,
            InputTokens = withTokens ? 1000 : null,
            OutputTokens = withTokens ? 500 : null,
            CacheReadTokens = withTokens ? 90_000 : null,
            CacheWriteTokens = withTokens ? 300 : null,
            ReasoningTokens = withTokens ? 42 : null,
            TypedArgs = args,
        };

    private JobItem Reload(string id) => _db.GetJobById(id)!;

    [Fact]
    public void Run_JobWithTokensAndKnownModel_FillsAnEstimate()
    {
        _db.UpsertJob(Job());

        Service().Run();

        var job = Reload("j1");
        Assert.Equal(ExpectedEstimate, job.Cost);
        Assert.Equal(JobCostSources.Estimated, job.CostSource);
    }

    [Fact]
    public void Run_StoredZeroFromTheOldWriter_IsFilledToo()
    {
        // The pre-migration writer coerced an unknown cost to 0.0, which is why the candidate query
        // cannot just look for NULL.
        _db.UpsertJob(Job(cost: 0m, costSource: JobCostSources.Computed));

        Service().Run();

        Assert.Equal(ExpectedEstimate, Reload("j1").Cost);
    }

    [Fact]
    public void Run_AgentReportedCost_IsLeftCompletelyAlone()
    {
        // Never overwrite a real charge. A $0 an agent reported is the agent's claim, not an absence.
        _db.UpsertJob(Job(cost: 0m, costSource: JobCostSources.Agent));

        Service().Run();

        var job = Reload("j1");
        Assert.Equal(0m, job.Cost);
        Assert.Equal(JobCostSources.Agent, job.CostSource);
    }

    [Fact]
    public void Run_UnknownModel_LeavesCostNullRatherThanWritingZero()
    {
        _db.UpsertJob(Job(model: "some-model-nobody-has-rates-for"));

        Service().Run();

        var job = Reload("j1");
        Assert.Null(job.Cost);
        Assert.Null(job.CostSource);
    }

    [Fact]
    public void Run_ModelButNoTokens_IsSkipped()
    {
        _db.UpsertJob(Job(withTokens: false));

        Service().Run();

        var job = Reload("j1");
        Assert.Null(job.Cost);
        Assert.Null(job.CostSource);
    }

    [Fact]
    public void Run_SecondPass_ChangesNothing()
    {
        _db.UpsertJob(Job());
        _db.UpsertJob(Job("j2", model: "unpriced-model"));
        _db.UpsertJob(Job("j3", cost: 9.99m, costSource: JobCostSources.Agent));

        var service = Service();
        service.Run();
        var afterFirst = Snapshot();

        service.Run();

        Assert.Equal(afterFirst, Snapshot());
    }

    private List<(string Id, decimal? Cost, string? Source)> Snapshot() =>
        _db.GetRecentJobs(500)
            .OrderBy(j => j.Id, StringComparer.Ordinal)
            .Select(j => (j.Id, j.Cost, j.CostSource))
            .ToList();

    [Fact]
    public void Run_RewritesTheMatchingCostsCsvRowAndLeavesPricedRowsAlone()
    {
        // Not optional: SyncPlanCosts re-reads this file and UpsertCosts replaces every Costs row for
        // the plan, so a database-only repair is undone by the next sync.
        var folder = Path.Combine(_tempDir.Path, "01500-Plan");
        Directory.CreateDirectory(folder);
        var csvPath = Path.Combine(folder, "costs.csv");
        File.WriteAllText(csvPath,
            "Promptware,Tokens,Cost,Model\nCreatePlan,25000,0.0750,claude-opus-5\nExecutePlan,150000,,\n");

        _db.UpsertJob(Job(args: new ExecutePlanArgs(folder)));

        Service().Run();

        var lines = File.ReadAllLines(csvPath);
        Assert.Equal("CreatePlan,25000,0.0750,claude-opus-5", lines[1]);
        Assert.Equal($"ExecutePlan,150000,{ExpectedEstimate:F4},claude-opus-5", lines[2]);
    }

    [Fact]
    public void Run_AmbiguousPromptwareInTheCsv_LeavesEveryRowAlone()
    {
        // Two unpriced ExecutePlan rows: the file cannot say which run this job was, and guessing would
        // move money onto the wrong row.
        var folder = Path.Combine(_tempDir.Path, "01500-Plan");
        Directory.CreateDirectory(folder);
        var csvPath = Path.Combine(folder, "costs.csv");
        var original = "Promptware,Tokens,Cost,Model\nExecutePlan,150000,,\nExecutePlan,90000,,\n";
        File.WriteAllText(csvPath, original);

        _db.UpsertJob(Job(args: new ExecutePlanArgs(folder)));

        Service().Run();

        Assert.Equal(original, File.ReadAllText(csvPath));
        // The database row is still repaired: the CSV being ambiguous says nothing about the job.
        Assert.Equal(ExpectedEstimate, Reload("j1").Cost);
    }

    [Fact]
    public void Run_NotMaster_TouchesNothing()
    {
        // Through the seam, not by setting TENDRIL_NOT_MASTER: the environment is process wide, and
        // JobService.ReconcileRestoredJob reads the same variable, so flipping it here changed what
        // JobServiceStartupTests saw on another thread.
        _db.UpsertJob(Job());

        var service = new CostBackfillService(
            _db, new ModelPricingProvider([ClaudePricing]), NullLogger<CostBackfillService>.Instance)
        {
            IsNotMaster = () => true,
        };
        service.Run();

        Assert.Null(Reload("j1").Cost);
    }
}
