using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.PullRequest;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.Plans;
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
        Assert.True(PullRequestApp.IsValidUrl("Https://github.com/owner/repo/pull/1"));
        Assert.True(PullRequestApp.IsValidUrl("HTTPS://GITHUB.COM/owner/repo/pull/1"));
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

    [Fact]
    public async Task RunSyncAsync_StoresMerged_ForUrlARecentWindowWouldMiss()
    {
        var url = "https://github.com/owner/repo/pull/1";
        var fakePlans = new FakePlanReaderService(new List<PlanFile>
        {
            CreatePlanWithPrs(new[] { url })
        });

        var fakeGithub = new FakeGithubService(new Dictionary<string, PrInfo>
        {
            [url] = new PrInfo("Merged", "main")
        });

        var service = new PrStatusSyncService(_db, fakeGithub, fakePlans, NullLogger<PrStatusSyncService>.Instance);
        await service.RunSyncAsync();

        var statuses = _db.GetAllPrStatuses();
        Assert.Single(statuses);
        Assert.Equal("Merged", statuses[url].Status);
    }

    [Fact]
    public async Task RunSyncAsync_LeavesExistingRowUntouched_WhenLookupFails()
    {
        var url = "https://github.com/owner/repo/pull/1";
        var seededTime = DateTime.UtcNow.AddHours(-1);
        _db.UpsertPrStatus(url, "owner", "repo", "Closed", "old-branch", seededTime);

        var fakePlans = new FakePlanReaderService(new List<PlanFile>
        {
            CreatePlanWithPrs(new[] { url })
        });

        var fakeGithub = new FakeGithubService(new Dictionary<string, PrInfo>());
        fakeGithub.SetError(url, "gh failed");

        var service = new PrStatusSyncService(_db, fakeGithub, fakePlans, NullLogger<PrStatusSyncService>.Instance);
        await service.RunSyncAsync();

        var statuses = _db.GetAllPrStatuses();
        Assert.Single(statuses);
        Assert.Equal("Closed", statuses[url].Status);
        Assert.Equal("old-branch", statuses[url].Branch);
    }

    [Fact]
    public async Task RunSyncAsync_DoesNotResolveUrlsAlreadyMerged()
    {
        var url1 = "https://github.com/owner/repo/pull/1";
        var url2 = "https://github.com/owner/repo/pull/2";
        _db.UpsertPrStatus(url1, "owner", "repo", "Merged", "main", DateTime.UtcNow);

        var fakePlans = new FakePlanReaderService(new List<PlanFile>
        {
            CreatePlanWithPrs(new[] { url1, url2 })
        });

        var fakeGithub = new FakeGithubService(new Dictionary<string, PrInfo>
        {
            [url2] = new PrInfo("Open", "feature")
        });

        var service = new PrStatusSyncService(_db, fakeGithub, fakePlans, NullLogger<PrStatusSyncService>.Instance);
        await service.RunSyncAsync();

        Assert.Single(fakeGithub.ResolvedUrls);
        Assert.Equal(url2, fakeGithub.ResolvedUrls[0]);
    }

    [Fact]
    public async Task RunSyncAsync_KeysRowByRecordedUrl_WithFilesSuffix()
    {
        var urlWithSuffix = "https://github.com/owner/repo/pull/7/files";
        var fakePlans = new FakePlanReaderService(new List<PlanFile>
        {
            CreatePlanWithPrs(new[] { urlWithSuffix })
        });

        var fakeGithub = new FakeGithubService(new Dictionary<string, PrInfo>
        {
            [urlWithSuffix] = new PrInfo("Open", "feature")
        });

        var service = new PrStatusSyncService(_db, fakeGithub, fakePlans, NullLogger<PrStatusSyncService>.Instance);
        await service.RunSyncAsync();

        var statuses = _db.GetAllPrStatuses();
        Assert.Single(statuses);
        Assert.True(statuses.ContainsKey(urlWithSuffix));
    }

    [Fact]
    public async Task RunSyncAsync_SecondConcurrentRun_PerformsNoLookups()
    {
        var url = "https://github.com/owner/repo/pull/1";
        var fakePlans = new FakePlanReaderService(new List<PlanFile>
        {
            CreatePlanWithPrs(new[] { url })
        });

        var gate = new TaskCompletionSource<bool>();
        var fakeGithub = new FakeGithubService(new Dictionary<string, PrInfo>
        {
            [url] = new PrInfo("Open", "main")
        });
        fakeGithub.SetGate(gate.Task);

        var service = new PrStatusSyncService(_db, fakeGithub, fakePlans, NullLogger<PrStatusSyncService>.Instance);

        var task1 = service.RunSyncAsync();
        await Task.Delay(50);
        var task2 = service.RunSyncAsync();

        gate.SetResult(true);
        await Task.WhenAll(task1, task2);

        Assert.Single(fakeGithub.ResolvedUrls);
    }

    [Fact]
    public async Task SyncPrAsync_ResolvesOnlyThatUrl()
    {
        var url1 = "https://github.com/owner/repo/pull/1";
        var url2 = "https://github.com/owner/repo/pull/2";

        _db.UpsertPrStatus(url2, "owner", "repo", "Open", "", DateTime.UtcNow);

        var fakeGithub = new FakeGithubService(new Dictionary<string, PrInfo>
        {
            [url1] = new PrInfo("Merged", "main")
        });

        var service = new PrStatusSyncService(_db, fakeGithub, new FakePlanReaderService(new List<PlanFile>()),
            NullLogger<PrStatusSyncService>.Instance);
        var result = await service.SyncPrAsync(url1);

        Assert.True(result);
        Assert.Single(fakeGithub.ResolvedUrls);
        Assert.Equal(url1, fakeGithub.ResolvedUrls[0]);

        var statuses = _db.GetAllPrStatuses();
        Assert.Equal(2, statuses.Count);
        Assert.Equal("Merged", statuses[url1].Status);
    }

    private static PlanFile CreatePlanWithPrs(string[] prs)
    {
        var metadata = new PlanMetadata(
            1,
            "test-project",
            "Feature",
            "Test Plan",
            PlanStatus.Draft,
            [],
            prs.ToList(),
            [],
            [],
            [],
            [],
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            null
        );
        return new PlanFile(metadata, "", "/fake/path", "");
    }

    private class FakePlanReaderService : IPlanReaderService
    {
        private readonly List<PlanFile> _plans;

        public FakePlanReaderService(List<PlanFile> plans)
        {
            _plans = plans;
        }

        public string PlansDirectory => throw new NotImplementedException();
        public bool IsDatabaseReady => throw new NotImplementedException();
#pragma warning disable CS0067
        public event Action? CountsInvalidated;
#pragma warning restore CS0067

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null) => _plans;
        public PlanFile? GetPlanByFolder(string folderPath) => throw new NotImplementedException();
        public void MigratePlans() => throw new NotImplementedException();
        public void RecoverStuckPlans() => throw new NotImplementedException();
        public List<PlanFile> GetIceboxPlans() => throw new NotImplementedException();
        public void TransitionState(string folderName, PlanStatus newState) => throw new NotImplementedException();
        public IReadOnlyList<string> GetFailedVerifications(string folderName) => throw new NotImplementedException();
        public void CompleteWithPartialDelivery(string folderName) => throw new NotImplementedException();
        public void ResetToDraft(string folderName) => throw new NotImplementedException();
        public void ResetVerificationsForRetry(string folderName) => throw new NotImplementedException();
        public void SetVerificationStatus(string folderName, string name, VerificationStatus status) => throw new NotImplementedException();
        public void SaveRevision(string folderName, string content) => throw new NotImplementedException();
        public void RevertRevision(string folderName) => throw new NotImplementedException();
        public string ReadLatestRevision(string folderName) => throw new NotImplementedException();
        public List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName) => throw new NotImplementedException();
        public void DeletePlan(string folderName) => throw new NotImplementedException();
        public string ReadRawPlan(string folderName) => throw new NotImplementedException();
        public void SavePlan(string folderName, string fullContent) => throw new NotImplementedException();
        public void UpdateLatestRevision(string folderName, string content) => throw new NotImplementedException();
        public DashboardModels GetDashboardData(string? projectFilter) => throw new NotImplementedException();
        public DashboardActivityStats GetDashboardActivity(int monthsBack = 24) => throw new NotImplementedException();
        public List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days) => throw new NotImplementedException();
        public decimal GetPlanTotalCost(string folderPath) => throw new NotImplementedException();
        public int GetPlanTotalTokens(string folderPath) => throw new NotImplementedException();
        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null) => throw new NotImplementedException();
        public List<Recommendation> GetRecommendations() => throw new NotImplementedException();
        public int GetPendingRecommendationsCount() => throw new NotImplementedException();
        public PlanReaderService.PlanCountSnapshot ComputePlanCounts() => throw new NotImplementedException();
        public void UpdateRecommendationState(string planFolderName, string recommendationTitle, string newState, string? declineReason = null) => throw new NotImplementedException();
        public List<RecommendationYaml> GetRecommendationsForPlan(string folderName) => throw new NotImplementedException();
        public void AcceptRecommendationAndRetry(string folderName, string recommendationTitle) => throw new NotImplementedException();
        public void AcceptRecommendationsAndRetry(string folderName, IReadOnlyCollection<string> titles) => throw new NotImplementedException();
        public void SyncPlanArtifacts(string planFolder) => throw new NotImplementedException();
        public void InvalidateCaches() => throw new NotImplementedException();
        public Task FlushPendingWritesAsync() => throw new NotImplementedException();
    }

    private class FakeGithubService : IGithubService
    {
        private readonly Dictionary<string, PrInfo> _responses;
        private readonly Dictionary<string, string> _errors = new();
        private Task? _gate;

        public List<string> ResolvedUrls { get; } = new();

        public FakeGithubService(Dictionary<string, PrInfo> responses)
        {
            _responses = responses;
        }

        public void SetError(string url, string error)
        {
            _errors[url] = error;
        }

        public void SetGate(Task gate)
        {
            _gate = gate;
        }

        public async Task<(PrInfo? info, string? error)> GetPrStatusAsync(string prUrl)
        {
            if (_gate is not null)
                await _gate;

            ResolvedUrls.Add(prUrl);

            if (_errors.TryGetValue(prUrl, out var error))
                return (null, error);

            if (_responses.TryGetValue(prUrl, out var info))
                return (info, null);

            return (null, "Not found");
        }

        public List<RepoConfig> GetRepos() => throw new NotImplementedException();
        public RepoConfig? GetRepoConfigFromPathCached(string repoPath) => throw new NotImplementedException();
        public ProjectConfig? FindProjectForGithubRepo(string ownerRepo) => throw new NotImplementedException();
        public IReadOnlyList<string> GetResolvedGithubRepos(ProjectConfig project) => throw new NotImplementedException();
        public Task<(List<string> assignees, string? error)> GetAssigneesAsync(string owner, string repo) =>
            throw new NotImplementedException();
        public Task<(List<string> labels, string? error)> GetLabelsAsync(string owner, string repo) =>
            throw new NotImplementedException();
        public Task<(Dictionary<string, PrInfo> statuses, string? error)> GetPrStatusesAsync(string owner, string repo) =>
            throw new NotImplementedException();
        public Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request) =>
            throw new NotImplementedException();
    }
}
