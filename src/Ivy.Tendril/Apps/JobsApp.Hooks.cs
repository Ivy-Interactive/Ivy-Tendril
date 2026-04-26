using System.Reactive.Disposables;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private void SetupOutputStreaming(
        IJobService jobService,
        IState<string?> showOutput,
        IState<string?> streamingJobId,
        IState<int> lastProcessedIndex,
        IState<bool> hasStreamContent,
        IStream<string> outputStream)
    {
        UseInterval(() =>
        {
            if (showOutput.Value is not { } activeJobId) return;

            var activeJob = jobService.GetJob(activeJobId);
            if (activeJob is not { Status: JobStatus.Running }) return;

            var startIdx = lastProcessedIndex.Value;

            if (streamingJobId.Value != activeJobId)
            {
                streamingJobId.Set(activeJobId);
                hasStreamContent.Set(false);

                var existingLines = activeJob.OutputLines.ToArray();
                foreach (var line in existingLines)
                {
                    outputStream.Write(line);
                }

                if (existingLines.Length > 0 && !hasStreamContent.Value)
                {
                    hasStreamContent.Set(true);
                }

                lastProcessedIndex.Set(existingLines.Length);
            }
            else
            {
                var currentLines = activeJob.OutputLines.ToArray();
                for (var i = startIdx; i < currentLines.Length; i++)
                {
                    outputStream.Write(currentLines[i]);
                }

                if (currentLines.Length > 0 && !hasStreamContent.Value)
                {
                    hasStreamContent.Set(true);
                }

                lastProcessedIndex.Set(currentLines.Length);
            }
        }, TimeSpan.FromMilliseconds(100));
    }

    private void SetupNotificationHooks(IJobService jobService, IClientProvider client)
    {
        UseEffect(() =>
        {
            void OnNotification(JobNotification notification)
            {
                if (notification.IsSuccess)
                    client.Toast(notification.Message, notification.Title);
                else
                    client.Toast(notification.Message, notification.Title).Destructive();
            }

            jobService.NotificationReady += OnNotification;
            return Disposable.Create(() => jobService.NotificationReady -= OnNotification);
        });
    }

    private void SetupJobChangeHooks(IJobService jobService, IRefreshToken refreshToken)
    {
        UseEffect(() =>
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
        });
    }

    private void SetupAutoRefresh(IJobService jobService, IRefreshToken refreshToken)
    {
        UseInterval(() =>
        {
            var hasActiveOrRecentJobs = jobService.GetJobs().Any(j =>
                j.Status == JobStatus.Running ||
                (j.Status is JobStatus.Stopped or JobStatus.Failed or JobStatus.Timeout or JobStatus.Completed
                 && j.CompletedAt.HasValue
                 && DateTime.UtcNow - j.CompletedAt.Value < TimeSpan.FromSeconds(5)));

            if (hasActiveOrRecentJobs)
                refreshToken.Refresh();
        }, TimeSpan.FromSeconds(5));
    }
}
