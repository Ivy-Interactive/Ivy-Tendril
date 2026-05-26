using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

public record PlanCounts(int Drafts, int ActiveJobs, int Reviews, int Icebox, int Recommendations, int TotalPlans);

public class PlanCountsService : IPlanCountsService
{
    private readonly IJobService _jobService;
    private readonly IPlanReaderService _planReaderService;
    private readonly IPlanWatcherService _planWatcher;

    public PlanCountsService(IPlanReaderService planReaderService, IJobService jobService,
        IPlanWatcherService planWatcher)
    {
        _planReaderService = planReaderService;
        _jobService = jobService;
        _planWatcher = planWatcher;
        Current = ComputeCounts();
        _planWatcher.PlansChanged += OnPlansSourceChanged;
        _jobService.JobsStructureChanged += OnSourceChanged;
        _planReaderService.CountsInvalidated += OnCountsInvalidated;
    }

    public event Action? CountsChanged;

    public PlanCounts Current { get; private set; }

    public void Dispose()
    {
        _planWatcher.PlansChanged -= OnPlansSourceChanged;
        _jobService.JobsStructureChanged -= OnSourceChanged;
        _planReaderService.CountsInvalidated -= OnCountsInvalidated;
    }

    private void OnCountsInvalidated()
    {
        try
        {
            Refresh();
        }
        catch
        {
            // Swallow to prevent unhandled exceptions from terminating the process.
        }
    }

    private void OnPlansSourceChanged(string? _)
    {
        OnSourceChanged();
    }

    private void OnSourceChanged()
    {
        try
        {
            _planReaderService.InvalidateCaches();
            Refresh();
        }
        catch
        {
            // Swallow to prevent unhandled exceptions on timer/thread-pool threads
            // from terminating the process.
        }
    }

    public void RefreshNow()
    {
        _planReaderService.InvalidateCaches();
        Refresh();
    }

    private void Refresh()
    {
        var updated = ComputeCounts();
        if (updated != Current)
        {
            Current = updated;
            CountsChanged?.Invoke();
        }
    }

    private PlanCounts ComputeCounts()
    {
        var snapshot = _planReaderService.ComputePlanCounts();
        var jobs = _jobService.GetJobs();

        var activeJobs = jobs
            .Where(j => j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked)
            .ToList();

        var activePlanFolders = activeJobs
            .Select(j => j.TypedArgs?.PlanFolder)
            .Where(f => f != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var activeCreatePlanIds = activeJobs
            .Where(j => j.TypedArgs is CreatePlanArgs)
            .Select(j => j.ReportedPlanId ?? j.AllocatedPlanId)
            .Where(id => id != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var prematureReviews = 0;
        var prematureDrafts = 0;

        if (activePlanFolders.Count > 0 || activeCreatePlanIds.Count > 0)
        {
            foreach (var p in _planReaderService.GetPlans())
            {
                var hasActiveJob = activePlanFolders.Contains(p.FolderPath) ||
                    activeCreatePlanIds.Any(id => p.FolderName.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase));
                if (!hasActiveJob) continue;

                if (p.Status is PlanStatus.ReadyForReview or PlanStatus.Failed)
                    prematureReviews++;
                else if (p.Status is PlanStatus.Draft or PlanStatus.Blocked)
                    prematureDrafts++;
            }
        }

        return new PlanCounts(
            Math.Max(0, snapshot.Drafts - prematureDrafts),
            jobs.Count(j => j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Blocked),
            Math.Max(0, snapshot.ReadyForReview + snapshot.Failed - prematureReviews),
            snapshot.Icebox,
            snapshot.PendingRecommendations,
            snapshot.TotalPlans
        );
    }
}
