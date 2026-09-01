using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Test;

public class JobServiceWaitForJobsTests
{
    [Fact]
    public void StartJob_WithWaitForJobs_BlocksWhenDependencyRunning()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));
        Assert.Equal(JobStatus.Running, service.GetJob(depId)!.Status);

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Blocked, job.Status);
        Assert.Contains(depId, job.StatusMessage);
    }

    [Fact]
    public void StartJob_WithWaitForJobs_DoesNotBlockWhenAllCompleted()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));
        service.CompleteJob(depId, 0);

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.NotEqual(JobStatus.Blocked, job.Status);
    }

    [Fact]
    public void StartJob_WithWaitForJobs_FailsImmediatelyWhenDependencyAlreadyFailed()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));
        service.CompleteJob(depId, 1);

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains(depId, job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_Success_UnblocksWaitingJobs()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        service.CompleteJob(depId, 0);

        // The blocked job should have been removed (restarted as a new job)
        Assert.Null(service.GetJob(waitingId));
        Assert.Contains(notifications, n => n.Title == "Job Unblocked");
    }

    [Fact]
    public void CompleteJob_Failure_CascadesFailureToWaitingJobs()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        service.CompleteJob(depId, 1);

        var waitingJob = service.GetJob(waitingId);
        Assert.NotNull(waitingJob);
        Assert.Equal(JobStatus.Failed, waitingJob.Status);
        Assert.Contains(depId, waitingJob.StatusMessage);
        Assert.Contains(notifications, n => n.Title == "Job Failed");
    }

    [Fact]
    public void CompleteJob_Failure_CascadesTransitively()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        // A -> B -> C chain
        var jobAId = service.CreateTestJob(new CreatePlanArgs("Job A", "Auto"));

        var jobBId = service.StartJob(new CreatePlanArgs("Job B", "Auto") { WaitForJobs = [jobAId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(jobBId)!.Status);

        var jobCId = service.StartJob(new CreatePlanArgs("Job C", "Auto") { WaitForJobs = [jobBId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(jobCId)!.Status);

        // Fail A -> should cascade to B -> should cascade to C
        service.CompleteJob(jobAId, 1);

        var jobB = service.GetJob(jobBId);
        var jobC = service.GetJob(jobCId);

        Assert.NotNull(jobB);
        Assert.Equal(JobStatus.Failed, jobB.Status);

        Assert.NotNull(jobC);
        Assert.Equal(JobStatus.Failed, jobC.Status);
    }

    [Fact]
    public void StopJob_CascadesFailureToWaitingJobs()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        service.StopJob(depId);

        var waitingJob = service.GetJob(waitingId);
        Assert.NotNull(waitingJob);
        Assert.Equal(JobStatus.Failed, waitingJob.Status);
        Assert.Contains(depId, waitingJob.StatusMessage);
    }

    [Fact]
    public void CompleteJob_MultipleWaitForJobs_UnblocksOnlyWhenAllComplete()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var dep1Id = service.CreateTestJob(new CreatePlanArgs("Dep 1", "Auto"));
        var dep2Id = service.CreateTestJob(new CreatePlanArgs("Dep 2", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [dep1Id, dep2Id] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        // Complete first dep — should still be blocked
        service.CompleteJob(dep1Id, 0);
        var stillBlocked = service.GetJob(waitingId);
        Assert.NotNull(stillBlocked);
        Assert.Equal(JobStatus.Blocked, stillBlocked.Status);

        // Complete second dep — should now unblock
        service.CompleteJob(dep2Id, 0);
        Assert.Null(service.GetJob(waitingId));
    }

    [Fact]
    public void StartJob_WithoutWaitForJobs_NotBlocked()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var id = service.StartJob(new CreatePlanArgs("Normal job", "Auto"));
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.NotEqual(JobStatus.Blocked, job.Status);
    }

    [Fact]
    public void StartJob_WithWaitForJobs_MessageNamesPlanWhenDependencyTiedToPlan()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new ExecutePlanArgs("00075-DepPlan"));

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Blocked, job.Status);
        Assert.Equal($"Waiting for ExecutePlan of plan 00075 (job {depId})", job.StatusMessage);
    }

    [Fact]
    public void StartJob_WithWaitForJobs_MessageOmitsPlanWhenDependencyNotTiedToPlan()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Blocked, job.Status);
        Assert.Equal($"Waiting for CreatePlan (job {depId})", job.StatusMessage);
    }

    [Fact]
    public void StartJob_WithMultipleWaitForJobs_MessageListsEachDependency()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var dep1Id = service.CreateTestJob(new ExecutePlanArgs("00075-DepPlan"));
        var dep2Id = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [dep1Id, dep2Id] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Blocked, job.Status);
        Assert.Contains($"ExecutePlan of plan 00075 (job {dep1Id})", job.StatusMessage);
        Assert.Contains($"CreatePlan (job {dep2Id})", job.StatusMessage);
    }

    [Fact]
    public void HandleWaitForJobsDependents_UnblockedJob_DeletesOldRecordFromDatabase()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var db = new FakeDatabaseService();
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            database: db,
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        service.CompleteJob(depId, 0);

        // The blocked job should have been removed from memory and deleted from the database
        Assert.Null(service.GetJob(waitingId));
        Assert.Contains(waitingId, db.DeletedJobIds);
    }

    private class FakeDatabaseService : IPlanDatabaseService
    {
        public List<JobItem> Jobs { get; } = new();
        public List<string> DeletedJobIds { get; } = new();
        public List<string> UpsertedJobIds { get; } = new();

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

        public void DeleteJob(string id)
        {
            DeletedJobIds.Add(id);
            Jobs.RemoveAll(j => j.Id == id);
        }

        public void Dispose()
        {
        }

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null)
        {
            return [];
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
            return new DashboardModels(0, 0, 0, 0, 0, 0, 0, [], []);
        }

        public List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days = 30)
        {
            return [];
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
            return [];
        }

        public List<Recommendation> GetRecommendations()
        {
            return [];
        }

        public int GetPendingRecommendationsCount()
        {
            return 0;
        }

        public List<PlanFile> SearchPlans(string query)
        {
            return [];
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
            return [];
        }

        public void UpsertJob(JobItem job)
        {
            UpsertedJobIds.Add(job.Id);
            Jobs.RemoveAll(j => j.Id == job.Id);
            Jobs.Add(job);
        }

        public List<string> PurgeOldJobs(int keepCount = 500)
        {
            return [];
        }

        public Dictionary<string, PrInfo> GetAllPrStatuses()
        {
            return new Dictionary<string, PrInfo>();
        }

        public void UpsertPrStatus(string prUrl, string owner, string repo, string status, string branch, DateTime lastChecked)
        {
        }

        public List<string> GetNonMergedPrUrls()
        {
            return [];
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
