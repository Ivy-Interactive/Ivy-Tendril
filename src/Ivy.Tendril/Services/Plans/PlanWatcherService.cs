using Ivy.Helpers;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Ivy.Tendril.Services.Plans;

public class PlanWatcherService : IPlanWatcherService
{
    // Default self-heal cadence: the top-level FSW fires when a plan folder first appears
    // (still empty), but plan.yaml / Revisions land a moment later and writes *inside* the
    // folder don't re-trigger the watcher. These re-scans surface the now-complete folder
    // within seconds instead of waiting for the 30s poll. Two delays suffice because the
    // database sync coalesces and reruns after an in-flight pass (#2571), so a rescan that
    // races the last write is followed by another without a third timer.
    private static readonly int[] DefaultSelfHealDelaysMs = { 1000, 4000 };

    private readonly Timer _debounceTimer;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer? _pollTimer;
    private readonly ILogger<PlanWatcherService>? _logger;
    private readonly int[] _selfHealDelaysMs;
    private readonly object _selfHealLock = new();

    /// <summary>
    ///     Guards <see cref="_pendingPlanFolder" /> and the debounce timer's Stop/Start pair. Every
    ///     caller is a timer or an FSW callback on an arbitrary thread pool thread, and a burst of plan
    ///     mutations has them arriving together: unsynchronized, two concurrent calls could lose the
    ///     escalation to a full rescan (each seeing a null pending folder) or restart the timer between
    ///     the other's Stop and Start and drop the pending fire entirely.
    /// </summary>
    private readonly object _debounceLock = new();

    private List<System.Threading.Timer> _selfHealTimers = new();
    private string? _pendingPlanFolder;
    private bool _disposed;

    public PlanWatcherService(IConfigService config, ILogger<PlanWatcherService>? logger = null,
        int[]? selfHealDelaysMs = null)
    {
        _logger = logger;
        _selfHealDelaysMs = selfHealDelaysMs ?? DefaultSelfHealDelaysMs;
        _debounceTimer = new Timer(500);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) =>
        {
            string? folder;
            lock (_debounceLock)
            {
                folder = _pendingPlanFolder;
                _pendingPlanFolder = null;
            }

            // Raised outside the lock: subscribers do real work (the database sync queues, views
            // refresh), and holding the lock across them would stall every NotifyChanged behind it.
            RaisePlansChanged(folder);
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

        // Under the same lock as ScheduleDebounce: an FSW callback already queued on a thread pool
        // thread would otherwise reach Start() on the disposed timer and throw there.
        lock (_debounceLock)
        {
            _disposed = true;
            _debounceTimer.Dispose();
        }

        lock (_selfHealLock)
        {
            foreach (var timer in _selfHealTimers)
                timer.Dispose();
            _selfHealTimers = new List<System.Threading.Timer>();
        }
    }

    /// <summary>
    ///     Schedules a couple of full re-scans after a top-level folder event. A brand-new plan
    ///     folder fires the FSW while still empty; its plan.yaml / first revision land a moment
    ///     later, and those writes (inside the folder) don't re-trigger the watcher. These re-scans
    ///     pick up the completed folder within seconds rather than waiting for the 30s poll.
    ///     <para>
    ///     Each delay goes through <see cref="ScheduleDebounce" /> rather than raising directly, so
    ///     the whole burst is subject to the same coalescing as everything else: a folder event
    ///     costs one fire per delay at most, and timers that land together cost one between them.
    ///     Two delays are enough because <c>PlanDatabaseSyncService</c> reruns once after the pass
    ///     in flight, which already covers content that landed during a rescan (#2571).
    ///     </para>
    /// </summary>
    private void ScheduleSelfHeal()
    {
        var newTimers = new List<System.Threading.Timer>(_selfHealDelaysMs.Length);
        foreach (var delayMs in _selfHealDelaysMs)
        {
            var timer = new System.Threading.Timer(_ => ScheduleDebounce(null),
                null, delayMs, Timeout.Infinite);
            newTimers.Add(timer);
        }

        lock (_selfHealLock)
        {
            foreach (var timer in _selfHealTimers)
                timer.Dispose();
            _selfHealTimers = newTimers;
        }
    }

    /// <summary>
    ///     Invokes each PlansChanged subscriber in isolation. A multicast Invoke stops at the
    ///     first throwing handler (e.g. one left behind by a disposed view), which would silently
    ///     starve every subscriber added after it — typically the most recently opened tabs.
    ///     Catching per handler also keeps unhandled exceptions off the timers' thread-pool
    ///     threads, which would otherwise terminate the process.
    /// </summary>
    private void RaisePlansChanged(string? planFolder)
    {
        var handlers = PlansChanged;
        if (handlers == null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action<string?>>())
            try
            {
                handler(planFolder);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "PlansChanged subscriber threw");
            }
    }

    private void ScheduleDebounce(string? planFolder)
    {
        lock (_debounceLock)
        {
            if (_disposed) return;

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
}
