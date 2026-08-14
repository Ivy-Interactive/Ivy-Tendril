using Ivy.Tendril.Apps;
using Ivy.Tendril.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class PrStatusSyncServiceTests : IDisposable
{
    private readonly PlanDatabaseService _db;
    private readonly string _dbPath;

    public PrStatusSyncServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid()}.db");
        _db = new PlanDatabaseService(_dbPath, NullLogger<PlanDatabaseService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        if (File.Exists(_dbPath + "-wal"))
            File.Delete(_dbPath + "-wal");
        if (File.Exists(_dbPath + "-shm"))
            File.Delete(_dbPath + "-shm");
    }

    [Fact]
    public void UpsertPrStatus_StoresAndRetrieves()
    {
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", "", DateTime.UtcNow);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/2", "owner", "repo", "Merged", "", DateTime.UtcNow);

        var statuses = _db.GetAllPrStatuses();
        Assert.Equal(2, statuses.Count);
        Assert.Equal("Open", statuses["https://github.com/owner/repo/pull/1"].Status);
        Assert.Equal("Merged", statuses["https://github.com/owner/repo/pull/2"].Status);
    }

    [Fact]
    public void UpsertPrStatus_UpdatesExistingStatus()
    {
        var now = DateTime.UtcNow;
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", "", now);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Merged", "", now.AddMinutes(10));

        var statuses = _db.GetAllPrStatuses();
        Assert.Single(statuses);
        Assert.Equal("Merged", statuses["https://github.com/owner/repo/pull/1"].Status);
    }

    [Fact]
    public void UpsertPrStatus_StoresAndRetrievesBranch()
    {
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", "feature/foo", DateTime.UtcNow);

        var statuses = _db.GetAllPrStatuses();
        Assert.Equal("feature/foo", statuses["https://github.com/owner/repo/pull/1"].Branch);
    }

    [Fact]
    public void UpsertPrStatus_UpdatesBranchOnConflict()
    {
        var now = DateTime.UtcNow;
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", "feature/foo", now);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", "feature/bar", now.AddMinutes(10));

        var statuses = _db.GetAllPrStatuses();
        Assert.Equal("feature/bar", statuses["https://github.com/owner/repo/pull/1"].Branch);
    }

    [Fact]
    public void GetAllPrStatuses_MapsNullBranchColumnToEmptyString()
    {
        // Simulate a pre-migration row: insert directly (on a fresh connection to the same file,
        // since PlanDatabaseService doesn't expose its connection), bypassing UpsertPrStatus and
        // leaving Branch NULL.
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO PrStatuses (PrUrl, Owner, Repo, Status, LastChecked)
                              VALUES (@url, @owner, @repo, @status, @checked)
                              """;
            cmd.Parameters.AddWithValue("@url", "https://github.com/owner/repo/pull/1");
            cmd.Parameters.AddWithValue("@owner", "owner");
            cmd.Parameters.AddWithValue("@repo", "repo");
            cmd.Parameters.AddWithValue("@status", "Open");
            cmd.Parameters.AddWithValue("@checked", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        var statuses = _db.GetAllPrStatuses();
        Assert.Equal("", statuses["https://github.com/owner/repo/pull/1"].Branch);
    }

    [Fact]
    public void GetNonMergedPrUrls_ExcludesMerged()
    {
        var now = DateTime.UtcNow;
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", "", now);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/2", "owner", "repo", "Merged", "", now);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/3", "owner", "repo", "Closed", "", now);

        var nonMerged = _db.GetNonMergedPrUrls();
        Assert.Equal(2, nonMerged.Count);
        Assert.Contains("https://github.com/owner/repo/pull/1", nonMerged);
        Assert.Contains("https://github.com/owner/repo/pull/3", nonMerged);
        Assert.DoesNotContain("https://github.com/owner/repo/pull/2", nonMerged);
    }

    [Fact]
    public void GroupByOwnerRepo_OnlyReceivesValidPrUrls_WhenFilteredByIsValidUrl()
    {
        var rawUrls = new List<string>
        {
            "https://github.com/owner/repo/pull/1",
            "https://github.com/Ivy-Interactive/Ivy.Releases (new repo — no PR needed)",
            "https://github.com/owner/repo" // repo URL, not a PR
        };

        // Simulate the CollectPrUrlsFromPlans filter
        var filtered = rawUrls.Where(PullRequestApp.IsValidUrl).ToList();
        var grouped = PrStatusSyncService.GroupByOwnerRepo(filtered);
        Assert.Single(grouped);
        Assert.Single(grouped["owner/repo"]);
    }

    [Fact]
    public void IsValidUrl_AcceptsValidPrUrls()
    {
        Assert.True(PullRequestApp.IsValidUrl("https://github.com/owner/repo/pull/1"));
        Assert.True(PullRequestApp.IsValidUrl("https://github.com/owner/repo/pull/123"));
        Assert.True(PullRequestApp.IsValidUrl("http://github.com/owner/repo/pull/1"));
    }

    [Fact]
    public void IsValidUrl_RejectsInvalidUrls()
    {
        Assert.False(PullRequestApp.IsValidUrl("https://github.com/owner/repo"));
        Assert.False(
            PullRequestApp.IsValidUrl("https://github.com/Ivy-Interactive/Ivy.Releases (new repo — no PR needed)"));
        Assert.False(PullRequestApp.IsValidUrl("not a url"));
        Assert.False(PullRequestApp.IsValidUrl("https://example.com/page"));
    }

    [Fact]
    public void GroupByOwnerRepo_BatchesCorrectly()
    {
        var urls = new List<string>
        {
            "https://github.com/owner1/repo1/pull/1",
            "https://github.com/owner1/repo1/pull/2",
            "https://github.com/owner2/repo2/pull/10",
            "https://github.com/owner1/repo3/pull/5"
        };

        var grouped = PrStatusSyncService.GroupByOwnerRepo(urls);
        Assert.Equal(3, grouped.Count);
        Assert.Equal(2, grouped["owner1/repo1"].Count);
        Assert.Single(grouped["owner2/repo2"]);
        Assert.Single(grouped["owner1/repo3"]);
    }
}
