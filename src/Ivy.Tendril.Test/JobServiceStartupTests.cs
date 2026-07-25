using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceStartupTests
{
    /// <summary>Lays down the eventwire file a previous run would have streamed to disk.</summary>
    private static void WriteEventWire(string tendrilHome, JobItem job, params string[] lines)
    {
        JobLogPaths.EnsureJobsDir(tendrilHome);
        File.WriteAllLines(JobLogPaths.EventWire(tendrilHome, job), lines);
    }

    [Fact]
    public void LoadHistoricalJobs_LoadsAllRecentJobs()
    {
        // Arrange: seed database with completed and active jobs
        var db = new FakeDatabaseService
        {
            Jobs =
            {
                new JobItem { Id = "job-1", Status = JobStatus.Completed },
                new JobItem { Id = "job-2", Status = JobStatus.Failed },
                new JobItem { Id = "job-3", Status = JobStatus.Timeout },
                new JobItem { Id = "job-4", Status = JobStatus.Stopped },
                new JobItem { Id = "job-5", Status = JobStatus.Running },
                new JobItem { Id = "job-6", Status = JobStatus.Pending },
                new JobItem { Id = "job-7", Status = JobStatus.Queued },
                new JobItem { Id = "job-8", Status = JobStatus.Blocked }
            }
        };

        // Act: create JobService (which calls LoadHistoricalJobs)
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            database: db);

        // Assert: all jobs from the database are loaded (eviction happens later)
        var jobs = service.GetJobs().ToList();
        Assert.Equal(8, jobs.Count);
        Assert.Contains(jobs, j => j.Id == "job-1"); // Completed
        Assert.Contains(jobs, j => j.Id == "job-2"); // Failed
        Assert.Contains(jobs, j => j.Id == "job-3"); // Timeout
        Assert.Contains(jobs, j => j.Id == "job-4"); // Stopped
        Assert.Contains(jobs, j => j.Id == "job-5"); // Running
        Assert.Contains(jobs, j => j.Id == "job-6"); // Pending
        Assert.Contains(jobs, j => j.Id == "job-7"); // Queued
        Assert.Contains(jobs, j => j.Id == "job-8"); // Blocked
    }

    [Fact]
    public void LoadHistoricalJobs_NoDatabaseProvided_DoesNotThrow()
    {
        // Act & Assert: creating service without database should not throw
        var exception = Record.Exception(() => new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            database: null));

        Assert.Null(exception);
    }

    [Fact]
    public void LoadHistoricalJobs_DatabaseThrows_DoesNotBlockStartup()
    {
        // Arrange: database that throws on GetRecentJobs
        var db = new FakeDatabaseService { ThrowOnGetRecentJobs = true };

        // Act & Assert: creating service should not throw
        var exception = Record.Exception(() => new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            database: db));

        Assert.Null(exception);
    }

    [Fact]
    public void GetJob_AfterRestart_RehydratesOutputFromEventWireFile()
    {
        var tendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-restart-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tendrilHome);
        try
        {
            // The EventWire file the original run streamed to disk. Its name is derived from
            // Id/Type/PlanFile, so those must match the row that comes back from the DB.
            var originalJob = new JobItem { Id = "job-restart", Type = "ExecutePlan" };
            WriteEventWire(tendrilHome, originalJob, "hello", "world");

            // The SQLite row as it comes back on reload: metadata only, no output (matches production —
            // OutputLines is never a database column).
            var db = new FakeDatabaseService
            {
                Jobs = { new JobItem { Id = "job-restart", Status = JobStatus.Completed, Type = "ExecutePlan" } }
            };

            // Act: construct a fresh JobService over the same TendrilHome (simulating restart).
            var config = new FakeConfigService(tendrilHome);
            var service = new JobService(config, database: db);
            var reloaded = service.GetJob("job-restart");

            Assert.NotNull(reloaded);
            Assert.Equal(new[] { "hello", "world" }, reloaded!.OutputLines.ToArray());
        }
        finally
        {
            try { Directory.Delete(tendrilHome, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void GetJob_HydratesOutputAtMostOnce()
    {
        var tendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-hydrate-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tendrilHome);
        try
        {
            var originalJob = new JobItem { Id = "job-hydrate", Type = "ExecutePlan" };
            WriteEventWire(tendrilHome, originalJob, "only-line");

            var db = new FakeDatabaseService
            {
                Jobs = { new JobItem { Id = "job-hydrate", Status = JobStatus.Completed, Type = "ExecutePlan" } }
            };

            var config = new FakeConfigService(tendrilHome);
            var service = new JobService(config, database: db);

            var first = service.GetJob("job-hydrate");
            Assert.NotNull(first);
            Assert.Single(first!.OutputLines);

            // Delete the backing file — if GetJob re-read from disk on every call, the
            // second call would come back empty instead of using the cached, hydrated lines.
            File.Delete(JobLogPaths.EventWire(tendrilHome, originalJob));

            var second = service.GetJob("job-hydrate");
            Assert.NotNull(second);
            Assert.Single(second!.OutputLines);
        }
        finally
        {
            try { Directory.Delete(tendrilHome, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void UpdateJobStatus_JobOnlyInDatabase_RehydratesAndSucceeds()
    {
        // Simulates a master restart: the job is gone from the in-memory dictionary
        // (fresh JobService, no StartJob call) but was persisted to the database while running.
        var db = new FakeDatabaseService
        {
            Jobs = { new JobItem { Id = "job-restarted", Status = JobStatus.Running } }
        };
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), database: db);

        var ok = service.UpdateJobStatus("job-restarted", "Running verifications...", "01234", "My Plan");

        Assert.True(ok);
        var job = service.GetJobs().Single(j => j.Id == "job-restarted");
        Assert.Equal("Running verifications...", job.StatusMessage);
        Assert.Equal("01234", job.ReportedPlanId);
        Assert.Equal("My Plan", job.ReportedPlanTitle);
    }

    [Fact]
    public void ReportJobFailure_JobOnlyInDatabase_RehydratesAndSucceeds()
    {
        var db = new FakeDatabaseService
        {
            Jobs = { new JobItem { Id = "job-restarted", Status = JobStatus.Running } }
        };
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), database: db);

        var ok = service.ReportJobFailure("job-restarted", "Worktree creation failed");

        Assert.True(ok);
        var job = service.GetJobs().Single(j => j.Id == "job-restarted");
        Assert.Equal("Worktree creation failed", job.ReportedFailureReason);
    }

    [Fact]
    public void UpdateJobStatus_UnknownToBothMemoryAndDatabase_ReturnsFalse()
    {
        var db = new FakeDatabaseService();
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), database: db);

        Assert.False(service.UpdateJobStatus("nonexistent", "message"));
    }

    [Fact]
    public void StartJob_PersistsJobToDatabaseWhileStillRunning()
    {
        var db = new FakeDatabaseService();
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), database: db);

        // StartJob persists the job as soon as it is registered in StartJobInternal, not just
        // on completion, so the row shows up even though the process launch below will fail
        // in this test environment (no real coding agent available).
        string id;
        try
        {
            id = service.StartJob(new CreatePlanArgs("Test Job", "Auto"));
        }
        catch
        {
            id = service.GetJobs().Single().Id;
        }

        Assert.NotNull(db.GetJobById(id));
    }

    private class FakeConfigService : IConfigService
    {
        public FakeConfigService(string tendrilHome)
        {
            TendrilHome = tendrilHome;
        }

        public TendrilSettings Settings => new();
        public string TendrilHome { get; }
        public string ConfigPath => "";
        public string PlanFolder => "";
        public List<ProjectConfig> Projects => [];
        public List<LevelConfig> Levels => [];
        public string[] LevelNames => [];
        public EditorConfig Editor => new() { Command = "code", Label = "VS Code" };
        public bool NeedsOnboarding => false;
        public ConfigParseError? ParseError => null;

        public ProjectConfig? GetProject(string name) => null;
        public Colors? GetLevelColor(string level) => null;
        public Colors? GetProjectColor(string projectName) => null;
        public void SaveSettings() { }
        public void ReloadSettings() { }
        public bool TryAutoHeal() => false;
        public void ResetToDefaults() { }
        public void RetryLoadConfig() { }
#pragma warning disable CS0067
        public event EventHandler? SettingsReloaded;
#pragma warning restore CS0067
        public void SetPendingCodingAgent(string name) { }
        public string? GetPendingCodingAgent() => null;
        public void SetPendingTendrilHome(string path) { }
        public string? GetPendingTendrilHome() => null;
        public void SetPendingProject(ProjectConfig project) { }
        public ProjectConfig? GetPendingProject() => null;
        public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions) { }
        public List<VerificationConfig>? GetPendingVerificationDefinitions() => null;
        public void CompleteOnboarding(string tendrilHome) { }
        public void OpenInEditor(string path) { }
        public string PolishMarkdown(string content) => content;
        public void Dispose() { }
    }

    private class FakeDatabaseService : IPlanDatabaseService
    {
        public List<JobItem> Jobs { get; } = new();
        public bool ThrowOnGetRecentJobs { get; init; }

        public List<JobItem> GetRecentJobs(int limit = 100)
        {
            if (ThrowOnGetRecentJobs) throw new Exception("DB error");
            return Jobs;
        }

        public JobItem? GetJobById(string id)
        {
            return Jobs.FirstOrDefault(j => j.Id == id);
        }

        public List<JobItem> GetJobsForPlan(string planFile)
        {
            return Jobs.Where(j => j.PlanFile == planFile).ToList();
        }

        public void DeleteJob(string id)
        {
        }

        public void Dispose()
        {
        }

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null)
        {
            return new List<PlanFile>();
        }

        public PlanFile? GetPlanByFolder(string folderPath)
        {
            return null;
        }

        public PlanFile? GetPlanById(int planId)
        {
            return null;
        }

        public PlanReaderService.PlanCountSnapshot ComputePlanCounts()
        {
            return new PlanReaderService.PlanCountSnapshot(0, 0, 0, 0, 0, 0);
        }

        public DashboardModels GetDashboardData(string? projectFilter)
        {
            return new DashboardModels(0, 0, 0, 0, 0, 0, 0, new List<DashboardDayStats>(), new List<ProjectCount>());
        }

        public List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days = 30)
        {
            return new List<(DateOnly Date, int Count)>();
        }

        public decimal GetPlanTotalCost(int planId)
        {
            return 0;
        }

        public int GetPlanTotalTokens(int planId)
        {
            return 0;
        }

        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null)
        {
            return new List<HourlyTokenBurn>();
        }

        public List<Recommendation> GetRecommendations()
        {
            return new List<Recommendation>();
        }

        public int GetPendingRecommendationsCount()
        {
            return 0;
        }

        public List<PlanFile> SearchPlans(string query)
        {
            return new List<PlanFile>();
        }

        public void RebuildFtsIndex()
        {
        }

        public void UpdatePlanState(int planId, PlanStatus state)
        {
        }

        public void UpdatePlanContent(int planId, string latestRevisionContent, int revisionCount)
        {
        }

        public void UpdateRecommendationState(int planId, string recommendationTitle, string newState, string? declineReason)
        {
        }

        public void UpsertPlan(PlanFile plan)
        {
        }

        public void DeletePlan(int planId)
        {
        }

        public void UpsertCosts(int planId, List<CostEntry> costs)
        {
        }

        public void UpsertRecommendations(int planId, string folderName, List<RecommendationYaml> recommendations,
            string project, string planTitle, DateTime updated, PlanStatus status)
        {
        }

        public void BulkUpsertPlans(List<PlanFile> plans, bool forceOverwrite = false)
        {
        }

        public HashSet<int> GetTerminalPlanIds()
        {
            return new HashSet<int>();
        }

        public void UpsertJob(JobItem job)
        {
            Jobs.RemoveAll(j => j.Id == job.Id);
            Jobs.Add(job);
        }

        public List<string> PurgeOldJobs(int keepCount = 500)
        {
            return new List<string>();
        }

        public Dictionary<string, string> GetAllPrStatuses()
        {
            return new Dictionary<string, string>();
        }

        public void UpsertPrStatus(string prUrl, string owner, string repo, string status, DateTime lastChecked)
        {
        }

        public List<string> GetNonMergedPrUrls()
        {
            return new List<string>();
        }

        public long GetDatabaseSize()
        {
            return 0;
        }

        public DateTime GetLastSyncTime()
        {
            return DateTime.UtcNow;
        }

        public void SetLastSyncTime(DateTime time)
        {
        }
    }
}
