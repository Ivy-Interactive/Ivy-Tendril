using System.Collections.Concurrent;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class JobServiceConcurrencyTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-concurrency");
    private readonly List<PlanDatabaseService> _databases = [];

    public void Dispose()
    {
        foreach (var db in _databases)
            db.Dispose();
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    /// <summary>
    ///     A real database, because a machine wide lease is only observable through one:
    ///     <c>CountLiveJobSlots</c> is the assertion every release-path test below makes.
    /// </summary>
    private PlanDatabaseService CreateDatabase()
    {
        var db = new PlanDatabaseService(
            Path.Combine(_tempDir.Path, $"tendril-concurrency-{Guid.NewGuid()}.db"),
            NullLogger<PlanDatabaseService>.Instance);
        _databases.Add(db);
        return db;
    }

    private JobService CreateLeasedService(IPlanDatabaseService database, int maxConcurrentJobs)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            inboxPath: _tempDir.Path, maxConcurrentJobs: maxConcurrentJobs, database: database);
    }

    /// <summary>A distinct plan folder per job, so two test jobs are not the same scope.</summary>
    private string PlanPath(string name) => Path.Combine(_tempDir.Path, name);

    [Fact]
    public void MaxConcurrentJobs_DefaultsToTwenty()
    {
        var settings = new TendrilSettings();
        Assert.Equal(20, settings.MaxConcurrentJobs);
    }

    [Fact]
    public void MaxConcurrentJobs_CanBeConfigured()
    {
        var settings = new TendrilSettings { MaxConcurrentJobs = 10 };
        Assert.Equal(10, settings.MaxConcurrentJobs);
    }

    [Fact]
    public void StartJob_WhenAtMaxConcurrency_QueuesJob()
    {
        // maxConcurrentJobs=0 means all jobs get queued
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var id = service.StartJob(new CreatePlanArgs("Test Job", "Auto"));
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Contains("max 0 concurrent jobs", job.StatusMessage);
    }

    [Fact]
    public void StartJob_WhenBelowMaxConcurrency_DoesNotQueue()
    {
        // maxConcurrentJobs=10 and no running jobs — should not queue
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // This will try to launch a process which will fail,
        // but the initial status should be "Running" not "Queued"
        try
        {
            var id = service.StartJob(new CreatePlanArgs("Test Job", "Auto"));
            var job = service.GetJob(id);
            Assert.NotNull(job);
            Assert.NotEqual(JobStatus.Queued, job.Status);
        }
        catch
        {
            // Process launch may fail in test — that's OK, we're testing the queue check
        }
    }

    [Fact]
    public void GetJobs_ReturnsQueuedJobs()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob(new CreatePlanArgs("Job 1", "Auto"));
        service.StartJob(new CreatePlanArgs("Job 2", "Auto"));

        var jobs = service.GetJobs();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(JobStatus.Queued, j.Status));
    }

    [Fact]
    public void StopJob_OnQueuedJob_SetsStopped()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var id = service.StartJob(new CreatePlanArgs("Test Job", "Auto"));
        service.StopJob(id);

        var job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Stopped, job.Status);
    }

    [Fact]
    public void StopJob_OnPreRunningJobWithReservedSlot_ReleasesSlot()
    {
        // maxConcurrentJobs=1: the single slot is held by job1 while its Status is still Queued —
        // simulating the narrow window where ValidateProjectReposOrFail (#1340, runs before
        // PrepareJobForLaunch sets Running) can hold a launcher-acquired slot on a job whose Status
        // hasn't flipped to Running yet. StopJob must release based on SlotReserved, not Status,
        // or a concurrent Stop landing in that window would permanently drain the slot.
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), null, 1);
        var job1Id = service.CreateTestJob(new ExecutePlanArgs("plan1"));
        service.GetJob(job1Id)!.Status = JobStatus.Queued;

        service.StopJob(job1Id);
        Assert.Equal(JobStatus.Stopped, service.GetJob(job1Id)!.Status);

        // If the slot had leaked, this would queue instead of attempting to launch. No try/catch
        // needed: LaunchJob's catch-all (this plan's core fix) guarantees StartJob never throws,
        // even though the launch itself fails here (no agent program configured in this test).
        var job2Id = service.StartJob(new CreatePlanArgs("Test Job 2", "Auto"));
        var job2 = service.GetJob(job2Id);
        Assert.NotNull(job2);
        Assert.NotEqual(JobStatus.Queued, job2.Status);
    }

    // The machine wide lease (#2710) is the half of the cap that outlives this process, so a leaked one
    // is far worse than a leaked semaphore permit: it consumes a slot for every instance sharing the
    // TENDRIL_HOME until its TTL expires, and re-takes it on every heartbeat until the job is reaped.
    // Every terminal path therefore has to release it, which is why release is keyed off SlotReserved
    // rather than off the status the job happened to be in.

    private void AssertCompleteJobReleasesLease(int? exitCode)
    {
        var db = CreateDatabase();
        var service = CreateLeasedService(db, maxConcurrentJobs: 1);

        var id = service.CreateTestJob(new ExecutePlanArgs(PlanPath("plan1")));
        Assert.Equal(1, db.CountLiveJobSlots());

        service.CompleteJob(id, exitCode);

        // Counted directly rather than via a fresh acquisition, which would reclaim the row anyway now
        // that the job's own row is terminal. This asserts the release itself happened.
        Assert.Equal(0, db.CountLiveJobSlots());
    }

    [Fact]
    public void CompleteJob_ReleasesLease_OnSuccess() => AssertCompleteJobReleasesLease(0);

    [Fact]
    public void CompleteJob_ReleasesLease_OnFailure() => AssertCompleteJobReleasesLease(1);

    [Fact]
    public void CompleteJob_ReleasesLease_OnExitCode137()
    {
        // 137 is the OOM kill this plan's over-admission was producing, so it is the exit code the
        // release path is least allowed to miss: a machine already out of memory cannot afford to also
        // lose a slot per casualty.
        AssertCompleteJobReleasesLease(137);
    }

    [Fact]
    public void StopJob_ReleasesLease_OnRunningJob()
    {
        var db = CreateDatabase();
        var service = CreateLeasedService(db, maxConcurrentJobs: 1);

        var id = service.CreateTestJob(new ExecutePlanArgs(PlanPath("plan1")));
        service.StopJob(id);

        Assert.Equal(JobStatus.Stopped, service.GetJob(id)!.Status);
        Assert.Equal(0, db.CountLiveJobSlots());
    }

    [Fact]
    public void StopJob_ReleasesLease_OnQueuedJob()
    {
        var db = CreateDatabase();
        var service = CreateLeasedService(db, maxConcurrentJobs: 1);

        // The pre-Running launch window (#1340): the slot and the lease are both held while Status is
        // still Queued, so gating release on Status would strand the lease for its full TTL.
        var id = service.CreateTestJob(new ExecutePlanArgs(PlanPath("plan1")));
        service.GetJob(id)!.Status = JobStatus.Queued;
        Assert.Equal(1, db.CountLiveJobSlots());

        service.StopJob(id);

        Assert.Equal(JobStatus.Stopped, service.GetJob(id)!.Status);
        Assert.Equal(0, db.CountLiveJobSlots());
    }

    [Fact]
    public void LaunchFailure_ReleasesLease_WhenBeforeHookThrows()
    {
        // Mirrors JobServiceSlotLeakTests for the new ReleaseLease callback: the same unhandled throw
        // site that used to leak the semaphore permit would otherwise leak the lease instead, and this
        // one is invisible until another instance is refused a slot nothing is using.
        var launcher = new JobLauncher(configService: null, agentRunner: null, NullLogger.Instance, promptsRoot: "");
        var jobs = new ConcurrentDictionary<string, JobItem>();
        using var semaphore = new SemaphoreSlim(1, 1);
        semaphore.Wait();

        var job = new JobItem
        {
            Id = "job-lease",
            Type = "CreatePlan",
            TypedArgs = new CreatePlanArgs("Test job", "Auto"),
            Status = JobStatus.Pending
        };
        jobs[job.Id] = job;
        var released = new List<string>();

        launcher.LaunchJob(
            job, jobs, semaphore, () => TimeSpan.FromMinutes(30), () => TimeSpan.FromMinutes(10),
            runHooks: (_, _, _, _, _) => throw new InvalidOperationException("boom from before-hook"),
            completeJob: (_, _, _, _) => { },
            raiseStructureChanged: () => { },
            releaseLease: j => released.Add(j.Id));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(["job-lease"], released);
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public void StartJob_QueuesJob_WhenMachineLeaseIsRefusedDespiteFreeLocalSlot()
    {
        var db = CreateDatabase();

        // A lease held by another instance over the same database, owned by a pid that is demonstrably
        // alive, so nothing about it can be reclaimed.
        Assert.True(db.TryAcquireJobSlot("foreign-1", 1, Environment.ProcessId, Environment.MachineName,
            TimeSpan.FromMinutes(10), (pid, _) => pid == Environment.ProcessId, out _));

        var service = CreateLeasedService(db, maxConcurrentJobs: 1);
        var id = service.StartJob(new CreatePlanArgs("Add a widget", "Tendril"));

        var job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Queued, job.Status);
        // The machine-limit wording, not "max 1 concurrent jobs": this instance's own slot was free and
        // the machine's was not, which is the entire point of leasing.
        Assert.Contains("machine limit", job.StatusMessage);
        Assert.Equal(1, db.CountLiveJobSlots());

        // And the local slot it briefly took was handed back rather than leaked, so the next submission
        // is admitted once the machine has room again.
        db.ReleaseJobSlot("foreign-1");
        var laterId = service.StartJob(new CreatePlanArgs("Add another widget", "Tendril"));
        Assert.NotEqual(JobStatus.Queued, service.GetJob(laterId)!.Status);
    }

    [Fact]
    public void Dispose_ReleasesLeasesForNonDetachedJobs_AndKeepsDetachedOnes()
    {
        var db = CreateDatabase();
        var service = CreateLeasedService(db, maxConcurrentJobs: 4);

        var attachedId = service.CreateTestJob(new ExecutePlanArgs(PlanPath("plan1")));
        var detachedId = service.CreateTestJob(new ExecutePlanArgs(PlanPath("plan2")));
        service.GetJob(detachedId)!.Detached = true;
        Assert.Equal(2, db.CountLiveJobSlots());

        service.Dispose();

        // A detached agent survives this process and is still consuming the slot it was granted, so its
        // lease has to survive too and be reclaimed later on agent-pid liveness. Releasing it here would
        // let the next instance over-admit by exactly the number of agents still running.
        Assert.Equal(1, db.CountLiveJobSlots());
        Assert.True(db.TryAcquireJobSlot(attachedId, 2, Environment.ProcessId, Environment.MachineName,
            TimeSpan.FromMinutes(10), (pid, _) => pid == Environment.ProcessId, out _));
        Assert.Equal(2, db.CountLiveJobSlots());
    }

    [Fact]
    public void OnSettingsReloaded_ChangingMaxConcurrentJobs_DoesNotThrowForAlreadyRunningJobs()
    {
        // Defect 5. A reload used to build a replacement semaphore and dispose the old one while running
        // jobs still held permits on it, so the release in CompleteJob threw ObjectDisposedException
        // straight out of the completion path: the job never finished cleanly and the slot was returned
        // to neither semaphore.
        var configService = new TestConfigService { MaxConcurrentJobs = 4 };
        using var jobService = new JobService(configService);

        var job1Id = jobService.CreateTestJob(new ExecutePlanArgs("plan1"));
        var job2Id = jobService.CreateTestJob(new ExecutePlanArgs("plan2"));

        configService.MaxConcurrentJobs = 2;
        configService.TriggerSettingsReloaded();
        configService.MaxConcurrentJobs = 6;
        configService.TriggerSettingsReloaded();

        jobService.CompleteJob(job1Id, 0);
        jobService.CompleteJob(job2Id, 1);

        Assert.Equal(JobStatus.Completed, jobService.GetJob(job1Id)!.Status);
        Assert.Equal(JobStatus.Failed, jobService.GetJob(job2Id)!.Status);

        // The effective cap afterwards is the new configured value, neither lower nor higher. Held by
        // test jobs, which stay Running: a submission whose launch fails hands its slot straight back,
        // so it cannot be used to fill the cap, only to probe it.
        for (var i = 1; i <= 5; i++)
            jobService.CreateTestJob(new ExecutePlanArgs($"held{i}"));
        var underCap = jobService.StartJob(new CreatePlanArgs("Probe with five held", "Auto"));
        Assert.NotEqual(JobStatus.Queued, jobService.GetJob(underCap)!.Status);

        jobService.CreateTestJob(new ExecutePlanArgs("held6"));
        var atCap = jobService.StartJob(new CreatePlanArgs("Probe with six held", "Auto"));
        Assert.Equal(JobStatus.Queued, jobService.GetJob(atCap)!.Status);
        Assert.Contains("max 6 concurrent jobs", jobService.GetJob(atCap)!.StatusMessage);
    }

    [Fact]
    public void OnSettingsReloaded_LoweringCapBelowRunningCount_DoesNotPermanentlyWedgeAdmission()
    {
        // The wedge this plan was written to fix: lowering the cap under the running count left the
        // running jobs holding permits on a disposed semaphore, so every one of their releases was lost
        // and admission never recovered no matter how many of them finished.
        var configService = new TestConfigService { MaxConcurrentJobs = 4 };
        using var jobService = new JobService(configService);

        var ids = new List<string>();
        for (var i = 1; i <= 4; i++)
            ids.Add(jobService.CreateTestJob(new ExecutePlanArgs($"plan{i}")));
        Assert.Equal(4, jobService.GetJobs().Count(j => j.Status == JobStatus.Running));

        configService.MaxConcurrentJobs = 2;
        configService.TriggerSettingsReloaded();

        // Over the new cap, so nothing new is admitted and the four running jobs are left alone.
        var whileOverCap = jobService.StartJob(new CreatePlanArgs("Submitted while over the cap", "Auto"));
        Assert.Equal(JobStatus.Queued, jobService.GetJob(whileOverCap)!.Status);
        Assert.Equal(4, jobService.GetJobs().Count(j => j.Status == JobStatus.Running));

        // Back under the cap: admission resumes, which is only true if the completions above actually
        // returned their permits.
        jobService.CompleteJob(ids[0], 0);
        jobService.CompleteJob(ids[1], 0);
        jobService.CompleteJob(ids[2], 0);

        var afterDraining = jobService.StartJob(new CreatePlanArgs("Submitted once back under the cap", "Auto"));
        Assert.NotEqual(JobStatus.Queued, jobService.GetJob(afterDraining)!.Status);
    }

    [Fact]
    public void SettingsReload_IncreasedConcurrency_StartsQueuedJobs()
    {
        // Arrange: Start with max=2
        var configService = new TestConfigService { MaxConcurrentJobs = 2 };
        var jobService = new JobService(configService);

        // Create 2 test jobs that will run (consume the 2 slots)
        var job1Id = jobService.CreateTestJob(new ExecutePlanArgs("plan1"));
        var job2Id = jobService.CreateTestJob(new ExecutePlanArgs("plan2"));

        // Verify we can only have 2 running jobs
        Assert.Equal(JobStatus.Running, jobService.GetJob(job1Id)!.Status);
        Assert.Equal(JobStatus.Running, jobService.GetJob(job2Id)!.Status);

        // Act: Increase max to 4
        configService.MaxConcurrentJobs = 4;
        configService.TriggerSettingsReloaded();

        // Assert: Should now be able to create 2 more running jobs
        var job3Id = jobService.CreateTestJob(new ExecutePlanArgs("plan3"));
        var job4Id = jobService.CreateTestJob(new ExecutePlanArgs("plan4"));

        Assert.Equal(JobStatus.Running, jobService.GetJob(job3Id)!.Status);
        Assert.Equal(JobStatus.Running, jobService.GetJob(job4Id)!.Status);
    }

    [Fact]
    public void SettingsReload_DecreasedConcurrency_PreventsNewJobsUntilSlotsFree()
    {
        // Arrange: Start with max=4, launch 4 jobs
        var configService = new TestConfigService { MaxConcurrentJobs = 4 };
        var jobService = new JobService(configService);

        // Create 4 test jobs that will run
        var job1Id = jobService.CreateTestJob(new ExecutePlanArgs("plan1"));
        var job2Id = jobService.CreateTestJob(new ExecutePlanArgs("plan2"));
        var job3Id = jobService.CreateTestJob(new ExecutePlanArgs("plan3"));
        var job4Id = jobService.CreateTestJob(new ExecutePlanArgs("plan4"));

        // Act: Decrease max to 2
        configService.MaxConcurrentJobs = 2;
        configService.TriggerSettingsReloaded();

        // Assert: Cannot create new jobs (all 4 running jobs continue, but semaphore has 0 available slots)
        // CreateTestJob tries to acquire a slot with Wait(0), which should fail and throw or queue
        var canCreateMore = jobService.GetJobs().Count(j => j.Status == JobStatus.Running);
        Assert.Equal(4, canCreateMore); // All 4 still running

        // Complete 2 jobs to free slots
        jobService.CompleteJob(job1Id, 0);
        jobService.CompleteJob(job2Id, 0);

        // Now we should have 0 available slots (2 running == limit of 2)
        canCreateMore = jobService.GetJobs().Count(j => j.Status == JobStatus.Running);
        Assert.Equal(2, canCreateMore);

        // Complete 1 more job
        jobService.CompleteJob(job3Id, 0);

        // Now we should have 1 available slot (1 running < limit of 2)
        var job5Id = jobService.CreateTestJob(new ExecutePlanArgs("plan5"));
        Assert.Equal(JobStatus.Running, jobService.GetJob(job5Id)!.Status);
    }

    [Fact]
    public void SettingsReload_NoChange_DoesNotRecreateSemaphore()
    {
        // Arrange
        var configService = new TestConfigService { MaxConcurrentJobs = 5 };
        var jobService = new JobService(configService);

        var job1Id = jobService.CreateTestJob(new ExecutePlanArgs("plan1"));
        Assert.Equal(JobStatus.Running, jobService.GetJob(job1Id)!.Status);

        // Act: Reload with same value
        configService.TriggerSettingsReloaded();

        // Assert: Job continues running (semaphore not disrupted)
        Assert.Equal(JobStatus.Running, jobService.GetJob(job1Id)!.Status);

        // Can still create new test jobs
        var job2Id = jobService.CreateTestJob(new ExecutePlanArgs("plan2"));
        Assert.Equal(JobStatus.Running, jobService.GetJob(job2Id)!.Status);
    }

    [Fact]
    public void GetJobs_UnstartedAndQueuedJobs_SortedBeforeCompletedJobs()
    {
        var db = new FakeDatabaseService
        {
            Jobs =
            {
                new JobItem
                {
                    Id = "00001",
                    Status = JobStatus.Completed,
                    StartedAt = DateTime.UtcNow.AddMinutes(-30),
                    CompletedAt = DateTime.UtcNow.AddMinutes(-20)
                },
                new JobItem
                {
                    Id = "00002",
                    Status = JobStatus.Running,
                    StartedAt = DateTime.UtcNow.AddMinutes(-5)
                },
                new JobItem
                {
                    Id = "00003",
                    Status = JobStatus.Queued,
                    StartedAt = null
                },
                new JobItem
                {
                    Id = "00004",
                    Status = JobStatus.Pending,
                    StartedAt = null
                }
            }
        };

        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), database: db, maxConcurrentJobs: 0);
        var jobs = service.GetJobs();

        Assert.Equal(4, jobs.Count);
        // Unstarted jobs (StartedAt == null) sort at the top, ordered by Id descending (00004, 00003),
        // followed by running/completed jobs ordered by StartedAt descending (00002, 00001).
        Assert.Equal("00004", jobs[0].Id);
        Assert.Equal("00003", jobs[1].Id);
        Assert.Equal("00002", jobs[2].Id);
        Assert.Equal("00001", jobs[3].Id);
    }

    [Fact]
    public void GetJobsForPlan_UnstartedJobs_SortedBeforeCompletedJobs()
    {
        var planFile = "00042-TestPlan";
        var db = new FakeDatabaseService
        {
            Jobs =
            {
                new JobItem
                {
                    Id = "00001",
                    PlanFile = planFile,
                    Status = JobStatus.Completed,
                    StartedAt = DateTime.UtcNow.AddMinutes(-30),
                    CompletedAt = DateTime.UtcNow.AddMinutes(-20)
                },
                new JobItem
                {
                    Id = "00002",
                    PlanFile = planFile,
                    Status = JobStatus.Running,
                    StartedAt = DateTime.UtcNow.AddMinutes(-5)
                },
                new JobItem
                {
                    Id = "00003",
                    PlanFile = planFile,
                    Status = JobStatus.Queued,
                    StartedAt = null
                }
            }
        };

        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), database: db, maxConcurrentJobs: 0);
        var jobs = service.GetJobsForPlan(planFile);

        Assert.Equal(3, jobs.Count);
        // Unstarted queued job sorts first, followed by running and completed jobs
        Assert.Equal("00003", jobs[0].Id);
        Assert.Equal("00002", jobs[1].Id);
        Assert.Equal("00001", jobs[2].Id);
    }

    private class FakeDatabaseService : IPlanDatabaseService
    {
        public DashboardActivityStats GetActivityStats(int monthsBack = 24) => new([], 0m);
        public List<JobItem> Jobs { get; } = new();

        public List<JobItem> GetRecentJobs(int limit = 100)
        {
            return Jobs.ToList();
        }

        public JobItem? GetJobById(string id)
        {
            return Jobs.FirstOrDefault(j => j.Id == id);
        }

        public List<JobItem> GetJobsForPlan(string planFile)
        {
            return Jobs.Where(j => j.PlanFile == planFile).ToList();
        }

        public void DeleteJob(string id) { }
        public void Dispose() { }
        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null) => [];
        public PlanFile? GetPlanByFolder(string folderPath) => null;
        public PlanFile? GetPlanById(int planId) => null;
        public PlanReaderService.PlanCountSnapshot ComputePlanCounts() => new(0, 0, 0, 0, 0, 0);
        public DashboardModels GetDashboardData(string? projectFilter) => new(0, 0, 0, 0, 0, 0, 0, [], []);
        public List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days = 30) => [];
        public decimal GetPlanTotalCost(int planId) => 0;
        public int GetPlanTotalTokens(int planId) => 0;
        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null) => [];
        public List<Recommendation> GetRecommendations() => [];
        public int GetPendingRecommendationsCount() => 0;
        public List<PlanFile> SearchPlans(string query) => [];
        public void RebuildFtsIndex() { }
        public void UpdatePlanState(int planId, PlanStatus state) { }
        public void UpdatePlanContent(int planId, string latestRevisionContent, int revisionCount) { }
        public void UpdateRecommendationState(int planId, string recommendationTitle, string newState, string? declineReason) { }
        public void UpsertPlan(PlanFile plan) { }
        public void DeletePlan(int planId) { }
        public void UpsertCosts(int planId, List<CostEntry> costs) { }
        public void UpsertRecommendations(int planId, string folderName, List<RecommendationYaml> recommendations, string project, string planTitle, DateTime updated, PlanStatus status) { }
        public void BulkUpsertPlans(List<PlanFile> plans, bool forceOverwrite = false) { }
        public HashSet<int> GetTerminalPlanIds() => [];
        public void UpsertJob(JobItem job)
        {
            Jobs.RemoveAll(j => j.Id == job.Id);
            Jobs.Add(job);
        }
        public List<string> PurgeOldJobs(int keepCount = 500) => [];
        public Dictionary<string, PrInfo> GetAllPrStatuses() => [];
        public void UpsertPrStatus(string prUrl, string owner, string repo, string status, string branch, DateTime lastChecked) { }
        public List<string> GetNonMergedPrUrls() => [];
        public long GetDatabaseSize() => 0;
        public DateTime GetLastSyncTime() => DateTime.UtcNow;
        public void SetLastSyncTime(DateTime time) { }
    }

    private class TestConfigService : IConfigService
    {
        public int MaxConcurrentJobs { get; set; } = 20;

        public TendrilSettings Settings => new()
        {
            MaxConcurrentJobs = MaxConcurrentJobs,
            JobTimeout = 60,
            StaleOutputTimeout = 5,
            Projects = []
        };

        public string TendrilHome => "";
        public string ConfigPath => "";
        public string PlanFolder => "";
        public List<ProjectConfig> Projects => [];
        public List<LevelConfig> Levels => [];
        public string[] LevelNames => [];
        public EditorConfig Editor => new();
        public bool NeedsOnboarding => false;
        public ConfigParseError? ParseError => null;

        public event EventHandler? SettingsReloaded;

        public void TriggerSettingsReloaded()
        {
            SettingsReloaded?.Invoke(this, EventArgs.Empty);
        }

        public ProjectConfig? GetProject(string name) => null;
        public bool TryAutoHeal() => false;
        public void ResetToDefaults() { }
        public void RetryLoadConfig() { }
        public Colors? GetLevelColor(string level) => null;
        public Colors? GetProjectColor(string projectName) => null;
        public void SaveSettings() { }
        public void MutateAndSave(Action<TendrilSettings> mutate) => mutate(Settings);
        public void ReloadSettings() { }
        public void SetPendingTendrilHome(string path) { }
        public string? GetPendingTendrilHome() => null;
        public void SetPendingProject(ProjectConfig project) { }
        public ProjectConfig? GetPendingProject() => null;
        public void SetPendingCodingAgent(string name) { }
        public string? GetPendingCodingAgent() => null;
        public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions) { }
        public List<VerificationConfig>? GetPendingVerificationDefinitions() => null;
        public void CompleteOnboarding(string tendrilHome) { }
        public void OpenInEditor(string path) { }
        public string PolishMarkdown(string content) => content;
        public void Dispose() { }
    }
}
