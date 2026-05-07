using Ivy.Tendril.Apps;
using Ivy.Tendril.Services;
using Ivy.Tendril.Models;
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
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", DateTime.UtcNow);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/2", "owner", "repo", "Merged", DateTime.UtcNow);

        var statuses = _db.GetAllPrStatuses();
        Assert.Equal(2, statuses.Count);
        Assert.Equal("Open", statuses["https://github.com/owner/repo/pull/1"]);
        Assert.Equal("Merged", statuses["https://github.com/owner/repo/pull/2"]);
    }

    [Fact]
    public void UpsertPrStatus_UpdatesExistingStatus()
    {
        var now = DateTime.UtcNow;
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", now);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Merged", now.AddMinutes(10));

        var statuses = _db.GetAllPrStatuses();
        Assert.Single(statuses);
        Assert.Equal("Merged", statuses["https://github.com/owner/repo/pull/1"]);
    }

    [Fact]
    public void GetNonMergedPrUrls_ExcludesMerged()
    {
        var now = DateTime.UtcNow;
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/1", "owner", "repo", "Open", now);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/2", "owner", "repo", "Merged", now);
        _db.UpsertPrStatus("https://github.com/owner/repo/pull/3", "owner", "repo", "Closed", now);

        var nonMerged = _db.GetNonMergedPrUrls();
        Assert.Equal(2, nonMerged.Count);
        Assert.Contains("https://github.com/owner/repo/pull/1", nonMerged);
        Assert.Contains("https://github.com/owner/repo/pull/3", nonMerged);
        Assert.DoesNotContain("https://github.com/owner/repo/pull/2", nonMerged);
    }

    [Fact]
    public void GroupByHostOwnerRepo_OnlyReceivesValidPrUrls_WhenFilteredByIsValidUrl()
    {
        var rawUrls = new List<string>
        {
            "https://github.com/owner/repo/pull/1",
            "https://github.com/Ivy-Interactive/Ivy.Releases (new repo — no PR needed)",
            "https://github.com/owner/repo" // repo URL, not a PR
        };

        // Simulate the CollectPrUrlsFromPlans filter
        var filtered = rawUrls.Where(PullRequestApp.IsValidUrl).ToList();
        var grouped = PrStatusSyncService.GroupByHostOwnerRepo(filtered);
        Assert.Single(grouped);
        Assert.Single(grouped["github.com|owner/repo"]);
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
    public void GroupByHostOwnerRepo_BatchesCorrectly()
    {
        var urls = new List<string>
        {
            "https://github.com/owner1/repo1/pull/1",
            "https://github.com/owner1/repo1/pull/2",
            "https://github.com/owner2/repo2/pull/10",
            "https://github.com/owner1/repo3/pull/5",
            "https://bitbucket.org/workspace/repo4/pull-requests/1"
        };

        var grouped = PrStatusSyncService.GroupByHostOwnerRepo(urls);
        Assert.Equal(4, grouped.Count);
        Assert.Equal(2, grouped["github.com|owner1/repo1"].Count);
        Assert.Single(grouped["github.com|owner2/repo2"]);
        Assert.Single(grouped["github.com|owner1/repo3"]);
        Assert.Single(grouped["bitbucket.org|workspace/repo4"]);
    }

    [Fact]
    public async Task RunSyncAsync_RoutesToCorrectServiceBasedOnHost()
    {
        var githubFake = new FakeGithubService();
        var bitbucketFake = new FakeBitbucketService();
        var planReaderFake = new FakePlanReaderService(new List<PlanFile>
        {
            new PlanFile(new PlanMetadata(1, "Test", "Test", "Test", PlanStatus.Draft, [], [], [ "https://github.com/owner/repo1/pull/1", "https://bitbucket.org/workspace/repo2/pull-requests/2" ], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null), "", "", "")
        });

        var service = new PrStatusSyncService(_db, githubFake, bitbucketFake, planReaderFake, NullLogger<PrStatusSyncService>.Instance);

        await service.RunSyncAsync();

        Assert.True(githubFake.WasCalled);
        Assert.True(bitbucketFake.WasCalled);
        
        var statuses = _db.GetAllPrStatuses();
        Assert.Equal(2, statuses.Count);
        Assert.Equal("Merged", statuses["https://github.com/owner/repo1/pull/1"]);
        Assert.Equal("Open", statuses["https://bitbucket.org/workspace/repo2/pull-requests/2"]);
    }

    private class FakeGithubService : IGithubService
    {
        public bool WasCalled { get; private set; }

        public Task<(Dictionary<string, string> statuses, string? error)> GetPrStatusesAsync(string owner, string repo)
        {
            WasCalled = true;
            var result = new Dictionary<string, string>
            {
                { $"https://github.com/{owner}/{repo}/pull/1", "Merged" }
            };
            return Task.FromResult<(Dictionary<string, string>, string?)>((result, null));
        }

        public List<RepoConfig> GetRepos() => [];
        public RepoConfig? GetRepoConfigFromPathCached(string repoPath) => null;
        public Task<(List<string> assignees, string? error)> GetAssigneesAsync(string owner, string repo) => Task.FromResult<(List<string>, string?)>(([], null));
        public Task<(List<string> labels, string? error)> GetLabelsAsync(string owner, string repo) => Task.FromResult<(List<string>, string?)>(([], null));
        public Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request) => Task.FromResult<(List<GitHubIssue>, string?)>(([], null));
    }

    private class FakeBitbucketService : IBitbucketService
    {
        public bool WasCalled { get; private set; }

        public Task<(Dictionary<string, string> statuses, string? error)> GetPrStatusesAsync(string workspace, string repoSlug, List<string> prUrls)
        {
            WasCalled = true;
            var result = new Dictionary<string, string>
            {
                { $"https://bitbucket.org/{workspace}/{repoSlug}/pull-requests/2", "Open" }
            };
            return Task.FromResult<(Dictionary<string, string>, string?)>((result, null));
        }

        public Task<(List<string> assignees, string? error)> GetAssigneesAsync(string workspace, string repoSlug) => Task.FromResult<(List<string>, string?)>(([], null));
        public Task<(List<string> labels, string? error)> GetLabelsAsync(string workspace, string repoSlug) => Task.FromResult<(List<string>, string?)>(([], null));
    }

    private class FakePlanReaderService(List<PlanFile> plans) : IPlanReaderService
    {
        public event Action? CountsInvalidated;
        public string PlansDirectory => "";
        public bool IsDatabaseReady => true;

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null) => plans;

        public void RecoverStuckPlans() { }
        public void RepairPlans() { }
        public PlanFile? GetPlanByFolder(string folderPath) => null;
        public List<PlanFile> GetIceboxPlans() => [];
        public void TransitionState(string folderName, PlanStatus newState) { }
        public void SaveRevision(string folderName, string content) { }
        public string ReadLatestRevision(string folderName) => "";
        public List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName) => [];
        public void AddLog(string folderName, string action, string content) { }
        public void DeletePlan(string folderName) { }
        public string ReadRawPlan(string folderName) => "";
        public void SavePlan(string folderName, string fullContent) { }
        public void UpdateLatestRevision(string folderName, string content) { }
        public DashboardModels GetDashboardData(string? projectFilter) => new(0, 0, 0, 0, 0, 0, 0, [], []);
        public decimal GetPlanTotalCost(string folderPath) => 0;
        public int GetPlanTotalTokens(string folderPath) => 0;
        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null) => [];
        public List<Recommendation> GetRecommendations() => [];
        public int GetPendingRecommendationsCount() => 0;
        public PlanReaderService.PlanCountSnapshot ComputePlanCounts() => new(0, 0, 0, 0, 0, 0);
        public void UpdateRecommendationState(string planFolderName, string recommendationTitle, string newState, string? declineReason = null) { }
        public void InvalidateCaches() { }
        public Task FlushPendingWritesAsync() => Task.CompletedTask;
    }
}
