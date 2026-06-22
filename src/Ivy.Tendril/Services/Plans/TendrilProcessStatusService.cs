using System.Reactive.Subjects;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans;

public class TendrilProcessStatusService : ITendrilProcessStatusService
{
    private readonly BehaviorSubject<TendrilProcessStatus> _subject;
    private readonly IPlanReaderService _planReaderService;
    private readonly IJobService _jobService;
    private readonly IPlanWatcherService _planWatcher;
    private readonly IConfigService _config;
    private readonly ILogger<TendrilProcessStatusService> _logger;
    private readonly FileSystemWatcher? _trashWatcher;
    private readonly System.Timers.Timer _debounceTimer;

    public TendrilProcessStatusService(
        IPlanReaderService planReaderService,
        IJobService jobService,
        IPlanWatcherService planWatcher,
        IConfigService config,
        ILogger<TendrilProcessStatusService> logger)
    {
        _planReaderService = planReaderService;
        _jobService = jobService;
        _planWatcher = planWatcher;
        _config = config;
        _logger = logger;

        _subject = new BehaviorSubject<TendrilProcessStatus>(Compute());

        _debounceTimer = new System.Timers.Timer(200) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => SafeRefresh();

        _planWatcher.PlansChanged += OnPlansChanged;
        _jobService.JobsStructureChanged += OnSourceChanged;
        _planReaderService.CountsInvalidated += OnCountsInvalidated;

        if (!string.IsNullOrEmpty(config.TendrilHome))
        {
            var trashDir = Path.Combine(config.TendrilHome, "Trash");
            if (Directory.Exists(trashDir))
            {
                _trashWatcher = new FileSystemWatcher(trashDir, "*.md")
                {
                    NotifyFilter = NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                _trashWatcher.Created += (_, _) => ScheduleRefresh();
                _trashWatcher.Deleted += (_, _) => ScheduleRefresh();
            }
        }
    }

    public IObservable<TendrilProcessStatus> Status => _subject;
    public TendrilProcessStatus Current => _subject.Value;

    public void RefreshNow()
    {
        _planReaderService.InvalidateCaches();
        SafeRefresh();
    }

    private void OnPlansChanged(string? _) => OnSourceChanged();

    private void OnCountsInvalidated() => ScheduleRefresh();

    private void OnSourceChanged()
    {
        _planReaderService.InvalidateCaches();
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void SafeRefresh()
    {
        try
        {
            var updated = Compute();
            if (updated != _subject.Value)
                _subject.OnNext(updated);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TendrilProcessStatusService.Compute failed");
        }
    }

    private TendrilProcessStatus Compute()
    {
        var snapshot = _planReaderService.ComputePlanCounts();
        var jobs = _jobService.GetJobs();

        var activeJobs = jobs
            .Where(j => j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked)
            .ToList();

        var creatingCount = activeJobs.Count(j => j.Type == Constants.JobTypes.CreatePlan);
        var updatingCount = activeJobs.Count(j =>
            j.Type is Constants.JobTypes.UpdatePlan or Constants.JobTypes.ExpandPlan or Constants.JobTypes.SplitPlan);
        var executingCount = activeJobs.Count(j => j.Type == Constants.JobTypes.ExecutePlan);
        var retryingCount = activeJobs.Count(j => j.Type == Constants.JobTypes.RetryPlan);
        var creatingPrCount = activeJobs.Count(j => j.Type == Constants.JobTypes.CreatePr);

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
                    activeCreatePlanIds.Any(id =>
                        p.FolderName.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase));
                if (!hasActiveJob) continue;

                if (p.Status is PlanStatus.Review or PlanStatus.Failed)
                    prematureReviews++;
                else if (p.Status is PlanStatus.Draft or PlanStatus.Blocked)
                    prematureDrafts++;
            }
        }

        var trashCount = 0;
        if (!string.IsNullOrEmpty(_config.TendrilHome))
        {
            var trashDir = Path.Combine(_config.TendrilHome, "Trash");
            if (Directory.Exists(trashDir))
                trashCount = Directory.GetFiles(trashDir, "*.md").Length;
        }

        return new TendrilProcessStatus
        {
            DraftCount = Math.Max(0, snapshot.Drafts - prematureDrafts),
            ReviewCount = Math.Max(0, snapshot.Review + snapshot.Failed - prematureReviews),
            IceboxCount = snapshot.Icebox,
            JobCount = activeJobs.Count,
            TrashCount = trashCount,
            CreatingPlansCount = creatingCount,
            UpdatingPlansCount = updatingCount,
            ExecutingPlansCount = executingCount,
            RetryingPlansCount = retryingCount,
            CreatingPrCount = creatingPrCount,
            RecommendationsCount = snapshot.PendingRecommendations
        };
    }

    public void Dispose()
    {
        _planWatcher.PlansChanged -= OnPlansChanged;
        _jobService.JobsStructureChanged -= OnSourceChanged;
        _planReaderService.CountsInvalidated -= OnCountsInvalidated;
        _trashWatcher?.Dispose();
        _debounceTimer.Dispose();
        _subject.Dispose();
    }
}
