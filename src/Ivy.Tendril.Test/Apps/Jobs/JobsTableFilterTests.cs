using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Jobs;

public class JobsTableFilterTests
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
    public void JobItemRow_Status_ContainsCleanStringWithoutPrefix_ForEveryJobStatus()
    {
        var planService = new FakePlanReaderService();
        var allStatuses = Enum.GetValues<JobStatus>();
        var jobs = allStatuses.Select((s, i) => MakeJob($"job-{i}", s)).ToList();

        var rows = JobsApp.BuildJobRows(jobs, planService);

        Assert.Equal(allStatuses.Length, rows.Count);
        foreach (var row in rows)
        {
            Assert.False(row.Status.Contains("idle:"), $"Row status '{row.Status}' should not contain 'idle:' prefix");
            Assert.False(row.Status.Contains("running:"), $"Row status '{row.Status}' should not contain 'running:' prefix");

            var matchingStatus = allStatuses.FirstOrDefault(s => s.ToString() == row.Status);
            Assert.Equal(matchingStatus.ToString(), row.Status);
        }
    }

    [Fact]
    public void BuildDataTableUpdates_GeneratesCleanStatusString_WithoutPrefix()
    {
        var jobService = new FilterTestFakeJobService();
        var runningJob = MakeJob("job-running", JobStatus.Running);
        var timeoutJob = MakeJob("job-timeout", JobStatus.Timeout, completedAt: DateTime.UtcNow);
        jobService.Jobs.Add(runningJob);
        jobService.Jobs.Add(timeoutJob);

        var cache = new Dictionary<string, string>();
        var updates = JobsApp.BuildDataTableUpdates(jobService, cache).ToList();

        var statusUpdates = updates
            .Where(u => u.ColumnName == nameof(JobItemRow.Status))
            .ToList();

        Assert.Equal(2, statusUpdates.Count);

        var runningUpdate = statusUpdates.Single(u => u.RowId?.ToString() == "job-running");
        Assert.Equal("Running", runningUpdate.Value as string);

        var timeoutUpdate = statusUpdates.Single(u => u.RowId?.ToString() == "job-timeout");
        Assert.Equal("Timeout", timeoutUpdate.Value as string);
    }

    [Fact]
    public void InMemoryLinqFiltering_ByStatus_MatchesExpectedRows()
    {
        var planService = new FakePlanReaderService();
        var jobs = new List<JobItem>
        {
            MakeJob("job-1", JobStatus.Running),
            MakeJob("job-2", JobStatus.Timeout, completedAt: DateTime.UtcNow),
            MakeJob("job-3", JobStatus.Timeout, completedAt: DateTime.UtcNow),
            MakeJob("job-4", JobStatus.Completed, completedAt: DateTime.UtcNow),
            MakeJob("job-5", JobStatus.Failed, completedAt: DateTime.UtcNow)
        };

        var rows = JobsApp.BuildJobRows(jobs, planService);

        var timeoutRows = rows.Where(r => r.Status == "Timeout").ToList();
        Assert.Equal(2, timeoutRows.Count);
        Assert.All(timeoutRows, r => Assert.Equal("Timeout", r.Status));

        var runningRows = rows.Where(r => r.Status == "Running").ToList();
        var singleRunning = Assert.Single(runningRows);
        Assert.Equal("Running", singleRunning.Status);
        Assert.Equal("job-1", singleRunning.Id);

        var completedRows = rows.Where(r => r.Status == "Completed").ToList();
        var singleCompleted = Assert.Single(completedRows);
        Assert.Equal("Completed", singleCompleted.Status);
        Assert.Equal("job-4", singleCompleted.Id);
    }

    private class FilterTestFakeJobService : IJobService
    {
        public List<JobItem> Jobs { get; } = new();

        public string StartJob(JobArgsBase args, string? inboxFilePath = null) => throw new NotSupportedException();
        public void ForceStartJob(string id) => throw new NotSupportedException();
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) => throw new NotSupportedException();
        public void StopJob(string id) => throw new NotSupportedException();
        public int StopAllJobs() => throw new NotSupportedException();
        public int StopQueuedJobs() => throw new NotSupportedException();
        public void DeleteJob(string id) => throw new NotSupportedException();
        public void ClearCompletedJobs() => throw new NotSupportedException();
        public void ClearFailedJobs() => throw new NotSupportedException();
        public void ClearAllJobs() => throw new NotSupportedException();
        public List<JobItem> GetJobs() => Jobs;
        public List<JobItem> GetJobsForPlan(string planFile) => throw new NotSupportedException();
        public JobItem? GetJob(string id) => Jobs.FirstOrDefault(j => j.Id == id);
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => throw new NotSupportedException();
        public void SetChatSessionId(string id, string chatSessionId) { }
        public bool ReportJobFailure(string id, string message) => throw new NotSupportedException();
        public bool IsInboxFileTracked(string filePath) => false;
        public void Dispose() { }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
        public event Action<JobItem>? JobFinished;
#pragma warning restore CS0067
    }
}
