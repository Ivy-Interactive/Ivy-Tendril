using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobsAppRefreshGatingTests
{
    private static JobItem MakeJob(string id, JobStatus status, DateTime? completedAt = null) => new()
    {
        Id = id,
        Type = "ExecutePlan",
        PlanFile = $"{id}-Plan",
        Project = "Test",
        Status = status,
        CompletedAt = completedAt
    };

    [Fact]
    public void ComputeStructuralSignature_StableWhenCostTokensOrStartedAtChangeOnRunningJob()
    {
        var job = MakeJob("job-1", JobStatus.Running);
        var jobs = new List<JobItem> { job };
        var before = JobsApp.ComputeStructuralSignature(jobs);

        job.Cost = 1.23m;
        job.Tokens = 500;
        job.StartedAt = DateTime.UtcNow;
        var after = JobsApp.ComputeStructuralSignature(jobs);

        Assert.Equal(before, after);
    }

    [Fact]
    public void ComputeStructuralSignature_ChangesWhenJobAdded()
    {
        var jobs = new List<JobItem> { MakeJob("job-1", JobStatus.Running) };
        var before = JobsApp.ComputeStructuralSignature(jobs);

        jobs.Add(MakeJob("job-2", JobStatus.Running));
        var after = JobsApp.ComputeStructuralSignature(jobs);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ComputeStructuralSignature_ChangesWhenJobRemoved()
    {
        var jobs = new List<JobItem> { MakeJob("job-1", JobStatus.Running), MakeJob("job-2", JobStatus.Running) };
        var before = JobsApp.ComputeStructuralSignature(jobs);

        jobs.RemoveAt(1);
        var after = JobsApp.ComputeStructuralSignature(jobs);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ComputeStructuralSignature_ChangesWhenStatusTransitionsRunningToCompleted()
    {
        var job = MakeJob("job-1", JobStatus.Running);
        var jobs = new List<JobItem> { job };
        var before = JobsApp.ComputeStructuralSignature(jobs);

        job.Status = JobStatus.Completed;
        var after = JobsApp.ComputeStructuralSignature(jobs);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void BuildDataTableUpdates_FirstCall_ReturnsAllSixCells()
    {
        var jobService = new FakeJobService();
        jobService.Jobs.Add(MakeJob("job-1", JobStatus.Running));
        var cache = new Dictionary<string, string>();

        var updates = JobsApp.BuildDataTableUpdates(jobService, cache).ToList();

        Assert.Equal(6, updates.Count);
    }

    [Fact]
    public void BuildDataTableUpdates_SecondCallWithUnchangedState_ReturnsEmpty()
    {
        var jobService = new FakeJobService();
        jobService.Jobs.Add(MakeJob("job-1", JobStatus.Running));
        var cache = new Dictionary<string, string>();

        JobsApp.BuildDataTableUpdates(jobService, cache).ToList();
        var second = JobsApp.BuildDataTableUpdates(jobService, cache).ToList();

        Assert.Empty(second);
    }

    [Fact]
    public void BuildDataTableUpdates_OnlyChangedCellReturned_AfterCostUpdated()
    {
        var jobService = new FakeJobService();
        var job = MakeJob("job-1", JobStatus.Running);
        jobService.Jobs.Add(job);
        var cache = new Dictionary<string, string>();
        JobsApp.BuildDataTableUpdates(jobService, cache).ToList();

        job.Cost = 4.56m;
        var updates = JobsApp.BuildDataTableUpdates(jobService, cache).ToList();

        var update = Assert.Single(updates);
        Assert.Equal(nameof(JobItemRow.Cost), update.ColumnName);
    }

    [Fact]
    public void BuildDataTableUpdates_PrunesCacheKeysForJobsNoLongerReturned()
    {
        var jobService = new FakeJobService();
        jobService.Jobs.Add(MakeJob("job-1", JobStatus.Running));
        var cache = new Dictionary<string, string>();
        JobsApp.BuildDataTableUpdates(jobService, cache).ToList();
        Assert.NotEmpty(cache);

        jobService.Jobs.Clear();
        JobsApp.BuildDataTableUpdates(jobService, cache).ToList();

        Assert.Empty(cache);
    }

    private class FakeJobService : IJobService
    {
        public List<JobItem> Jobs { get; } = new();

        public string StartJob(JobArgsBase args, string? inboxFilePath = null) => throw new NotSupportedException();
        public void ForceStartJob(string id) => throw new NotSupportedException();
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) => throw new NotSupportedException();
        public void StopJob(string id) => throw new NotSupportedException();
        public int StopAllJobs() => throw new NotSupportedException();
        public void DeleteJob(string id) => throw new NotSupportedException();
        public void ClearCompletedJobs() => throw new NotSupportedException();
        public void ClearFailedJobs() => throw new NotSupportedException();
        public void ClearAllJobs() => throw new NotSupportedException();
        public int StopQueuedJobs() => throw new NotSupportedException();
        public List<JobItem> GetJobs() => Jobs;
        public List<JobItem> GetJobsForPlan(string planFile) => throw new NotSupportedException();
        public JobItem? GetJob(string id) => Jobs.FirstOrDefault(j => j.Id == id);
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => throw new NotSupportedException();
        public bool ReportJobFailure(string id, string message) => throw new NotSupportedException();
        public bool IsInboxFileTracked(string filePath) => false;
        public void Dispose()
        {
        }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }
}
