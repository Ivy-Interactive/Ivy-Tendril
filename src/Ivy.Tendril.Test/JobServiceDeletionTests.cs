using System.Collections.Concurrent;
using System.Reflection;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceDeletionTests
{
    private static JobService CreateService(IPlanDatabaseService? database = null)
    {
        return new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            database: database);
    }

    private static void AddJobDirectly(JobService service, JobItem job)
    {
        var field = typeof(JobService).GetField("_jobs",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var jobs = (ConcurrentDictionary<string, JobItem>)field!.GetValue(service)!;
        jobs[job.Id] = job;
    }

    [Fact]
    public void DeleteJob_RemovesFromMemoryAndCallsDatabase()
    {
        var db = new FakeDatabaseService();
        var service = CreateService(db);
        AddJobDirectly(service, new JobItem { Id = "job-1", Status = JobStatus.Completed });

        service.DeleteJob("job-1");

        Assert.Null(service.GetJob("job-1"));
        Assert.Contains("job-1", db.DeletedJobIds);
    }

    [Fact]
    public void DeleteJob_NonexistentId_DoesNotCallDatabase()
    {
        var db = new FakeDatabaseService();
        var service = CreateService(db);

        service.DeleteJob("nonexistent");

        Assert.Empty(db.DeletedJobIds);
    }

    [Fact]
    public void ClearCompletedJobs_RemovesFromMemoryButNotDatabase()
    {
        var db = new FakeDatabaseService();
        var service = CreateService(db);
        AddJobDirectly(service, new JobItem { Id = "completed-1", Status = JobStatus.Completed });
        AddJobDirectly(service, new JobItem { Id = "completed-2", Status = JobStatus.Completed });
        AddJobDirectly(service, new JobItem { Id = "running-1", Status = JobStatus.Running });

        service.ClearCompletedJobs();

        Assert.Null(service.GetJob("completed-1"));
        Assert.Null(service.GetJob("completed-2"));
        Assert.NotNull(service.GetJob("running-1"));
        Assert.Empty(db.DeletedJobIds);

        // Cleared jobs are soft-deleted: persisted with Cleared=true so they
        // stay out of the Jobs app after restart but remain in plan history
        Assert.Contains(db.UpsertedJobs, j => j.Id == "completed-1" && j.Cleared);
        Assert.Contains(db.UpsertedJobs, j => j.Id == "completed-2" && j.Cleared);
        Assert.DoesNotContain(db.UpsertedJobs, j => j.Id == "running-1");
    }

    [Fact]
    public void ClearFailedJobs_RemovesFromMemoryButNotDatabase()
    {
        var db = new FakeDatabaseService();
        var service = CreateService(db);
        AddJobDirectly(service, new JobItem { Id = "failed-1", Status = JobStatus.Failed });
        AddJobDirectly(service, new JobItem { Id = "timeout-1", Status = JobStatus.Timeout });
        AddJobDirectly(service, new JobItem { Id = "blocked-1", Status = JobStatus.Blocked });
        AddJobDirectly(service, new JobItem { Id = "completed-1", Status = JobStatus.Completed });

        service.ClearFailedJobs();

        Assert.Null(service.GetJob("failed-1"));
        Assert.NotNull(service.GetJob("timeout-1"));
        Assert.NotNull(service.GetJob("blocked-1"));
        Assert.NotNull(service.GetJob("completed-1"));
        Assert.Empty(db.DeletedJobIds);
        Assert.Contains(db.UpsertedJobs, j => j.Id == "failed-1" && j.Cleared);
        Assert.DoesNotContain(db.UpsertedJobs, j => j.Id == "timeout-1" && j.Cleared);
    }

    [Fact]
    public void DeleteJob_DatabaseThrows_StillRemovesFromMemory()
    {
        var db = new FakeDatabaseService { ThrowOnDelete = true };
        var service = CreateService(db);
        AddJobDirectly(service, new JobItem { Id = "job-1", Status = JobStatus.Completed });

        service.DeleteJob("job-1");

        Assert.Null(service.GetJob("job-1"));
    }

    private class FakeDatabaseService : IPlanDatabaseService
    {
        public List<string> DeletedJobIds { get; } = new();
        public List<JobItem> UpsertedJobs { get; } = new();
        public bool ThrowOnDelete { get; init; }

        public void DeleteJob(string id)
        {
            if (ThrowOnDelete) throw new Exception("DB error");
            DeletedJobIds.Add(id);
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

        public void UpdateRecommendationState(int planId, string recommendationTitle, string newState,
            string? declineReason)
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
            UpsertedJobs.Add(job);
        }

        public List<JobItem> GetRecentJobs(int limit = 100)
        {
            return new List<JobItem>();
        }

        public JobItem? GetJobById(string id)
        {
            return null;
        }

        public List<JobItem> GetJobsForPlan(string planFile)
        {
            return new List<JobItem>();
        }

        public void PurgeOldJobs(int keepCount = 500)
        {
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
            return DateTime.MinValue;
        }

        public void SetLastSyncTime(DateTime time)
        {
        }
    }
}
