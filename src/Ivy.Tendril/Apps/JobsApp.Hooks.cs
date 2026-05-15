using System.Reactive.Disposables;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private static IDisposable JobChangeHookDisposable(IJobService jobService, RefreshToken refreshToken)
    {
        void OnJobsChanged()
        {
            refreshToken.Refresh();
        }

        jobService.JobsStructureChanged += OnJobsChanged;
        jobService.JobPropertyChanged += OnJobsChanged;
        return Disposable.Create(() =>
        {
            jobService.JobsStructureChanged -= OnJobsChanged;
            jobService.JobPropertyChanged -= OnJobsChanged;
        });
    }

    private static void AutoRefreshCheck(IJobService jobService, RefreshToken refreshToken)
    {
        var hasActiveOrRecentJobs = jobService.GetJobs().Any(j =>
            j.Status == JobStatus.Running ||
            (j.Status is JobStatus.Stopped or JobStatus.Failed or JobStatus.Timeout or JobStatus.Completed
             && j.CompletedAt.HasValue
             && DateTime.UtcNow - j.CompletedAt.Value < TimeSpan.FromSeconds(5)));

        if (hasActiveOrRecentJobs)
            refreshToken.Refresh();
    }
}

