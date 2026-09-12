using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Ivy.Tendril.AppShell;

/// <summary>
///     Collapses a burst of job notifications into one toast. A wave of jobs exiting together raised a
///     toast each, and every toast is a client round trip plus a card the user has to read past — at the
///     moment the workspace is already busy syncing those same exits (#2571). Failures keep their own
///     toast: which plan failed, and why, is the part worth reading.
/// </summary>
internal sealed class NotificationBurstSummarizer : IDisposable
{
    /// <summary>
    ///     How long notifications are collected before they are shown. The window opens at the first
    ///     notification, so a shell with nothing happening pays nothing.
    /// </summary>
    internal static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(300);

    /// <summary>Up to this many notifications in one window are shown exactly as they arrived.</summary>
    internal const int MaxIndividual = 3;

    internal const string SummaryTitle = "Jobs Finished";

    private readonly Subject<JobNotification> _notifications = new();
    private readonly IDisposable _subscription;

    internal NotificationBurstSummarizer(
        Action<JobNotification> show, TimeSpan? window = null, IScheduler? scheduler = null)
    {
        var burst = window ?? DefaultWindow;

        _subscription = _notifications
            // Closed by the first notification in the buffer plus the window, rather than
            // Buffer(TimeSpan), which would tick a timer every window for the life of the shell.
            .Buffer(() => _notifications.Delay(burst, scheduler ?? Scheduler.Default).Take(1))
            .Subscribe(batch =>
            {
                // Buffer hands back an IList; Summarize is written against the read-only shape the
                // tests call it with.
                foreach (var notification in Summarize([.. batch]))
                    show(notification);
            });
    }

    internal void Add(JobNotification notification) => _notifications.OnNext(notification);

    /// <summary>
    ///     What one window's worth of notifications should be shown as: everything, when there are few
    ///     enough to read, else one summary of the wave followed by each failure in it.
    /// </summary>
    internal static IReadOnlyList<JobNotification> Summarize(IReadOnlyList<JobNotification> batch)
    {
        if (batch.Count <= MaxIndividual) return batch;

        var failures = batch.Where(n => !n.IsSuccess).ToList();
        var finished = batch.Count - failures.Count;

        // A burst that is nothing but failures has nothing to collapse: the failures are all detail,
        // and a summary on top of them would only be one more card.
        if (finished == 0) return failures;

        var jobs = finished == 1 ? "job" : "jobs";
        var message = failures.Count == 0
            ? $"{finished} {jobs} finished"
            : $"{finished} {jobs} finished, {failures.Count} failed";

        return [new JobNotification(SummaryTitle, message, true), .. failures];
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _notifications.OnCompleted();
        _notifications.Dispose();
    }
}
