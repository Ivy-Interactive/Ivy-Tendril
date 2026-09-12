using System.Diagnostics;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class PlanDatabaseSyncServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PlanDatabaseService _database;
    private readonly string _dbPath;
    private readonly PlanReaderService _planReader;
    private readonly PlanDatabaseSyncService _syncService;
    private readonly PlanWatcherService _watcher;

    public PlanDatabaseSyncServiceTests()
    {
        _dbPath = Path.Combine(_tempDir.Path, "tendril.db");

        var settings = new TendrilSettings();
        var configService = new ConfigService(settings, _tempDir.Path);
        _planReader = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
        _database = new PlanDatabaseService(_dbPath, NullLogger<PlanDatabaseService>.Instance);
        _watcher = new PlanWatcherService(configService);
        _syncService = new PlanDatabaseSyncService(
            _planReader, _database, _watcher, configService,
            NullLogger<PlanDatabaseSyncService>.Instance);
    }

    public void Dispose()
    {
        _syncService.Dispose();
        _watcher.Dispose();
        _database.Dispose();
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    private void CreatePlan(string folderName, string yaml, string? revisionContent = null)
    {
        var dir = Path.Combine(_planReader.PlansDirectory, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"), yaml);

        if (revisionContent != null)
        {
            var revisionsDir = Path.Combine(dir, "Revisions");
            Directory.CreateDirectory(revisionsDir);
            File.WriteAllText(Path.Combine(revisionsDir, "001.md"), revisionContent);
        }
    }

    [Fact]
    public void PerformInitialSync_SyncsPlansToDatabase()
    {
        var yaml =
            "state: Draft\nproject: Tendril\ntitle: Test Plan\nlevel: NiceToHave\nrepos: []\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n";
        CreatePlan("01500-TestPlan", yaml, "# Test Plan Content");
        CreatePlan("01501-AnotherPlan", yaml.Replace("Test Plan", "Another Plan"), "# Another");

        _syncService.PerformInitialSync();

        Assert.True(_syncService.IsInitialSyncComplete);

        var plans = _database.GetPlans();
        Assert.Equal(2, plans.Count);
    }

    [Fact]
    public void PerformInitialSync_EnablesDatabaseReads()
    {
        var yaml =
            "state: Draft\nproject: Tendril\ntitle: Test Plan\nlevel: NiceToHave\nrepos: []\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n";
        CreatePlan("01500-TestPlan", yaml, "# Test");

        _syncService.PerformInitialSync();

        // After sync, PlanReaderService should use database
        var plans = _planReader.GetPlans();
        Assert.Single(plans);
        Assert.Equal("Test Plan", plans[0].Title);
    }

    [Fact]
    public void PerformInitialSync_SyncsCosts()
    {
        var yaml =
            "state: Completed\nproject: Tendril\ntitle: Cost Plan\nlevel: NiceToHave\nrepos: []\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n";
        var dir = Path.Combine(_planReader.PlansDirectory, "01500-CostPlan");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"), yaml);
        var revisionsDir = Path.Combine(dir, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "# Cost Plan");
        File.WriteAllText(Path.Combine(dir, "costs.csv"),
            "promptware,tokens,cost\nExecutePlan,50000,1.50\nCreatePr,10000,0.30\n");

        _syncService.PerformInitialSync();

        var totalCost = _database.GetPlanTotalCost(1500);
        Assert.Equal(1.80m, totalCost);
    }

    /// <summary>
    ///     A plan folder holding <paramref name="costsCsv" />, ready to sync. Completed and dated today
    ///     so the state-filtered, last-7-days dashboard aggregates all see it.
    /// </summary>
    private void CreateCostPlan(string folderName, string costsCsv)
    {
        var stamp = DateTime.UtcNow.ToString("O");
        var yaml = "state: Completed\nproject: Tendril\ntitle: Cost Plan\nlevel: NiceToHave\nrepos: []\n"
                   + "commits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\n"
                   + $"created: {stamp}\nupdated: {stamp}\n";
        CreatePlan(folderName, yaml, "# Cost Plan");
        File.WriteAllText(Path.Combine(_planReader.PlansDirectory, folderName, "costs.csv"), costsCsv);
    }

    /// <summary>
    ///     Reads the synced Costs rows straight out of SQLite. Null and 0 are the whole point here and
    ///     no aggregate on the service can tell them apart, so the rows are inspected directly.
    /// </summary>
    private List<(string Promptware, int Tokens, decimal? Cost, string? Model, string? CostSource, string? Agent)> ReadCostRows(int planId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Promptware, Tokens, Cost, Model, CostSource, Agent FROM Costs WHERE PlanId = @p ORDER BY Id";
        cmd.Parameters.AddWithValue("@p", planId);

        var rows = new List<(string, int, decimal?, string?, string?, string?)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add((
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows;
    }

    [Fact]
    public void PerformInitialSync_LegacyFourColumnFile_ResolvesCostSourceFromJobsAndUpgradesFile()
    {
        CreateCostPlan("01500-CostPlan",
            "Promptware,Tokens,Cost,Model\nExecutePlan,50000,1.5000,gemini-3.8-flash\n");

        var job = new JobItem
        {
            Id = "job-1500",
            Type = "ExecutePlan",
            PlanFile = "01500-CostPlan",
            ReportedPlanId = "1500",
            Project = "Tendril",
            Status = JobStatus.Completed,
            Provider = "antigravity",
            CostSource = JobCostSources.Estimated,
            CompletedAt = DateTime.UtcNow
        };
        _database.UpsertJob(job);

        _syncService.PerformInitialSync();

        var rows = ReadCostRows(1500);
        Assert.Single(rows);
        Assert.Equal("estimated", rows[0].CostSource);

        var csvPath = Path.Combine(_planReader.PlansDirectory, "01500-CostPlan", "costs.csv");
        var lines = File.ReadAllLines(csvPath);
        Assert.Equal("Promptware,Tokens,Cost,Model,CostSource,Agent", lines[0]);
        Assert.Equal("ExecutePlan,50000,1.5000,gemini-3.8-flash,estimated,antigravity", lines[1]);
    }

    [Fact]
    public void PerformInitialSync_UnknownCost_KeepsTheRowWithANullCost()
    {
        // The round trip the estimated tier depends on: an unpriceable run writes an empty Cost field,
        // and the row has to survive it. Dropping the row would lose the token count too, which is the
        // only thing the backfill can price from. Written through LogCostToCsv rather than by hand, so
        // the writer and the parser are pinned to each other.
        CreateCostPlan("01500-CostPlan", "Promptware,Tokens,Cost,Model\n");
        var folder = Path.Combine(_planReader.PlansDirectory, "01500-CostPlan");
        JobService.LogCostToCsv(folder, "ExecutePlan", 150_000, null, "claude-opus-5");
        JobService.LogCostToCsv(folder, "CreatePr", 10_000, 0.30m, "claude-opus-5");

        _syncService.PerformInitialSync();

        // Only the priced row contributes; the unknown one is skipped by SUM rather than counted as 0.
        Assert.Equal(0.30m, _database.GetPlanTotalCost(1500));
        Assert.Equal(160_000, _database.GetPlanTotalTokens(1500));

        var rows = ReadCostRows(1500);
        Assert.Equal(2, rows.Count);
        Assert.Null(rows[0].Cost);
        Assert.Equal(150_000, rows[0].Tokens);
        Assert.Equal("claude-opus-5", rows[0].Model);
    }

    [Fact]
    public void PerformInitialSync_LegacyThreeColumnFile_StillParses()
    {
        CreateCostPlan("01500-CostPlan", "promptware,tokens,cost\nExecutePlan,50000,1.50\nCreatePr,10000,0.30\n");

        _syncService.PerformInitialSync();

        Assert.Equal(1.80m, _database.GetPlanTotalCost(1500));
        Assert.All(ReadCostRows(1500), r => Assert.Null(r.Model));
    }

    [Fact]
    public void PerformInitialSync_MixedFile_ParsesThreeAndFourColumnRowsAlike()
    {
        // What a plan folder actually looks like after the upgrade: the header is whatever it was
        // created with, and the rows appended since carry a fourth field.
        CreateCostPlan("01500-CostPlan",
            "Promptware,Tokens,Cost\nExecutePlan,50000,1.5000\nCreatePr,10000,0.3000,claude-opus-5\n");

        _syncService.PerformInitialSync();

        var rows = ReadCostRows(1500);
        Assert.Equal(1.80m, _database.GetPlanTotalCost(1500));
        Assert.Null(rows[0].Model);
        Assert.Equal("claude-opus-5", rows[1].Model);
    }

    [Fact]
    public void PerformInitialSync_MalformedCost_KeepsTheRowRatherThanLosingTheTokens()
    {
        CreateCostPlan("01500-CostPlan",
            "Promptware,Tokens,Cost,Model\nExecutePlan,50000,not-a-number,claude-opus-5\n");

        _syncService.PerformInitialSync();

        var row = Assert.Single(ReadCostRows(1500));
        Assert.Null(row.Cost);
        Assert.Equal(50_000, row.Tokens);
    }

    [Fact]
    public void GetDashboardData_UnpricedPlan_DoesNotDragTheAverageDown()
    {
        // Two plans, one priced at $2 and one unpriceable. The average is $2, not $1: a plan nobody
        // could price is not a plan that cost nothing.
        CreateCostPlan("01500-CostPlan",
            "Promptware,Tokens,Cost,Model\nExecutePlan,50000,2.0000,claude-opus-5\n");
        CreateCostPlan("01501-CostPlan",
            "Promptware,Tokens,Cost,Model\nExecutePlan,50000,,claude-opus-5\n");

        _syncService.PerformInitialSync();

        Assert.Equal(2m, _database.GetDashboardData(null).AvgCostPerPlan);
    }

    [Fact]
    public void PerformInitialSync_SyncsRecommendations()
    {
        var yaml =
            "state: Completed\nproject: Tendril\ntitle: Rec Plan\nlevel: NiceToHave\nrepos: []\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\nrecommendations:\n  - title: Add tests\n    description: Need more tests\n    state: Pending\n";
        var dir = Path.Combine(_planReader.PlansDirectory, "01500-RecPlan");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"), yaml);
        var revisionsDir = Path.Combine(dir, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "# Rec Plan");

        _syncService.PerformInitialSync();

        var recs = _database.GetRecommendations();
        Assert.Single(recs);
        Assert.Equal("Add tests", recs[0].Title);
    }

    [Fact]
    public void PerformInitialSync_HandlesmalformedRecommendationsYaml()
    {
        var yaml =
            "state: Completed\nproject: Tendril\ntitle: Bad Recs Plan\nlevel: NiceToHave\nrepos: []\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n";
        var dir = Path.Combine(_planReader.PlansDirectory, "01502-BadRecsPlan");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"), yaml);
        var revisionsDir = Path.Combine(dir, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "# Bad Recs Plan");

        _syncService.PerformInitialSync();

        Assert.True(_syncService.IsInitialSyncComplete);
        var recs = _database.GetRecommendations();
        Assert.Empty(recs);
    }

    [Fact]
    public void PerformInitialSync_WithEmptyPlansDirectory_Succeeds()
    {
        _syncService.PerformInitialSync();

        Assert.True(_syncService.IsInitialSyncComplete);
        Assert.Empty(_database.GetPlans());
    }

    [Fact]
    public void PerformInitialSync_SetsLastSyncTime()
    {
        _syncService.PerformInitialSync();

        var syncTime = _database.GetLastSyncTime();
        Assert.True(syncTime > DateTime.MinValue);
    }

    [Fact]
    public void PerformInitialSync_FiveColumnFile_ResolvesAgentFromJobsAndUpgradesFile()
    {
        CreateCostPlan("01600-CostPlan",
            "Promptware,Tokens,Cost,Model,CostSource\nExecutePlan,50000,1.5000,gemini-3.8-flash,agent\n");

        var job = new JobItem
        {
            Id = "job-1600",
            Type = "ExecutePlan",
            PlanFile = "01600-CostPlan",
            ReportedPlanId = "1600",
            Project = "Tendril",
            Status = JobStatus.Completed,
            Provider = "gemini",
            CostSource = JobCostSources.Agent,
            CompletedAt = DateTime.UtcNow
        };
        _database.UpsertJob(job);

        _syncService.PerformInitialSync();

        var rows = ReadCostRows(1600);
        Assert.Single(rows);
        Assert.Equal("gemini", rows[0].Agent);

        var csvPath = Path.Combine(_planReader.PlansDirectory, "01600-CostPlan", "costs.csv");
        var lines = File.ReadAllLines(csvPath);
        Assert.Equal("Promptware,Tokens,Cost,Model,CostSource,Agent", lines[0]);
        Assert.Equal("ExecutePlan,50000,1.5000,gemini-3.8-flash,agent,gemini", lines[1]);
    }

    [Fact]
    public void PerformInitialSync_SixColumnFile_PreservesAgent()
    {
        CreateCostPlan("01700-CostPlan",
            "Promptware,Tokens,Cost,Model,CostSource,Agent\nExecutePlan,50000,1.5000,claude-opus-5,agent,claude\n");

        _syncService.PerformInitialSync();

        var rows = ReadCostRows(1700);
        Assert.Single(rows);
        Assert.Equal("claude", rows[0].Agent);

        var csvPath = Path.Combine(_planReader.PlansDirectory, "01700-CostPlan", "costs.csv");
        var lines = File.ReadAllLines(csvPath);
        Assert.Equal("Promptware,Tokens,Cost,Model,CostSource,Agent", lines[0]);
        Assert.Equal("ExecutePlan,50000,1.5000,claude-opus-5,agent,claude", lines[1]);
    }

    [Fact]
    public void PerformInitialSync_FiveColumnFile_NoMatchingJob_LeavesAgentNull()
    {
        CreateCostPlan("01800-CostPlan",
            "Promptware,Tokens,Cost,Model,CostSource\nExecutePlan,50000,1.5000,gemini-3.8-flash,agent\n");

        _syncService.PerformInitialSync();

        var rows = ReadCostRows(1800);
        Assert.Single(rows);
        Assert.Null(rows[0].Agent);
    }

    private const string DraftYaml =
        "state: Draft\nproject: Tendril\ntitle: Test Plan\nlevel: NiceToHave\nrepos: []\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n";

    /// <summary>
    ///     The behaviour #2571 was about: closing a dozen plans in a row raises a dozen change events,
    ///     and they must not turn into a dozen full rescans. The events are raised straight at
    ///     <see cref="PlanDatabaseSyncService.OnPlansChanged" /> rather than through
    ///     <see cref="PlanWatcherService.NotifyChanged" />, whose own debounce would hide the coalescing
    ///     being asserted here.
    /// </summary>
    [Fact]
    public async Task OnPlansChanged_BurstOfFullRescans_CostsAtMostTwoSyncs()
    {
        CreatePlan("01500-TestPlan", DraftYaml, "# Test");
        _syncService.PerformInitialSync();

        for (var i = 0; i < 20; i++)
            _syncService.OnPlansChanged(null);

        await _syncService.DrainAsync();

        // One for the pass in flight, at most one more for everything that arrived while it ran.
        Assert.InRange(_syncService.SyncCount, 1, 2);
    }

    /// <summary>
    ///     The freeze itself: <c>PlansChanged</c> is raised from the watcher's timers and from every plan
    ///     mutation, so a rescan running inline would block whichever thread closed the plan.
    /// </summary>
    [Fact]
    public async Task OnPlansChanged_DoesNotSyncOnTheRaisingThread()
    {
        CreatePlan("01500-TestPlan", DraftYaml, "# Test");
        _syncService.PerformInitialSync();

        var raisingThreadId = Environment.CurrentManagedThreadId;
        _syncService.OnPlansChanged(null);
        await _syncService.DrainAsync();

        Assert.NotEqual(0, _syncService.LastSyncThreadId);
        Assert.NotEqual(raisingThreadId, _syncService.LastSyncThreadId);
    }

    [Fact]
    public void TakeWork_FullRescanRequest_SupersedesPendingFolders()
    {
        CreatePlan("01500-TestPlan", DraftYaml, "# Test");
        var folder = Path.Combine(_planReader.PlansDirectory, "01500-TestPlan");

        // RecordPending rather than OnPlansChanged: queuing without waking the worker is what makes the
        // coalesced state observable from the test thread.
        _syncService.RecordPending(folder);
        _syncService.RecordPending(null);

        var (fullRescan, folders) = _syncService.TakeWork();

        Assert.True(fullRescan);
        Assert.Empty(folders);

        // And the superseded folder is gone rather than queued behind the rescan that covers it.
        Assert.False(_syncService.TakeWork().FullRescan);
        Assert.Empty(_syncService.TakeWork().Folders);
    }

    [Fact]
    public void TakeWork_DistinctFolders_AreKeptSeparate()
    {
        CreatePlan("01500-TestPlan", DraftYaml, "# Test");
        CreatePlan("01501-OtherPlan", DraftYaml, "# Other");
        var first = Path.Combine(_planReader.PlansDirectory, "01500-TestPlan");
        var second = Path.Combine(_planReader.PlansDirectory, "01501-OtherPlan");

        _syncService.RecordPending(first);
        _syncService.RecordPending(second);
        _syncService.RecordPending(first);

        var (fullRescan, folders) = _syncService.TakeWork();

        Assert.False(fullRescan);
        Assert.Equal(2, folders.Count);
        Assert.Contains(first, folders);
        Assert.Contains(second, folders);
    }

    [Fact]
    public void Dispose_WithSyncInFlight_ReturnsWithinTheShutdownBudget()
    {
        for (var i = 0; i < 40; i++)
            CreatePlan($"0{1500 + i}-TestPlan", DraftYaml, "# Test");
        _syncService.PerformInitialSync();

        for (var i = 0; i < 10; i++)
            _syncService.OnPlansChanged(null);

        var stopwatch = Stopwatch.StartNew();
        _syncService.Dispose();
        stopwatch.Stop();

        // Bounded at 5s inside Dispose; the slack covers a slow CI disk finishing the pass in flight.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"Dispose took {stopwatch.Elapsed.TotalSeconds:F1}s");

        // Idempotent: the fixture disposes it again.
        _syncService.Dispose();
    }

    /// <summary>
    ///     Guards the change-2 refactor: a rescan now enumerates the Jobs directory once and hands the
    ///     grouped result to every plan, where it used to glob per plan. Both paths must produce the same
    ///     rows, log timestamps included — those are the only part of a cost row the lookup feeds.
    /// </summary>
    [Fact]
    public async Task SinglePlanSync_AndFullRescan_ProduceTheSameCostRows()
    {
        CreateCostPlan("01500-CostPlan",
            "Promptware,Tokens,Cost,Model,CostSource,Agent\nExecutePlan,50000,1.5000,claude-opus-5,agent,claude\nCreatePr,10000,0.3000,claude-opus-5,agent,claude\n");
        var jobsDir = JobLogPaths.EnsureJobsDir(_tempDir.Path);
        File.WriteAllText(Path.Combine(jobsDir, "00001-01500-ExecutePlan.md"),
            "# Job\n**Completed:** 2026-01-02T03:04:05Z\n");
        File.WriteAllText(Path.Combine(jobsDir, "00002-01500-CreatePr.md"),
            "# Job\n**Completed:** 2026-01-03T04:05:06Z\n");
        // Neither of these belongs to the plan's cost rows: a prompt is not a log, and a CreatePlan log
        // carries no plan-id segment.
        File.WriteAllText(Path.Combine(jobsDir, "00003-01500-ExecutePlan.prompt.md"), "prompt");
        File.WriteAllText(Path.Combine(jobsDir, "00004-CreatePlan.md"), "**Completed:** 2026-01-04T00:00:00Z\n");

        // The lookup path: PerformInitialSync goes through SyncPlanDetails.
        _syncService.PerformInitialSync();
        var fromRescan = ReadCostRowsWithTimestamps(1500);

        // The per-plan glob path: a single-folder sync passes no lookup.
        _syncService.OnPlansChanged(Path.Combine(_planReader.PlansDirectory, "01500-CostPlan"));
        await _syncService.DrainAsync();
        var fromSingleSync = ReadCostRowsWithTimestamps(1500);

        Assert.Equal(2, fromRescan.Count);
        Assert.StartsWith("2026-01-02T03:04:05", fromRescan[0].LogTimestamp);
        Assert.StartsWith("2026-01-03T04:05:06", fromRescan[1].LogTimestamp);
        Assert.Equal(fromRescan, fromSingleSync);
    }

    /// <summary>
    ///     Cost rows including the log timestamp, which is the part of a row the log lookup feeds. Read as
    ///     the stored text rather than through <c>GetDateTime</c>, which would shift a UTC value into
    ///     local time and make the expectations machine dependent.
    /// </summary>
    private List<(string Promptware, int Tokens, decimal? Cost, string? LogTimestamp)> ReadCostRowsWithTimestamps(int planId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Promptware, Tokens, Cost, LogTimestamp FROM Costs WHERE PlanId = @p ORDER BY Id";
        cmd.Parameters.AddWithValue("@p", planId);

        var rows = new List<(string, int, decimal?, string?)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add((
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return rows;
    }
}
