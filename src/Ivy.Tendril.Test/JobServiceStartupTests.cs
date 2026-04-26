using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceStartupTests
{
    [Fact]
    public void LoadHistoricalJobs_ExcludesCompletedJobs()
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

        // Assert: only non-terminal jobs are loaded
        var jobs = service.GetJobs().ToList();
        Assert.Equal(4, jobs.Count);
        Assert.Contains(jobs, j => j.Id == "job-5"); // Running
        Assert.Contains(jobs, j => j.Id == "job-6"); // Pending
        Assert.Contains(jobs, j => j.Id == "job-7"); // Queued
        Assert.Contains(jobs, j => j.Id == "job-8"); // Blocked

        // Terminal jobs should not be loaded
        Assert.DoesNotContain(jobs, j => j.Id == "job-1"); // Completed
        Assert.DoesNotContain(jobs, j => j.Id == "job-2"); // Failed
        Assert.DoesNotContain(jobs, j => j.Id == "job-3"); // Timeout
        Assert.DoesNotContain(jobs, j => j.Id == "job-4"); // Stopped
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

    private class FakeDatabaseService : IPlanDatabaseService
    {
        public List<JobItem> Jobs { get; } = new();
        public bool ThrowOnGetRecentJobs { get; init; }

        public List<JobItem> GetRecentJobs(int limit = 100)
        {
            if (ThrowOnGetRecentJobs) throw new Exception("DB error");
            return Jobs;
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

        public decimal GetPlanTotalCost(int planId)
        {
            return 0;
        }

        public int GetPlanTotalTokens(int planId)
        {
            return 0;
        }

        public List<LatestJobsPerType> GetLatestJobsByType(int limit = 10)
        {
            return new List<LatestJobsPerType>();
        }

        public void UpsertJob(JobItem job)
        {
        }

        public void UpsertPlan(PlanFile plan)
        {
        }

        public void PurgeOldJobs(int keepCount = 500)
        {
        }

        public Dictionary<string, string> GetAllPrStatuses()
        {
            return new Dictionary<string, string>();
        }

        public void UpsertPrStatus(string prUrl, string status)
        {
        }

        public void RemovePrStatus(string prUrl)
        {
        }

        public void LogCostEntry(string jobId, decimal inputCost, decimal outputCost, int inputTokens, int outputTokens)
        {
        }

        public List<JobCostEntry> GetJobCostEntries(string jobId)
        {
            return new List<JobCostEntry>();
        }
    }
}
