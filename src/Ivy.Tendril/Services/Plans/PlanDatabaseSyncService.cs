using Ivy.Tendril.Helpers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans;

public class PlanDatabaseSyncService : IDisposable
{
    /// <summary>How long <see cref="Dispose" /> waits for the worker to finish its current sync.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly IPlanDatabaseService _database;
    private readonly IConfigService _configService;
    private readonly ILogger<PlanDatabaseSyncService> _logger;
    private readonly PlanReaderService _planReader;
    private readonly IPlanWatcherService _watcher;
    private volatile bool _isInitialSyncComplete;
    private volatile bool _isDatabaseAvailable;

    // The request stream is a wake-up signal, not a work queue: what to sync is decided from the
    // coalesced state below, so a burst of N events cannot turn into N rescans.
    private readonly Channel<byte> _wakeUps =
        Channel.CreateUnbounded<byte>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _pendingFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _worker;
    private int _fullSyncPending;
    private int _syncInFlight;
    private int _syncCount;
    private int _singlePlanSyncCount;
    private int _lastSyncThreadId;

    public PlanDatabaseSyncService(
        PlanReaderService planReader,
        IPlanDatabaseService database,
        IPlanWatcherService watcher,
        IConfigService configService,
        ILogger<PlanDatabaseSyncService> logger)
    {
        _planReader = planReader;
        _database = database;
        _watcher = watcher;
        _configService = configService;
        _logger = logger;

        // Long-running rather than a pooled task: the loop is parked on the channel for the life of
        // the process, and a full rescan is minutes of I/O on a large store — neither belongs on a
        // thread pool thread.
        _worker = Task.Factory.StartNew(RunWorker, CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

        _watcher.PlansChanged += OnPlansChanged;
    }

    public bool IsInitialSyncComplete => _isInitialSyncComplete;

    /// <summary>Full rescans performed by the worker. Test seam for the coalescing behaviour.</summary>
    internal int SyncCount => Volatile.Read(ref _syncCount);

    /// <summary>Single-folder syncs performed by the worker. Test seam.</summary>
    internal int SinglePlanSyncCount => Volatile.Read(ref _singlePlanSyncCount);

    /// <summary>Managed thread id the last sync ran on, or 0. Test seam.</summary>
    internal int LastSyncThreadId => Volatile.Read(ref _lastSyncThreadId);

    public void Dispose()
    {
        _watcher.PlansChanged -= OnPlansChanged;
        _wakeUps.Writer.TryComplete();

        // Bounded: shutdown must not hang behind a rescan that is mid-flight on a large store.
        try
        {
            if (!_worker.Wait(ShutdownTimeout))
                _logger.LogWarning("Database sync worker did not stop within {Timeout}s", ShutdownTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database sync worker faulted during shutdown");
        }
    }

    /// <summary>
    ///     Completes once the worker has no sync in flight and nothing queued. Test seam — production
    ///     code never waits on the sync.
    /// </summary>
    internal async Task DrainAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            if (Volatile.Read(ref _fullSyncPending) == 0
                && _pendingFolders.IsEmpty
                && Volatile.Read(ref _syncInFlight) == 0)
                return;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Database sync did not drain within the timeout.");

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    public void PerformInitialSync()
    {
        try
        {
            _logger.LogInformation("Starting initial database sync...");
            var stopwatch = Stopwatch.StartNew();

            // Skip completed/skipped plans already in the DB — they never change,
            // so re-parsing them from disk on every startup is wasted I/O.
            var terminalIds = _database.GetTerminalPlanIds();

            // Read directly from file system to avoid circular dependency.
            // Force overwrite to ensure filesystem is source of truth on startup,
            // even if the DB has newer timestamps from prior state transitions.
            var plans = _planReader.GetPlansFromFileSystem(skipIds: terminalIds);
            _logger.LogInformation("Filesystem returned {Count} plans for sync (skipped {Skipped} terminal)",
                plans.Count, terminalIds.Count);
            _database.BulkUpsertPlans(plans, true);
            RecoverStuckPlansInDatabase(plans);
            SyncPlanDetails(plans);

            // Purging old rows keeps the database small; the job artifacts under <TendrilHome>/Jobs/ are
            // kept so a purged job can still be inspected and attached to a bug report.
            _database.PurgeOldJobs();
            _database.SetLastSyncTime(DateTime.UtcNow);
            _isInitialSyncComplete = true;

            // Enable database-backed reads in PlanReaderService
            _planReader.EnableDatabaseReads(_database);
            _isDatabaseAvailable = true;

            stopwatch.Stop();
            _logger.LogInformation("Initial sync complete. Synced {Count} plans in {Ms}ms",
                plans.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial database sync failed");
            _isInitialSyncComplete = true;
            // Don't enable database reads — fall back to filesystem
        }
    }

    /// <summary>
    ///     Queues a sync and returns. Never syncs on the calling thread: PlansChanged is raised from
    ///     the watcher's timers and from every plan mutation, and a full rescan on a large store is
    ///     seconds of file and SQLite work — running it inline is what froze the workspace on a burst
    ///     of plan closes (#2571). Internal rather than private so tests can raise it directly.
    /// </summary>
    internal void OnPlansChanged(string? changedPlanFolder)
    {
        if (!_isInitialSyncComplete || !_isDatabaseAvailable) return;

        RecordPending(changedPlanFolder);

        // The state above is published before the wake-up, so the worker (and DrainAsync) can never
        // see a wake-up without the work it stands for.
        _wakeUps.Writer.TryWrite(0);
    }

    /// <summary>
    ///     Folds one request into the coalesced state without waking the worker. Split out of
    ///     <see cref="OnPlansChanged" /> so a test can queue several requests and then inspect what one
    ///     pass takes, which is racy to observe once the worker is awake.
    /// </summary>
    internal void RecordPending(string? changedPlanFolder)
    {
        if (changedPlanFolder != null && Directory.Exists(changedPlanFolder))
            _pendingFolders.TryAdd(changedPlanFolder, 0);
        else
            // A full rescan supersedes every pending single-folder sync, and a second request while
            // one is already pending collapses into it.
            Interlocked.Exchange(ref _fullSyncPending, 1);
    }

    /// <summary>
    ///     The coalesced work for one pass. A pending full rescan wins and discards the queued folders,
    ///     which it re-reads anyway.
    /// </summary>
    internal (bool FullRescan, List<string> Folders) TakeWork()
    {
        if (Interlocked.Exchange(ref _fullSyncPending, 0) == 1)
        {
            _pendingFolders.Clear();
            return (true, []);
        }

        var folders = new List<string>();
        foreach (var folder in _pendingFolders.Keys)
            if (_pendingFolders.TryRemove(folder, out _))
                folders.Add(folder);

        return (false, folders);
    }

    private async Task RunWorker()
    {
        try
        {
            while (await _wakeUps.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // Drain every queued wake-up before working: they are signals, and the work they
                // stand for has already been folded into _fullSyncPending / _pendingFolders.
                while (_wakeUps.Reader.TryRead(out _)) { }

                Interlocked.Exchange(ref _syncInFlight, 1);
                try
                {
                    DrainOnce();
                }
                finally
                {
                    Interlocked.Exchange(ref _syncInFlight, 0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database sync worker stopped unexpectedly");
        }
    }

    /// <summary>
    ///     One pass of the coalesced work. A full rescan wins over any pending single-folder syncs; a
    ///     request that arrives while this pass runs is picked up by the next one, so a burst costs at
    ///     most the rescan in flight plus one follow-up.
    /// </summary>
    private void DrainOnce()
    {
        Volatile.Write(ref _lastSyncThreadId, Environment.CurrentManagedThreadId);

        try
        {
            var (fullRescan, folders) = TakeWork();
            if (fullRescan)
            {
                Interlocked.Increment(ref _syncCount);
                SyncAllPlans();
                _database.SetLastSyncTime(DateTime.UtcNow);
                return;
            }

            if (folders.Count == 0) return;

            foreach (var folder in folders)
            {
                Interlocked.Increment(ref _singlePlanSyncCount);
                SyncSinglePlan(folder);
            }

            _database.SetLastSyncTime(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incremental sync failed");
        }
    }

    private void RecoverStuckPlansInDatabase(List<PlanFile> plans)
    {
        var stuckStates = new HashSet<PlanStatus>
            { PlanStatus.Creating, PlanStatus.Executing, PlanStatus.Updating, PlanStatus.Blocked };

        var recovered = 0;
        foreach (var plan in plans)
        {
            if (!stuckStates.Contains(plan.Status)) continue;

            var newState = plan.Status == PlanStatus.Executing ? PlanStatus.Failed : PlanStatus.Draft;
            _database.UpdatePlanState(plan.Id, newState);
            recovered++;
        }

        if (recovered > 0)
            _logger.LogInformation("Recovered {Count} stuck plans in database.", recovered);
    }

    private void SyncSinglePlan(string planFolder)
    {
        var plan = _planReader.ParseSinglePlanFolder(planFolder);
        if (plan != null)
        {
            _database.UpsertPlan(plan);
            SyncPlanCosts(plan);
            SyncPlanRecommendations(plan);
        }
    }

    private void SyncAllPlans()
    {
        var plans = _planReader.GetPlansFromFileSystem();
        _database.BulkUpsertPlans(plans);
        SyncPlanDetails(plans);
    }

    /// <summary>
    ///     Syncs every plan's costs and recommendations in one pass: the Jobs directory is enumerated
    ///     once for all plans rather than globbed per plan, and the whole loop runs inside one write
    ///     lock and one transaction rather than two lock acquisitions per plan. Both are what keep a
    ///     rescan from starving the UI's plan-list reads (#2571).
    /// </summary>
    internal void SyncPlanDetails(List<PlanFile> plans)
    {
        var logsByPlanId = JobLogPaths.LogsByPlanId(_configService.TendrilHome);

        using var batch = _database.BeginBatch();
        foreach (var plan in plans)
        {
            SyncPlanCosts(plan, logsByPlanId);
            SyncPlanRecommendations(plan);
        }
    }

    /// <param name="logsByPlanId">
    ///     Job logs grouped by plan id, built once per rescan. Null falls back to a per-plan glob,
    ///     which is what a single-folder sync wants.
    /// </param>
    private void SyncPlanCosts(PlanFile plan, Dictionary<string, string[]>? logsByPlanId = null)
    {
        var costsPath = Path.Combine(plan.FolderPath, "costs.csv");
        if (!File.Exists(costsPath)) return;

        try
        {
            var lines = FileHelper.ReadAllLines(costsPath);
            var planId = JobLogPaths.PlanIdFromFolderName(Path.GetFileName(plan.FolderPath));

            // Correlate each costs.csv row with the job log of the same promptware, oldest job first.
            // Job logs are named "{jobId}-{planId}-{promptware}.md".
            var logsByPromptware =
                new Dictionary<string, Queue<(string Path, int Num)>>(StringComparer.OrdinalIgnoreCase);
            if (planId != null)
            {
                var planLogs = logsByPlanId != null
                    ? logsByPlanId.TryGetValue(planId, out var grouped) ? grouped : []
                    : JobLogPaths.LogsForPlanId(_configService.TendrilHome, planId);

                var logFiles = planLogs
                    .Select(f =>
                    {
                        var parts = Path.GetFileNameWithoutExtension(f).Split('-');
                        int.TryParse(parts[0], out var num);
                        return (Promptware: parts[^1], Path: f, Num: num);
                    })
                    .OrderBy(l => l.Num)
                    .ToList();

                foreach (var log in logFiles)
                {
                    if (!logsByPromptware.ContainsKey(log.Promptware))
                        logsByPromptware[log.Promptware] = new Queue<(string, int)>();
                    logsByPromptware[log.Promptware].Enqueue((log.Path, log.Num));
                }
            }

            var costs = new List<CostEntry>();
            var needsFileUpgrade = false;
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length < 3) continue;

                var promptware = parts[0].Trim();
                if (!int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture,
                        out var tokens)) continue;

                // An unpriceable cost does not discard the row: the tokens were still spent, and a
                // null Cost is what every aggregate needs to skip the plan rather than average a zero
                // in. Applies to the empty field the v2 writer produces for a subscription run and to
                // a field that is simply corrupt.
                var cost = decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var parsedCost)
                    ? parsedCost
                    : (decimal?)null;

                // Fourth column since costs.csv v2; files written before it have three.
                var model = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3].Trim() : null;

                // Fifth column since costs.csv v3; files written before it have three or four.
                var costSource = parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4].Trim() : null;

                if (costSource == null)
                {
                    costSource = _database.ResolveCostSource(
                        plan.Id,
                        promptware,
                        plan.FolderPath,
                        Path.GetFileName(plan.FolderPath));

                    if (costSource != null || parts.Length < 5)
                        needsFileUpgrade = true;
                }

                // Sixth column since costs.csv v4; files written before it have three to five.
                var agent = parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]) ? parts[5].Trim() : null;

                if (agent == null)
                {
                    agent = _database.ResolveAgent(
                        plan.Id,
                        promptware,
                        plan.FolderPath,
                        Path.GetFileName(plan.FolderPath));

                    if (agent != null || parts.Length < 6)
                        needsFileUpgrade = true;
                }

                DateTime? timestamp = null;
                if (logsByPromptware.TryGetValue(promptware, out var queue) && queue.Count > 0)
                {
                    var logEntry = queue.Dequeue();
                    timestamp = ExtractCompletedTimestamp(logEntry.Path);
                }

                costs.Add(new CostEntry(promptware, tokens, cost, timestamp, model, costSource, agent));
            }

            _database.UpsertCosts(plan.Id, costs);

            if (needsFileUpgrade && costs.Count > 0)
            {
                RewriteCostsCsv(costsPath, costs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync costs for plan {PlanId}", plan.Id);
        }
    }

    private void RewriteCostsCsv(string costsPath, List<CostEntry> costs)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("Promptware,Tokens,Cost,Model,CostSource,Agent\n");
            foreach (var cost in costs)
            {
                var costField = cost.Cost?.ToString("F4", CultureInfo.InvariantCulture) ?? "";
                sb.Append($"{cost.Promptware},{cost.Tokens},{costField},{cost.Model ?? ""},{cost.CostSource ?? ""},{cost.Agent ?? ""}\n");
            }
            FileHelper.WriteAllText(costsPath, sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to upgrade costs.csv at {Path}", costsPath);
        }
    }

    private void SyncPlanRecommendations(PlanFile plan)
    {
        try
        {
            var planYamlPath = Path.Combine(plan.FolderPath, "plan.yaml");
            if (!File.Exists(planYamlPath)) return;

            var yaml = FileHelper.ReadAllText(planYamlPath);
            var planYaml = YamlHelper.Deserializer.Deserialize<PlanYaml>(yaml);
            var items = planYaml?.Recommendations;

            if (items != null && items.Count > 0)
            {
                _database.UpsertRecommendations(plan.Id, plan.FolderName, items,
                    plan.Project, plan.Title, plan.Updated, plan.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync recommendations for plan {PlanId}", plan.Id);
        }
    }

    private static DateTime? ExtractCompletedTimestamp(string logFilePath)
    {
        return FileHelper.ExtractCompletedTimestamp(logFilePath);
    }
}
