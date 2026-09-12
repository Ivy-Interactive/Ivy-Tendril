using Ivy.Tendril.Helpers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans;

public class PlanDatabaseSyncService : IDisposable
{
    private readonly IPlanDatabaseService _database;
    private readonly IConfigService _configService;
    private readonly ILogger<PlanDatabaseSyncService> _logger;
    private readonly PlanReaderService _planReader;
    private readonly IPlanWatcherService _watcher;
    private volatile bool _isInitialSyncComplete;
    private volatile bool _isDatabaseAvailable;

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

        _watcher.PlansChanged += OnPlansChanged;
    }

    public bool IsInitialSyncComplete => _isInitialSyncComplete;

    public void Dispose()
    {
        _watcher.PlansChanged -= OnPlansChanged;
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

            foreach (var plan in plans)
            {
                SyncPlanCosts(plan);
                SyncPlanRecommendations(plan);
            }

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

    private void OnPlansChanged(string? changedPlanFolder)
    {
        if (!_isInitialSyncComplete || !_isDatabaseAvailable) return;

        try
        {
            if (changedPlanFolder != null && Directory.Exists(changedPlanFolder))
                SyncSinglePlan(changedPlanFolder);
            else
                SyncAllPlans();

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

        foreach (var plan in plans)
        {
            SyncPlanCosts(plan);
            SyncPlanRecommendations(plan);
        }
    }

    private void SyncPlanCosts(PlanFile plan)
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
                var logFiles = JobLogPaths.LogsForPlanId(_configService.TendrilHome, planId)
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
