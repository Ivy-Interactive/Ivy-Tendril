using Ivy.Tendril.Services;
using Ivy.Tendril.Test.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

/// <summary>
///     Covers the self-heal re-scan behaviour added for #1257: a brand-new plan folder fires the
///     top-level FileSystemWatcher while still empty, then plan.yaml / Revisions land a moment
///     later (writes inside the folder, which don't re-trigger the watcher). The staggered
///     self-heal re-scans must surface the completed folder without waiting for the 30s poll.
/// </summary>
public class PlanWatcherServiceTests : IDisposable
{
    // Generous outer timeout: real FileSystemWatcher events are timing-dependent (especially on
    // CI), so we assert eventual state rather than exact event counts/ordering.
    private const int WatchTimeoutMs = 10_000;

    private const string DraftYaml =
        "state: Draft\nproject: Tendril\ntitle: Test Plan\nlevel: NiceToHave\nrepos: []\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n";

    private readonly ConfigService _configService;
    private readonly TempDirectoryFixture _tempDir = new();

    public PlanWatcherServiceTests()
    {
        _configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
        // The (settings, home) test constructor does not run CreateRequiredDirectories, so create
        // the Plans dir explicitly — otherwise PlanWatcherService's "directory missing" guard trips
        // and no watcher/poll is set up. (Production uses the public ctor, which creates it.)
        Directory.CreateDirectory(_configService.PlanFolder);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    private string CreateEmptyPlanFolder(string folderName)
    {
        var dir = Path.Combine(_configService.PlanFolder, folderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WritePlanContent(string planDir)
    {
        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), DraftYaml);
        var revisionsDir = Path.Combine(planDir, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "# Test Plan Content");
    }

    [Fact]
    public void SelfHeal_ReRaisesPlansChanged_AfterLateArrivingContent()
    {
        using var watcher = new PlanWatcherService(_configService, null, new[] { 50, 150, 300 });

        var changeCount = 0;
        var changesAfterContent = 0;
        var contentWritten = new ManualResetEventSlim(false);

        watcher.PlansChanged += _ =>
        {
            Interlocked.Increment(ref changeCount);
            if (contentWritten.IsSet)
                Interlocked.Increment(ref changesAfterContent);
        };

        // Folder appears empty first (mirrors `tendril plan create` creating the dir before
        // writing plan.yaml / the first revision).
        var planDir = CreateEmptyPlanFolder("01600-SelfHealPlan");

        // Content lands shortly after — inside the folder, so it does NOT re-trigger the FSW.
        Thread.Sleep(100);
        WritePlanContent(planDir);
        contentWritten.Set();

        // The staggered self-heal burst must re-raise PlansChanged multiple times, with at least
        // one rescan after the content landed. Poll for both outcomes rather than sampling at a
        // single instant — under load the burst's timer callbacks arrive with arbitrary spacing.
        Assert.True(
            RetryHelper.WaitUntil(
                () => Volatile.Read(ref changeCount) > 1 && Volatile.Read(ref changesAfterContent) >= 1,
                TimeSpan.FromSeconds(15)),
            "Expected multiple self-heal PlansChanged, including at least one after late-arriving content");
    }

    [Fact]
    public void OutOfBandPlan_AppearsInDatabase_WithoutRestart()
    {
        var dbPath = Path.Combine(_tempDir.Path, "tendril.db");
        var planReader = new PlanReaderService(_configService, NullLogger<PlanReaderService>.Instance);
        using var database = new PlanDatabaseService(dbPath, NullLogger<PlanDatabaseService>.Instance);
        // Self-heal delays straddle the content write: the 500ms debounce fires while the folder is
        // still empty (and would be the ONLY rescan without this fix), so the plan only reaches the
        // DB because a later self-heal rescan runs after the content lands. This makes the test a
        // genuine regression guard for #1257 rather than something the plain debounce would pass.
        using var watcher = new PlanWatcherService(_configService, null, new[] { 800, 2500 });
        using var syncService = new PlanDatabaseSyncService(
            planReader, database, watcher, NullLogger<PlanDatabaseSyncService>.Instance);

        // Initial sync with no plans → enables database-backed reads.
        syncService.PerformInitialSync();
        Assert.True(syncService.IsInitialSyncComplete);
        Assert.Empty(database.GetPlans());

        // Create a plan out-of-band: empty folder first (FSW fires here, before content)...
        var planDir = CreateEmptyPlanFolder("01601-OutOfBandPlan");
        // ...then plan.yaml + revision well after the 500ms debounce would have fired empty.
        Thread.Sleep(1500);
        WritePlanContent(planDir);

        // Without restart, a self-heal rescan must sync the new plan into the database.
        Assert.True(
            RetryHelper.WaitUntil(() => database.GetPlans().Count == 1, TimeSpan.FromMilliseconds(WatchTimeoutMs)),
            "Expected the out-of-band plan to be synced into the database without a restart");
    }
}
