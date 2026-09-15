using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     The database-level half of the cross-type dedup guard: <see cref="PlanDatabaseService" />'s
///     <see cref="PlanDatabaseService.FindLiveJobsByConflictKey" /> now takes a set of job type names
///     (one <see cref="JobExclusionGroup" />'s members) rather than a single type, so a live row of a
///     <em>different</em> type in the same group is still found (#2710 follow-up: plan 00606 admitted
///     CreatePr and RetryPlan on the same worktree because the old lookup only matched same-type rows).
/// </summary>
public class JobConflictLookupTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-job-conflicts");
    private readonly PlanDatabaseService _db;
    private const string PlanFolder = "/plans/00606-SamplePlan";

    public JobConflictLookupTests()
    {
        var dbPath = Path.Combine(_tempDir.Path, $"tendril-conflicts-{Guid.NewGuid()}.db");
        _db = new PlanDatabaseService(dbPath, NullLogger<PlanDatabaseService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    private static JobItem MakeJob(string id, JobArgsBase args, JobStatus status, DateTime? startedAt = null) =>
        new()
        {
            Id = id,
            Type = args.Type,
            PlanFile = PlanFolder,
            Project = "Test",
            Status = status,
            TypedArgs = args,
            StartedAt = startedAt ?? DateTime.UtcNow
        };

    [Fact]
    public void FindLiveJobsByConflictKey_ReturnsALiveJobOfADifferentConflictingType()
    {
        _db.UpsertJob(MakeJob("job-1", new RetryPlanArgs(PlanFolder, "retry"), JobStatus.Running));

        var candidates = _db.FindLiveJobsByConflictKey(
            JobExclusionGroups.TypesIn(JobExclusionGroup.PlanWorktree), PlanFolder, "job-2");

        var candidate = Assert.Single(candidates);
        Assert.Equal("job-1", candidate.Id);
        Assert.Equal("RetryPlan", candidate.Type);
    }

    [Fact]
    public void FindLiveJobsByConflictKey_IgnoresATypeOutsideTheGroup()
    {
        _db.UpsertJob(MakeJob("job-1", new CreateIssueArgs(PlanFolder, "Ivy-Interactive/ivy-tendril"), JobStatus.Running));

        var candidates = _db.FindLiveJobsByConflictKey(
            JobExclusionGroups.TypesIn(JobExclusionGroup.PlanWorktree), PlanFolder, "job-2");

        Assert.Empty(candidates);
    }

    [Fact]
    public void FindLiveJobsByConflictKey_IgnoresTerminalStatusesAndTheExcludedJob()
    {
        _db.UpsertJob(MakeJob("job-completed", new ExecutePlanArgs(PlanFolder), JobStatus.Completed));
        _db.UpsertJob(MakeJob("job-failed", new ExecutePlanArgs(PlanFolder), JobStatus.Failed));
        _db.UpsertJob(MakeJob("job-self", new ExecutePlanArgs(PlanFolder), JobStatus.Running));

        var candidates = _db.FindLiveJobsByConflictKey(
            JobExclusionGroups.TypesIn(JobExclusionGroup.PlanWorktree), PlanFolder, "job-self");

        Assert.Empty(candidates);
    }

    [Fact]
    public void FindLiveJobsByConflictKey_ReturnsNothing_ForAnEmptyTypeSet()
    {
        _db.UpsertJob(MakeJob("job-1", new ExecutePlanArgs(PlanFolder), JobStatus.Running));

        var candidates = _db.FindLiveJobsByConflictKey(
            JobExclusionGroups.TypesIn(JobExclusionGroup.None), PlanFolder, "job-2");

        Assert.Empty(candidates);
    }
}
