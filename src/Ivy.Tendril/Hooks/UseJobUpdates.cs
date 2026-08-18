using System.Reactive.Disposables;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Hooks;

public static class UseJobUpdatesExtensions
{
    /// <summary>
    ///     Re-renders a view that shows a single job whenever the fields it displays change on that
    ///     job. Sheets read their job once in <c>Build()</c>, so without this a sheet opened on a
    ///     just-completed job keeps its first snapshot: cost is calculated on a background task
    ///     about 30 seconds after the job finishes
    ///     (<see cref="Services.Jobs.JobCompletionHandler" />), and the sheet would show the empty
    ///     state until it is closed and reopened.
    /// </summary>
    /// <param name="signature">
    ///     Projects the job to the fields the view actually renders. Only a change here triggers a
    ///     re-render — <see cref="IJobService.JobPropertyChanged" /> also fires on every status
    ///     report from every running job, which would otherwise rebuild an open sheet constantly.
    /// </param>
    public static void UseJobUpdates(this IViewContext context, IJobService jobService, string jobId,
        Func<JobItem, string> signature)
    {
        var refreshToken = context.UseRefreshToken();
        var rendered = context.UseRef(() => Snapshot(jobService, jobId, signature));

        void Sync()
        {
            var current = Snapshot(jobService, jobId, signature);
            if (current == rendered.Value) return;
            rendered.Value = current;
            refreshToken.Refresh();
        }

        context.UseEffect(() =>
        {
            jobService.JobsStructureChanged += Sync;
            jobService.JobPropertyChanged += Sync;
            return Disposable.Create(() =>
            {
                jobService.JobsStructureChanged -= Sync;
                jobService.JobPropertyChanged -= Sync;
            });
        });

        // Safety net, mirroring JobsApp: the events above are the primary signal, but a job can also
        // be replaced in the service's map (reload, rerun) without one reaching this subscription.
        context.UseInterval(Sync, TimeSpan.FromSeconds(5));
    }

    private static string Snapshot(IJobService jobService, string jobId, Func<JobItem, string> signature)
    {
        var job = jobService.GetJob(jobId);
        return job is null ? "" : signature(job);
    }
}
