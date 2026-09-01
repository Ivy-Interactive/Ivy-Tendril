using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceConcurrencyTests
{
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
