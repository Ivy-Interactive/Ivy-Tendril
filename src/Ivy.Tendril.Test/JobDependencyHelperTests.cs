using Ivy.Tendril.Apps.Jobs.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Xunit;

namespace Ivy.Tendril.Test;

public class JobDependencyHelperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _plansDir;

    public JobDependencyHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JobDepTests_{Guid.NewGuid():N}");
        _plansDir = Path.Combine(_tempDir, "Plans");
        Directory.CreateDirectory(_plansDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void GetBlockingDependencies_WaitForJobIds_ResolvesBlockingJobs()
    {
        var depJob = new JobItem
        {
            Id = "job-100",
            Type = "ExecutePlan",
            Status = JobStatus.Running,
            PlanFile = "00012-MyDepPlan"
        };

        var blockedJob = new JobItem
        {
            Id = "job-200",
            Type = "ExecutePlan",
            Status = JobStatus.Blocked,
            WaitForJobIds = ["job-100"]
        };

        var jobService = new TestJobService([depJob, blockedJob]);
        var planService = new TestPlanReaderService(_plansDir);

        var blockingDeps = JobDependencyHelper.GetBlockingDependencies(blockedJob, jobService, planService);

        Assert.Single(blockingDeps);
        Assert.Equal("job-100", blockingDeps[0].JobId);
        Assert.Equal("ExecutePlan", blockingDeps[0].JobType);
        Assert.Equal(JobStatus.Running, blockingDeps[0].JobStatus);
        Assert.Equal("00012", blockingDeps[0].PlanId);
    }

    [Fact]
    public void GetBlockingDependencies_PlanDependsOn_WithActiveJob_ResolvesBlockingJob()
    {
        // Setup dependency plan folder
        var depPlanFolder = Path.Combine(_plansDir, "00020-BasePlan");
        Directory.CreateDirectory(depPlanFolder);
        File.WriteAllText(Path.Combine(depPlanFolder, "plan.yaml"), "title: Base Plan\nstate: Executing\n");

        // Setup current plan folder with dependsOn
        var currentPlanFolder = Path.Combine(_plansDir, "00021-DependentPlan");
        Directory.CreateDirectory(currentPlanFolder);
        File.WriteAllText(Path.Combine(currentPlanFolder, "plan.yaml"), "title: Dependent Plan\nstate: Blocked\ndependsOn:\n  - 00020-BasePlan\n");

        var depJob = new JobItem
        {
            Id = "job-300",
            Type = "ExecutePlan",
            Status = JobStatus.Running,
            PlanFile = "00020-BasePlan"
        };

        var blockedJob = new JobItem
        {
            Id = "job-301",
            Type = "ExecutePlan",
            Status = JobStatus.Blocked,
            PlanFile = "00021-DependentPlan",
            TypedArgs = new ExecutePlanArgs(currentPlanFolder)
        };

        var jobService = new TestJobService([depJob, blockedJob]);
        var planService = new TestPlanReaderService(_plansDir);

        var blockingDeps = JobDependencyHelper.GetBlockingDependencies(blockedJob, jobService, planService);

        Assert.Single(blockingDeps);
        Assert.Equal("job-300", blockingDeps[0].JobId);
        Assert.Equal("00020", blockingDeps[0].PlanId);
        Assert.Equal(JobStatus.Running, blockingDeps[0].JobStatus);
    }

    [Fact]
    public void GetBlockingDependencies_PlanDependsOn_WithoutJob_ResolvesBlockingPlan()
    {
        // Setup dependency plan folder with no jobs
        var depPlanFolder = Path.Combine(_plansDir, "00030-PendingPlan");
        Directory.CreateDirectory(depPlanFolder);
        File.WriteAllText(Path.Combine(depPlanFolder, "plan.yaml"), "title: Pending Plan\nstate: Draft\n");

        var currentPlanFolder = Path.Combine(_plansDir, "00031-DependentPlan");
        Directory.CreateDirectory(currentPlanFolder);
        File.WriteAllText(Path.Combine(currentPlanFolder, "plan.yaml"), "title: Dependent Plan\nstate: Blocked\ndependsOn:\n  - 00030-PendingPlan\n");

        var blockedJob = new JobItem
        {
            Id = "job-400",
            Type = "ExecutePlan",
            Status = JobStatus.Blocked,
            PlanFile = "00031-DependentPlan",
            TypedArgs = new ExecutePlanArgs(currentPlanFolder)
        };

        var jobService = new TestJobService([blockedJob]);
        var planService = new TestPlanReaderService(_plansDir);

        var blockingDeps = JobDependencyHelper.GetBlockingDependencies(blockedJob, jobService, planService);

        Assert.Single(blockingDeps);
        Assert.Null(blockingDeps[0].JobId);
        Assert.Equal("00030", blockingDeps[0].PlanId);
        Assert.Equal("00030-PendingPlan", blockingDeps[0].PlanFolder);
        Assert.Equal("Draft", blockingDeps[0].PlanStatus);
    }

    [Theory]
    [InlineData("Waiting for ExecutePlan of plan 00012 (job 00045)", "00045", "00012")]
    [InlineData("Blocked job 00099 failed", "00099", null)]
    public void GetBlockingDependencies_FallbackStatusMessage_ResolvesFromPattern(string statusMessage, string expectedJobId, string? expectedPlanId)
    {
        var depJob = new JobItem
        {
            Id = expectedJobId,
            Type = "ExecutePlan",
            Status = JobStatus.Running,
            PlanFile = expectedPlanId != null ? $"{expectedPlanId}-SomePlan" : ""
        };

        var blockedJob = new JobItem
        {
            Id = "job-500",
            Type = "ExecutePlan",
            Status = JobStatus.Blocked,
            StatusMessage = statusMessage
        };

        var jobService = new TestJobService([depJob, blockedJob]);
        var planService = new TestPlanReaderService(_plansDir);

        var blockingDeps = JobDependencyHelper.GetBlockingDependencies(blockedJob, jobService, planService);

        Assert.Single(blockingDeps);
        Assert.Equal(expectedJobId, blockingDeps[0].JobId);
        if (expectedPlanId != null)
        {
            Assert.Equal(expectedPlanId, blockingDeps[0].PlanId);
        }
    }

    [Fact]
    public void GetBlockingDependencies_FallbackStatusMessage_DependencyPlanPattern()
    {
        var blockedJob = new JobItem
        {
            Id = "job-600",
            Type = "ExecutePlan",
            Status = JobStatus.Blocked,
            StatusMessage = "Dependency '00050-PreReqPlan' is 'Draft', not Completed"
        };

        var jobService = new TestJobService([blockedJob]);
        var planService = new TestPlanReaderService(_plansDir);

        var blockingDeps = JobDependencyHelper.GetBlockingDependencies(blockedJob, jobService, planService);

        Assert.Single(blockingDeps);
        Assert.Equal("00050", blockingDeps[0].PlanId);
        Assert.Equal("00050-PreReqPlan", blockingDeps[0].PlanFolder);
    }

    private class TestJobService(List<JobItem> jobs) : IJobService
    {
        public List<JobItem> GetJobs() => jobs;
        public JobItem? GetJob(string id) => jobs.FirstOrDefault(j => j.Id == id);
        public List<JobItem> GetJobsForPlan(string planFile) => jobs.Where(j => j.PlanFile == planFile).ToList();
        public string StartJob(JobArgsBase args, string? inboxFilePath = null) => throw new NotImplementedException();
        public void ForceStartJob(string id) => throw new NotImplementedException();
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) => throw new NotImplementedException();
        public void StopJob(string id) => throw new NotImplementedException();
        public int StopAllJobs() => throw new NotImplementedException();
        public int StopQueuedJobs() => throw new NotImplementedException();
        public void DeleteJob(string id) => throw new NotImplementedException();
        public void ClearCompletedJobs() => throw new NotImplementedException();
        public void ClearFailedJobs() => throw new NotImplementedException();
        public void ClearAllJobs() => throw new NotImplementedException();
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => throw new NotImplementedException();
        public bool ReportJobFailure(string id, string message) => throw new NotImplementedException();
        public bool IsInboxFileTracked(string filePath) => false;
        public void Dispose() { }
#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }

    private class TestPlanReaderService(string plansDirectory) : IPlanReaderService
    {
        public string PlansDirectory => plansDirectory;
        public bool IsDatabaseReady => true;
        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null) => [];
        public PlanFile? GetPlanByFolder(string folderPath) => null;
        public List<PlanFile> GetIceboxPlans() => [];
        public void TransitionState(string folderName, PlanStatus newState) { }
        public IReadOnlyList<string> GetFailedVerifications(string folderName) => [];
        public void CompleteWithPartialDelivery(string folderName) { }
        public void ResetToDraft(string folderName) { }
        public void ResetVerificationsForRetry(string folderName) { }
        public void SetVerificationStatus(string folderName, string name, VerificationStatus status) { }
        public void SaveRevision(string folderName, string content) { }
        public void RevertRevision(string folderName) { }
        public string ReadLatestRevision(string folderName) => "";
        public List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName) => [];
        public void DeletePlan(string folderName) { }
        public string ReadRawPlan(string folderName) => "";
        public void SavePlan(string folderName, string fullContent) { }
        public void UpdateLatestRevision(string folderName, string content) { }
        public DashboardModels GetDashboardData(string? projectFilter) => throw new NotImplementedException();
        public DashboardActivityStats GetDashboardActivity(int monthsBack = 24) => throw new NotImplementedException();
        public List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days) => [];
        public decimal GetPlanTotalCost(string folderPath) => 0;
        public int GetPlanTotalTokens(string folderPath) => 0;
        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null) => [];
        public List<Recommendation> GetRecommendations() => [];
        public int GetPendingRecommendationsCount() => 0;
        public PlanReaderService.PlanCountSnapshot ComputePlanCounts() => throw new NotImplementedException();
        public void UpdateRecommendationState(string planFolderName, string recommendationTitle, string newState, string? declineReason = null) { }
        public List<RecommendationYaml> GetRecommendationsForPlan(string folderName) => [];
        public void AcceptRecommendationAndRetry(string folderName, string recommendationTitle) { }
        public void AcceptRecommendationsAndRetry(string folderName, IReadOnlyCollection<string> titles) { }
        public void SyncPlanArtifacts(string planFolder) { }
        public void InvalidateCaches() { }
        public Task FlushPendingWritesAsync() => Task.CompletedTask;
        public void MigratePlans() { }
        public void RecoverStuckPlans() { }
#pragma warning disable CS0067
        public event Action? CountsInvalidated;
#pragma warning restore CS0067
    }
}
