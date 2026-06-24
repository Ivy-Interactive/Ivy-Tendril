using Ivy.Helpers;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Ivy.Tendril.Services.Plans;

public class PlanWatcherService : IPlanWatcherService
{
    // Default self-heal cadence: the top-level FSW fires when a plan folder first appears
    // (still empty), but plan.yaml / Revisions land a moment later and writes *inside* the
    // folder don't re-trigger the watcher. These staggered re-scans surface the now-complete
    // folder within seconds instead of waiting for the 30s poll.
    private static readonly int[] DefaultSelfHealDelaysMs = { 1000, 3000, 8000 };

    private readonly Timer _debounceTimer;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer? _pollTimer;
    private readonly ILogger<PlanWatcherService>? _logger;
    private readonly int[] _selfHealDelaysMs;
    private readonly object _selfHealLock = new();
    private List<System.Threading.Timer> _selfHealTimers = new();
    private string? _pendingPlanFolder;

    public PlanWatcherService(IConfigService config, ILogger<PlanWatcherService>? logger = null,
        int[]? selfHealDelaysMs = null)
    {
        _logger = logger;
        _selfHealDelaysMs = selfHealDelaysMs ?? DefaultSelfHealDelaysMs;
        _debounceTimer = new Timer(500);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) =>
        {
            try
            {
                var folder = _pendingPlanFolder;
                _pendingPlanFolder = null;
                PlansChanged?.Invoke(folder);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to invoke PlansChanged event");
                // Swallow to prevent unhandled exceptions on the timer's thread-pool
                // thread from terminating the process.
            }
        };

        var planFolder = config.PlanFolder;
        if (!Directory.Exists(planFolder))
            return;

        // Only watch the top-level Plans directory (no subdirectories) to detect
        // new/deleted plan folders. This avoids the massive file event storm from
        // worktree operations (git checkout, npm install) that was overflowing
        // the FSW buffer and destabilizing explorer.exe.
        _watcher = new FileSystemWatcher(planFolder)
        {
            NotifyFilter = NotifyFilters.DirectoryName,
            InternalBufferSize = 65536,
            EnableRaisingEvents = true
        };

        // Created/Renamed/Error may indicate a new plan folder whose content (plan.yaml,
        // first revision) is still being written; schedule self-heal re-scans so it surfaces
        // promptly. Deleted needs no self-heal — there is no late-arriving content to wait for.
        _watcher.Created += (_, _) =>
        {
            ScheduleDebounce(null);
            ScheduleSelfHeal();
        };
        _watcher.Deleted += (_, _) => ScheduleDebounce(null);
        _watcher.Renamed += (_, _) =>
        {
            ScheduleDebounce(null);
            ScheduleSelfHeal();
        };
        _watcher.Error += (_, e) =>
        {
            CrashLog.Write($"[{DateTime.UtcNow:O}] PlanWatcher FSW error: {e.GetException()}");
            ScheduleDebounce(null);
            ScheduleSelfHeal();
        };

        // Poll as a safety net for external edits to plan.yaml or metadata files
        // that aren't covered by explicit NotifyChanged() calls from JobService.
        _pollTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                ScheduleDebounce(null);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to schedule debounce during poll");
                // Best-effort polling
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public event Action<string?>? PlansChanged;

    public void NotifyChanged(string? changedPlanFolder = null)
    {
        ScheduleDebounce(changedPlanFolder);
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _watcher?.Dispose();
        _debounceTimer.Dispose();

        lock (_selfHealLock)
        {
            foreach (var timer in _selfHealTimers)
                timer.Dispose();
            _selfHealTimers = new List<System.Threading.Timer>();
        }
    }

    /// <summary>
    ///     Schedules a short burst of full re-scans after a top-level folder event. A brand-new
    ///     plan folder fires the FSW while still empty; its plan.yaml / first revision land a
    ///     moment later, and those writes (inside the folder) don't re-trigger the watcher. These
    ///     staggered re-scans pick up the completed folder within seconds rather than waiting for
    ///     the 30s poll.
    ///     <para>
    ///     Trade-off: a single folder event now yields up to N+1 full re-scans (the debounce plus
    ///     one per delay) instead of one. Plan-folder events are infrequent and each new event
    ///     replaces the prior burst, so the extra I/O is bounded and acceptable.
    ///     </para>
    /// </summary>
    private void ScheduleSelfHeal()
    {
        var newTimers = new List<System.Threading.Timer>(_selfHealDelaysMs.Length);
        foreach (var delayMs in _selfHealDelaysMs)
        {
            // Fire an independent full rescan at each delay rather than routing through the
            // debounce (which would coalesce the staggered timers into a single fire). Each
            // rescan is a fresh chance to pick up content that landed after the last one.
            var timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    PlansChanged?.Invoke(null);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to invoke PlansChanged during self-heal");
                    // Swallow to prevent unhandled exceptions on the timer's thread-pool
                    // thread from terminating the process.
                }
            }, null, delayMs, Timeout.Infinite);
            newTimers.Add(timer);
        }

        lock (_selfHealLock)
        {
            foreach (var timer in _selfHealTimers)
                timer.Dispose();
            _selfHealTimers = newTimers;
        }
    }

    private void ScheduleDebounce(string? planFolder)
    {
        // If we already have a pending folder and a different one arrives, escalate to full rescan
        if (_pendingPlanFolder != null && planFolder != null
                                       && !string.Equals(_pendingPlanFolder, planFolder,
                                           StringComparison.OrdinalIgnoreCase))
            _pendingPlanFolder = null; // null = full rescan
        else if (_pendingPlanFolder == null && planFolder != null && !_debounceTimer.Enabled)
            _pendingPlanFolder = planFolder;
        // If planFolder is null (full rescan requested), override any specific folder
        else if (planFolder == null) _pendingPlanFolder = null;

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }
}
