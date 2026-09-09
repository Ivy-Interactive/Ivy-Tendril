using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.Inbox;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services.Inbox;

[Collection("TendrilHome")]
public class AssignedIssuesAutoImportServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("auto-import-tests");

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private ConfigService CreateConfigService(bool autoAccept = true, int interval = 15)
    {
        var settings = new TendrilSettings
        {
            Inbox = new InboxConfig
            {
                AutoAcceptAssignedIssues = autoAccept,
                CheckIntervalMinutes = interval
            }
        };

        var config = new ConfigService(settings);
        config.SetTendrilHome(_tempDir.Path);
        return config;
    }

    [Fact]
    public async Task RunSyncAsync_WhenDisabled_DoesNotFetchOrImport()
    {
        var config = CreateConfigService(autoAccept: false);
        var github = new TestGithubService
        {
            IssuesToReturn =
            [
                new GitHubIssue(101, "Fix login", "Login issue description", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/101")
            ]
        };
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, new FakeQueryService());

        await service.RunSyncAsync();

        Assert.Equal(0, github.CallCount);
        var inboxPath = Path.Combine(_tempDir.Path, "Inbox");
        if (Directory.Exists(inboxPath))
        {
            Assert.Empty(Directory.GetFiles(inboxPath));
        }
    }

    [Fact]
    public async Task RunSyncAsync_WhenEnabled_ImportsNewAssignedIssues()
    {
        var config = CreateConfigService(autoAccept: true);
        var github = new TestGithubService
        {
            IssuesToReturn =
            [
                new GitHubIssue(101, "Fix login bug", "Login fails with 500 error", ["bug"], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/101")
            ]
        };
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, new FakeQueryService());

        await service.RunSyncAsync();

        Assert.Equal(1, github.CallCount);
        var inboxPath = Path.Combine(_tempDir.Path, "Inbox");
        Assert.True(Directory.Exists(inboxPath));

        var files = Directory.GetFiles(inboxPath, "101-*.md");
        Assert.Single(files);

        var content = await File.ReadAllTextAsync(files[0]);
        Assert.Contains("project: Auto", content);
        Assert.Contains("[GitHub Issue #101](https://github.com/owner/repo/issues/101)", content);
        Assert.Contains("Login fails with 500 error", content);
    }

    [Fact]
    public async Task RunSyncAsync_ResolvesProjectOrDefaultsToAuto()
    {
        var config = CreateConfigService(autoAccept: true);
        var frontendProject = new ProjectConfig { Name = "FrontendProject" };

        var github = new TestGithubService
        {
            ProjectMap = new Dictionary<string, ProjectConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["org/frontend"] = frontendProject
            },
            IssuesToReturn =
            [
                new GitHubIssue(201, "Frontend issue", "UI bug", [], ["user1"], "org/frontend", "https://github.com/org/frontend/issues/201"),
                new GitHubIssue(202, "Backend issue", "API bug", [], ["user1"], "org/unmapped", "https://github.com/org/unmapped/issues/202")
            ]
        };
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, new FakeQueryService());

        await service.RunSyncAsync();

        var inboxPath = Path.Combine(_tempDir.Path, "Inbox");
        var frontendFiles = Directory.GetFiles(inboxPath, "201-*.md");
        Assert.Single(frontendFiles);
        var frontendContent = await File.ReadAllTextAsync(frontendFiles[0]);
        Assert.Contains("project: FrontendProject", frontendContent);

        var backendFiles = Directory.GetFiles(inboxPath, "202-*.md");
        Assert.Single(backendFiles);
        var backendContent = await File.ReadAllTextAsync(backendFiles[0]);
        Assert.Contains("project: Auto", backendContent);
    }

    [Fact]
    public async Task RunSyncAsync_SkipsExistingInboxFiles()
    {
        var config = CreateConfigService(autoAccept: true);
        var inboxPath = Path.Combine(_tempDir.Path, "Inbox");
        Directory.CreateDirectory(inboxPath);

        // Pre-create .md and .md.processing files
        await File.WriteAllTextAsync(Path.Combine(inboxPath, "301-existing.md"), "original 301");
        await File.WriteAllTextAsync(Path.Combine(inboxPath, "302-processing.md.processing"), "original 302");

        var github = new TestGithubService
        {
            IssuesToReturn =
            [
                new GitHubIssue(301, "Existing issue", "Body 301", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/301"),
                new GitHubIssue(302, "Processing issue", "Body 302", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/302"),
                new GitHubIssue(303, "New issue", "Body 303", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/303")
            ]
        };
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, new FakeQueryService());

        await service.RunSyncAsync();

        // 301 and 302 should be untouched
        var content301 = await File.ReadAllTextAsync(Path.Combine(inboxPath, "301-existing.md"));
        Assert.Equal("original 301", content301);

        Assert.True(File.Exists(Path.Combine(inboxPath, "302-processing.md.processing")));
        Assert.False(File.Exists(Path.Combine(inboxPath, "302-processing.md")));

        // 303 should be imported
        var files303 = Directory.GetFiles(inboxPath, "303-*.md");
        Assert.Single(files303);
    }

    [Fact]
    public async Task RunSyncAsync_SkipsExistingPlans()
    {
        var config = CreateConfigService(autoAccept: true);

        var existingPlan = new PlanFile(
            new PlanMetadata(
                Id: 1,
                Project: "Test",
                Level: "Feature",
                Title: "Already Planned",
                State: PlanStatus.Draft,
                Repos: [],
                Commits: [],
                Prs: [],
                Verifications: [],
                RelatedPlans: [],
                DependsOn: [],
                Created: DateTime.UtcNow,
                Updated: DateTime.UtcNow,
                InitialPrompt: null,
                SourceUrl: "https://github.com/owner/repo/issues/401"
            ),
            "",
            Path.Combine(_tempDir.Path, "Plans", "00001-AlreadyPlanned"),
            ""
        );

        var planReader = new FakePlanReaderService
        {
            Plans = [existingPlan]
        };

        var github = new TestGithubService
        {
            IssuesToReturn =
            [
                new GitHubIssue(401, "Planned issue", "Body 401", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/401"),
                new GitHubIssue(402, "Unplanned issue", "Body 402", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/402")
            ]
        };

        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, new FakeQueryService());

        await service.RunSyncAsync();

        var inboxPath = Path.Combine(_tempDir.Path, "Inbox");
        var files401 = Directory.GetFiles(inboxPath, "401-*.md");
        Assert.Empty(files401);

        var files402 = Directory.GetFiles(inboxPath, "402-*.md");
        Assert.Single(files402);
    }

    [Fact]
    public async Task RunSyncAsync_SkipsActiveJobs()
    {
        var config = CreateConfigService(autoAccept: true);

        var activeJob = new JobItem
        {
            Id = "00001",
            Status = JobStatus.Running,
            TypedArgs = new CreatePlanArgs("Implement changes for issue #501", "Auto")
        };

        var jobService = new TestJobService
        {
            Jobs = [activeJob]
        };

        var github = new TestGithubService
        {
            IssuesToReturn =
            [
                new GitHubIssue(501, "Active job issue", "Body 501", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/501"),
                new GitHubIssue(502, "No job issue", "Body 502", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/502")
            ]
        };

        var planReader = new FakePlanReaderService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, new FakeQueryService());

        await service.RunSyncAsync();

        var inboxPath = Path.Combine(_tempDir.Path, "Inbox");
        var files501 = Directory.GetFiles(inboxPath, "501-*.md");
        Assert.Empty(files501);

        var files502 = Directory.GetFiles(inboxPath, "502-*.md");
        Assert.Single(files502);
    }

    [Fact]
    public async Task TriggerManualCheckAsync_RunsImportImmediately()
    {
        // AutoAccept is disabled in config, but manual trigger must still run
        var config = CreateConfigService(autoAccept: false);
        var github = new TestGithubService
        {
            IssuesToReturn =
            [
                new GitHubIssue(601, "Manual check issue", "Body 601", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/601")
            ]
        };
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, new FakeQueryService());

        await service.TriggerManualCheckAsync();

        Assert.Equal(1, github.CallCount);
        var inboxPath = Path.Combine(_tempDir.Path, "Inbox");
        var files601 = Directory.GetFiles(inboxPath, "601-*.md");
        Assert.Single(files601);
    }

    [Fact]
    public async Task RunSyncAsync_WhenSuccessful_InvalidatesMyIssuesQueryTagOnce()
    {
        var config = CreateConfigService(autoAccept: true);
        var github = new TestGithubService
        {
            IssuesToReturn = [new GitHubIssue(101, "Issue 1", "Body", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/101")]
        };
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;
        var queryService = new FakeQueryService();

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, queryService);

        await service.RunSyncAsync();

        Assert.Single(queryService.InvalidatedTags);
        Assert.Equal(GithubService.MyIssuesQueryTag, queryService.InvalidatedTags[0]);
    }

    [Fact]
    public async Task RunSyncAsync_WhenSyncFails_DoesNotInvalidateQueryTag()
    {
        var config = CreateConfigService(autoAccept: true);
        var github = new TestGithubService
        {
            ErrorToReturn = "Failed to fetch issues"
        };
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;
        var queryService = new FakeQueryService();

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, queryService);

        await service.RunSyncAsync();

        Assert.Empty(queryService.InvalidatedTags);
    }

    [Fact]
    public async Task RunSyncAsync_WhenDisabled_DoesNotInvalidateQueryTag()
    {
        var config = CreateConfigService(autoAccept: false);
        var github = new TestGithubService
        {
            IssuesToReturn = [new GitHubIssue(101, "Issue 1", "Body", [], ["user1"], "owner/repo", "https://github.com/owner/repo/issues/101")]
        };
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;
        var queryService = new FakeQueryService();

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, queryService);

        await service.RunSyncAsync();

        Assert.Empty(queryService.InvalidatedTags);
    }

    [Fact]
    public async Task RunSyncAsync_WhenAlreadyInProgress_DoesNotInvalidateQueryTag()
    {
        var config = CreateConfigService(autoAccept: true);
        var tcs = new TaskCompletionSource<(List<GitHubIssue>, string?)>();
        var github = new BlockingGithubService(tcs.Task);
        var planReader = new FakePlanReaderService();
        var jobService = new TestJobService();
        var logger = NullLogger<AssignedIssuesAutoImportService>.Instance;
        var queryService = new FakeQueryService();

        var service = new AssignedIssuesAutoImportService(config, github, planReader, jobService, logger, queryService);

        var firstSync = service.RunSyncAsync();

        await service.RunSyncAsync();

        Assert.Empty(queryService.InvalidatedTags);

        tcs.SetResult(([], null));
        await firstSync;

        Assert.Single(queryService.InvalidatedTags);
    }

    private sealed class FakeQueryService : IQueryService
    {
        public List<object> InvalidatedTags { get; } = [];

        public void InvalidateByTag(object tag) => InvalidatedTags.Add(tag);
        public void RevalidateByTag(object tag) { }
        public void Invalidate(Func<object, bool> predicate) { }
        public void Revalidate(Func<object, bool> predicate) { }
        public void Clear() { }
    }

    private sealed class BlockingGithubService(Task<(List<GitHubIssue>, string?)> waitTask) : IGithubService
    {
        public List<RepoConfig> GetRepos() => [];
        public RepoConfig? GetRepoConfigFromPathCached(string repoPath) => null;
        public ProjectConfig? FindProjectForGithubRepo(string ownerRepo) => null;
        public IReadOnlyList<string> GetResolvedGithubRepos(ProjectConfig project) => [];
        public Task<(List<string> assignees, string? error)> GetAssigneesAsync(string owner, string repo) =>
            Task.FromResult((new List<string>(), (string?)null));
        public Task<(List<string> labels, string? error)> GetLabelsAsync(string owner, string repo) =>
            Task.FromResult((new List<string>(), (string?)null));
        public Task<(Dictionary<string, PrInfo> statuses, string? error)> GetPrStatusesAsync(string owner, string repo) =>
            Task.FromResult((new Dictionary<string, PrInfo>(), (string?)null));
        public Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request) =>
            Task.FromResult((new List<GitHubIssue>(), (string?)null));
        public async Task<(List<GitHubIssue> issues, string? error)> GetMyAssignedIssuesAsync() =>
            await waitTask;
        public Task<(List<GitHubReviewItem> prs, string? error)> GetReviewRequestsAsync() =>
            Task.FromResult((new List<GitHubReviewItem>(), (string?)null));
    }

    private sealed class TestGithubService : IGithubService
    {
        public List<GitHubIssue> IssuesToReturn { get; set; } = [];
        public string? ErrorToReturn { get; set; }
        public int CallCount { get; private set; }
        public Dictionary<string, ProjectConfig> ProjectMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RepoConfig> GetRepos() => [];
        public RepoConfig? GetRepoConfigFromPathCached(string repoPath) => null;
        public ProjectConfig? FindProjectForGithubRepo(string ownerRepo) =>
            ProjectMap.TryGetValue(ownerRepo, out var proj) ? proj : null;

        public IReadOnlyList<string> GetResolvedGithubRepos(ProjectConfig project) => [];
        public Task<(List<string> assignees, string? error)> GetAssigneesAsync(string owner, string repo) =>
            Task.FromResult((new List<string>(), (string?)null));
        public Task<(List<string> labels, string? error)> GetLabelsAsync(string owner, string repo) =>
            Task.FromResult((new List<string>(), (string?)null));
        public Task<(Dictionary<string, PrInfo> statuses, string? error)> GetPrStatusesAsync(string owner, string repo) =>
            Task.FromResult((new Dictionary<string, PrInfo>(), (string?)null));
        public Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request) =>
            Task.FromResult((new List<GitHubIssue>(), (string?)null));
        public Task<(List<GitHubIssue> issues, string? error)> GetMyAssignedIssuesAsync()
        {
            CallCount++;
            return Task.FromResult((IssuesToReturn, ErrorToReturn));
        }
        public Task<(List<GitHubReviewItem> prs, string? error)> GetReviewRequestsAsync() =>
            Task.FromResult((new List<GitHubReviewItem>(), (string?)null));
    }

    private sealed class TestJobService : IJobService
    {
        public List<JobItem> Jobs { get; set; } = [];

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
        public event Action<JobItem>? JobFinished;
#pragma warning restore CS0067

        public string StartJob(JobArgsBase args, string? inboxFilePath = null) => "job-id";
        public void ForceStartJob(string id) { }
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public int StopAllJobs() => 0;
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public int StopQueuedJobs() => 0;
        public List<JobItem> GetJobs() => Jobs;
        public List<JobItem> GetJobsForPlan(string planFile) => [];
        public JobItem? GetJob(string id) => null;
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => true;
        public void SetChatSessionId(string id, string chatSessionId) { }
        public bool ReportJobFailure(string id, string message) => true;
        public bool IsInboxFileTracked(string filePath) => false;
        public void Dispose() { }
    }
}
