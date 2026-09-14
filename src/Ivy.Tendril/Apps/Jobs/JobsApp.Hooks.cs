using System.Reactive.Disposables;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Jobs;

public partial class JobsApp
{
    /// <summary>
    ///     How long a burst of job changes is collapsed over. Matches <c>UseInboxAutoRefresh</c>: short
    ///     enough to feel immediate, long enough that ten jobs exiting together cost one rebuild.
    /// </summary>
    internal static readonly TimeSpan JobRefreshWindow = TimeSpan.FromMilliseconds(400);

    /// <summary>
    ///     Refreshes <paramref name="refreshToken" /> when the job list changes, owning a coalescer on the
    ///     caller's behalf. See the overload below for what is gated and why.
    /// </summary>
    internal static IDisposable JobChangeHookDisposable(IJobService jobService, RefreshToken refreshToken,
        Func<string>? renderedSignature = null)
    {
        var coalescer = new RefreshCoalescer(refreshToken, JobRefreshWindow);
        var hook = JobChangeHookDisposable(jobService, coalescer, renderedSignature);
        return Disposable.Create(() =>
        {
            hook.Dispose();
            coalescer.Dispose();
        });
    }

    /// <summary>
    ///     Requests a refresh through a coalescer the caller owns — for a view that shares one between
    ///     several signals raised by the same underlying change.
    /// </summary>
    /// <param name="renderedSignature">
    ///     The structural signature of the last render, or null for a view that tracks none. Ten jobs
    ///     exiting together raise <c>JobsStructureChanged</c> ten times while the first rebuild already
    ///     shows all ten, so an event whose signature matches what is on screen is dropped before it
    ///     reaches the coalescer (#2571). The view's 5s interval remains the backstop.
    /// </param>
    internal static IDisposable JobChangeHookDisposable(IJobService jobService, RefreshCoalescer coalescer,
        Func<string>? renderedSignature = null)
    {
        void OnJobsChanged()
        {
            if (renderedSignature != null
                && ComputeStructuralSignature(jobService.GetJobs()) == renderedSignature())
                return;

            coalescer.Request();
        }

        jobService.JobsStructureChanged += OnJobsChanged;
        return Disposable.Create(() =>
        {
            jobService.JobsStructureChanged -= OnJobsChanged;
        });
    }

    /// <summary>
    /// Signature over the parts of a job row not already covered by <see cref="BuildDataTableUpdates"/>'s
    /// cell update stream (Timer, Cost, Tokens, AgentOutput, Status, StatusMessage). Status is
    /// included here too because it also drives <see cref="CanRerun"/> and the header's
    /// StackedProgress, not just the badge cell.
    /// </summary>
    internal static string ComputeStructuralSignature(IReadOnlyList<JobItem> jobs) =>
        string.Join("|", jobs.Select(j =>
            $"{j.Id};{j.Status};{j.PlanFile};{j.ReportedPlanId};{j.Type};{j.Project}"));
}

