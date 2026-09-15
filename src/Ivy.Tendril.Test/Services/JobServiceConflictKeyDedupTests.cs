using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     Admission-time deduplication (#2710). <c>TryRejectConflictingJob</c> used to be an allow-list of
///     five job-type names, so CreatePlan, CreatePr, CreateIssue and SetupProject shipped
///     undeduplicated: 99 CreatePlan jobs from 39 distinct descriptions in one hour, and five CreatePr
///     jobs on one plan in 82 seconds. <see cref="JobArgsConflictKeyContractTests" /> pins the key each
///     args type produces; this pins what <see cref="JobService" /> does with it.
///     <para>
///     Every service here is built with <c>maxConcurrentJobs: 0</c>, so an admitted submission parks in
///     <see cref="JobStatus.Queued" /> instead of trying to launch an agent. Queued is one of the four
///     live statuses the guard treats as a conflict, which is exactly the state a duplicate has to be
///     rejected against.
///     </para>
/// </summary>
[Collection("TendrilHome")]
public class JobServiceConflictKeyDedupTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-dedup");
    private readonly List<PlanDatabaseService> _databases = [];

    /// <summary>A pid no process on this machine can plausibly have, so liveness reads false.</summary>
    private const int DeadPid = 0x7FFFFFF0;

    private const string Scope = "/plans/00601-SamplePlan";

    public void Dispose()
    {
        foreach (var db in _databases)
            db.Dispose();
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    private JobService CreateService(IPlanDatabaseService? database = null)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            inboxPath: _tempDir.Path, maxConcurrentJobs: 0, database: database);
    }

    private PlanDatabaseService CreateDatabase()
    {
        var db = new PlanDatabaseService(
            Path.Combine(_tempDir.Path, $"tendril-dedup-{Guid.NewGuid()}.db"),
            NullLogger<PlanDatabaseService>.Instance);
        _databases.Add(db);
        return db;
    }

    /// <summary>
    ///     The four types the old allow-list was missing, so the same case runs for more than one of
    ///     them. CreatePlan keys on the task, the other three on the plan folder.
    /// </summary>
    public static TheoryData<string> DeduplicatedKinds() => ["CreatePlan", "CreatePr", "CreateIssue", "SetupProject"];

    /// <summary>The three of those four that have a <c>--force</c> affordance.</summary>
    public static TheoryData<string> ForceableKinds() => ["CreatePlan", "CreatePr", "CreateIssue"];

    private static JobArgsBase Args(string kind, string scope, bool force = false) => kind switch
    {
        "CreatePlan" => new CreatePlanArgs(scope, "Tendril", Force: force),
        "CreatePr" => new CreatePrArgs(scope, Force: force),
        "CreateIssue" => new CreateIssueArgs(scope, "Ivy-Interactive/ivy-tendril", Force: force),
        "SetupProject" => new SetupProjectArgs(scope),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No sample args for this job kind")
    };

    /// <summary>
    ///     Submits the same request twice with the first one parked in <paramref name="existingStatus" />,
    ///     and asserts the second is refused by name.
    /// </summary>
    private void AssertSecondSubmissionRejected(string kind, JobStatus existingStatus)
    {
        var service = CreateService();

        var firstId = service.StartJob(Args(kind, Scope));
        service.GetJob(firstId)!.Status = existingStatus;

        var secondId = service.StartJob(Args(kind, Scope));

        var second = service.GetJob(secondId);
        Assert.NotNull(second);
        Assert.Equal(JobStatus.Failed, second.Status);
        // Naming the job it collided with is the whole point: a rejection nobody can trace is
        // indistinguishable from a job that vanished.
        Assert.Contains(firstId, second.StatusMessage);
        Assert.Contains("--force", second.StatusMessage);
    }

    [Theory]
    [MemberData(nameof(DeduplicatedKinds))]
    public void StartJob_RejectsIdenticalSubmission_WhenExistingIsPending(string kind) =>
        AssertSecondSubmissionRejected(kind, JobStatus.Pending);

    [Theory]
    [MemberData(nameof(DeduplicatedKinds))]
    public void StartJob_RejectsIdenticalSubmission_WhenExistingIsQueued(string kind) =>
        AssertSecondSubmissionRejected(kind, JobStatus.Queued);

    [Theory]
    [MemberData(nameof(DeduplicatedKinds))]
    public void StartJob_RejectsIdenticalSubmission_WhenExistingIsRunning(string kind) =>
        AssertSecondSubmissionRejected(kind, JobStatus.Running);

    [Fact]
    public void StartJob_RejectsIdenticalSubmission_WhenExistingIsBlocked()
    {
        // Blocked counts as live too: a plan waiting on a dependency will run, so a second submission
        // of the same work would double up the moment it unblocks.
        AssertSecondSubmissionRejected("CreatePr", JobStatus.Blocked);
    }

    [Fact]
    public void StartJob_AcceptsCreatePlan_WithDifferentDescription()
    {
        var service = CreateService();

        service.StartJob(new CreatePlanArgs("Add a widget", "Tendril"));
        var secondId = service.StartJob(new CreatePlanArgs("Remove a widget", "Tendril"));

        Assert.Equal(JobStatus.Queued, service.GetJob(secondId)!.Status);
    }

    [Fact]
    public void StartJob_AcceptsCreatePlan_WithDifferentProject()
    {
        var service = CreateService();

        service.StartJob(new CreatePlanArgs("Add a widget", "Tendril"));
        var secondId = service.StartJob(new CreatePlanArgs("Add a widget", "Ivy"));

        // The task key is per project, so the same sentence against two projects is two requests.
        Assert.Equal(JobStatus.Queued, service.GetJob(secondId)!.Status);
    }

    [Fact]
    public void StartJob_RejectsCreatePlan_WhenDescriptionDiffersOnlyByWhitespaceOrCase()
    {
        var service = CreateService();

        var firstId = service.StartJob(new CreatePlanArgs("Add a widget", "Tendril"));

        // A human re-typing the same request, or a client retrying it after a submission timeout, does
        // not reproduce the whitespace exactly. The task hash normalises both.
        var secondId = service.StartJob(new CreatePlanArgs("  ADD   A\tWIDGET  ", "  tendril "));

        var second = service.GetJob(secondId);
        Assert.NotNull(second);
        Assert.Equal(JobStatus.Failed, second.Status);
        Assert.Contains(firstId, second.StatusMessage);
    }

    [Theory]
    [MemberData(nameof(ForceableKinds))]
    public void StartJob_AcceptsDuplicateSubmission_WhenForceIsSet(string kind)
    {
        var service = CreateService();

        service.StartJob(Args(kind, Scope));
        var secondId = service.StartJob(Args(kind, Scope, force: true));

        // CreatePlan's own firmware spawns siblings with --force and the Create Plan dialog always
        // passes it, so a guard that ignored --force would break both paths.
        Assert.Equal(JobStatus.Queued, service.GetJob(secondId)!.Status);
    }

    [Fact]
    public void StartJob_TwoConcurrentIdenticalSubmissions_ProduceExactlyOneJob()
    {
        var service = CreateService();
        var args = new CreatePlanArgs("Fix the job duplication storm", "Tendril");

        // The check used to be a bare check-then-insert with nothing serialising it, so two submissions
        // arriving together both found nothing and both proceeded.
        using var barrier = new Barrier(2);
        var ids = new string[2];
        var threads = new Thread[2];
        for (var i = 0; i < threads.Length; i++)
        {
            var slot = i;
            threads[slot] = new Thread(() =>
            {
                barrier.SignalAndWait();
                ids[slot] = service.StartJob(args);
            });
            threads[slot].Start();
        }

        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Submission thread did not finish");

        var admitted = ids.Select(id => service.GetJob(id)!).Where(j => j.Status != JobStatus.Failed).ToList();
        Assert.Single(admitted);
        Assert.Equal(2, service.GetJobs().Count);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Stopped)]
    public void StartJob_AcceptsDuplicateSubmission_WhenPriorReachedTerminalState(JobStatus terminalStatus)
    {
        var service = CreateService();

        var firstId = service.StartJob(new CreatePlanArgs("Add a widget", "Tendril"));
        service.GetJob(firstId)!.Status = terminalStatus;

        // Dedup is about work in progress, not history: re-running a finished task is a legitimate
        // request and must not need --force.
        var secondId = service.StartJob(new CreatePlanArgs("Add a widget", "Tendril"));
        Assert.Equal(JobStatus.Queued, service.GetJob(secondId)!.Status);
    }

    [Fact]
    public void StartJob_RejectsSubmission_WhenDuplicateIsOnlyVisibleInTheDatabase()
    {
        var db = CreateDatabase();
        var service = CreateService(db);
        var args = new CreatePlanArgs("Add a widget", "Tendril");

        // Written after the service was constructed, so LoadHistoricalJobs never saw it: this is a
        // submission belonging to another instance over the same TENDRIL_HOME, which is the
        // configuration the storm ran in. StartedAt null means it never launched, so it is still live.
        db.UpsertJob(new JobItem
        {
            Id = "09999",
            Type = Constants.JobTypes.CreatePlan,
            Project = "Tendril",
            Status = JobStatus.Pending,
            TypedArgs = args
        });

        var id = service.StartJob(args);

        var job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("09999", job.StatusMessage);
    }

    [Fact]
    public void StartJob_AcceptsSubmission_WhenLiveRowIsFromDeadProcessAndPastTimeout()
    {
        var db = CreateDatabase();
        var service = CreateService(db);
        var args = new CreatePlanArgs("Add a widget", "Tendril");

        // A row left Running by an instance that was kill -9'd. Taken at face value it would lock this
        // task out forever, which is a worse failure than a duplicate.
        db.UpsertJob(new JobItem
        {
            Id = "09999",
            Type = Constants.JobTypes.CreatePlan,
            Project = "Tendril",
            Status = JobStatus.Running,
            ProcessId = DeadPid,
            StartedAt = DateTime.UtcNow.AddHours(-3),
            TypedArgs = args
        });

        var id = service.StartJob(args);
        Assert.Equal(JobStatus.Queued, service.GetJob(id)!.Status);
    }

    [Fact]
    public void StartJob_RejectsSubmission_WhenDatabaseRowIsWithinTheJobTimeout()
    {
        var db = CreateDatabase();
        var service = CreateService(db);
        var args = new CreatePlanArgs("Add a widget", "Tendril");

        // The other half of the guard above: a dead pid alone is not proof, because the pid is only
        // recorded once the agent launches. Inside the job timeout the row is still presumed live.
        db.UpsertJob(new JobItem
        {
            Id = "09998",
            Type = Constants.JobTypes.CreatePlan,
            Project = "Tendril",
            Status = JobStatus.Running,
            ProcessId = DeadPid,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            TypedArgs = args
        });

        var id = service.StartJob(args);
        var job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("09998", job.StatusMessage);
    }

    [Fact]
    public void StartJob_DoesNotDeduplicate_SyncRepoOrAddProject()
    {
        var service = CreateService();

        // SyncRepo opts out because it has something better: a duplicate submission is merged into the
        // job already in flight, so both callers get the same id rather than one getting a rejection.
        var firstSync = service.StartJob(new SyncRepoArgs(_tempDir.Path, "development"));
        var secondSync = service.StartJob(new SyncRepoArgs(_tempDir.Path, "development"));
        Assert.Equal(firstSync, secondSync);
        Assert.Equal(JobStatus.Queued, service.GetJob(firstSync)!.Status);

        // AddProject opts out outright, so two identical submissions both proceed.
        var repos = new List<RepoRef> { new() { Path = _tempDir.Path } };
        var firstAdd = service.StartJob(new AddProjectArgs("Tendril", repos));
        var secondAdd = service.StartJob(new AddProjectArgs("Tendril", repos));
        Assert.NotEqual(firstAdd, secondAdd);
        Assert.Equal(JobStatus.Queued, service.GetJob(firstAdd)!.Status);
        Assert.Equal(JobStatus.Queued, service.GetJob(secondAdd)!.Status);
    }

    [Fact]
    public void StartJob_RejectedDuplicate_IsKeptAsAFailedRow()
    {
        var service = CreateService();

        service.StartJob(new CreatePlanArgs("Add a widget", "Tendril"));
        var secondId = service.StartJob(new CreatePlanArgs("Add a widget", "Tendril"));

        // Deliberately not silent: the rejection count is a useful signal, and a submission that
        // disappears without trace is how the storm went unnoticed for an hour.
        var jobs = service.GetJobs();
        Assert.Equal(2, jobs.Count);
        Assert.Contains(jobs, j => j.Id == secondId && j.Status == JobStatus.Failed && j.CompletedAt != null);
    }
}
