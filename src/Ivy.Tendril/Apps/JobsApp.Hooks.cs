using System.Reactive.Disposables;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private static void StreamOutputLines(
        IJobService jobService,
        IState<string?> activeOutputJobId,
        IState<string?> streamingJobId,
        IState<int> lastProcessedIndex,
        IState<bool> hasStreamContent,
        IWriteStream<string> outputStream)
    {
        if (activeOutputJobId.Value is not { } activeJobId) return;

        var activeJob = jobService.GetJob(activeJobId);
        if (activeJob is not { Status: JobStatus.Running }) return;

        if (streamingJobId.Value != activeJobId)
        {
            streamingJobId.Set(activeJobId);
            hasStreamContent.Set(false);
            // Skip existing lines — OutputSheet provides them via JsonStream
            lastProcessedIndex.Set(activeJob.OutputLines.Count);
        }
        else
        {
            var normalizer = activeJob.OutputNormalizer
                ??= OutputNormalizerFactory.Create(activeJob.Provider);

            var startIdx = lastProcessedIndex.Value;
            var currentLines = activeJob.OutputLines.ToArray();
            for (var i = startIdx; i < currentLines.Length; i++)
            {
                foreach (var normalized in normalizer.Normalize(currentLines[i]))
                    outputStream.Write(normalized);
            }

            if (currentLines.Length > startIdx && !hasStreamContent.Value)
            {
                hasStreamContent.Set(true);
            }

            lastProcessedIndex.Set(currentLines.Length);
        }
    }

    private static IDisposable NotificationHookDisposable(IJobService jobService, IClientProvider client)
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
    }

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

