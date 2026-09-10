using Ivy.Tendril.Services;
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
    private List<(string Promptware, int Tokens, decimal? Cost, string? Model)> ReadCostRows(int planId)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Promptware, Tokens, Cost, Model FROM Costs WHERE PlanId = @p ORDER BY Id";
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
}
