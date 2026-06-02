using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Jobs;

public interface IJobService : IDisposable
{
    event Action? JobsChanged;
    event Action? JobsStructureChanged;  // Jobs added/removed or status changed
    event Action? JobPropertyChanged;    // Only properties changed (cost, tokens)
    event Action<JobNotification>? NotificationReady;

    string StartJob(JobArgsBase args, string? inboxFilePath = null);
    void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false);
    void StopJob(string id);
    void DeleteJob(string id);
    void ClearCompletedJobs();
    void ClearFailedJobs();
    void ClearAllJobs();
    List<JobItem> GetJobs();
    List<JobItem> GetJobsForPlan(string planFile);
    JobItem? GetJob(string id);
    bool IsInboxFileTracked(string filePath);
}