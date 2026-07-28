using System.Reactive.Disposables;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Jobs;

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

