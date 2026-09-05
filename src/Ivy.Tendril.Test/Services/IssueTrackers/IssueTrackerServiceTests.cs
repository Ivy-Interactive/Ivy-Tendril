using Ivy;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.IssueTrackers;
using Ivy.Tendril.Services.IssueTrackers.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services.IssueTrackers;

public class IssueTrackerServiceTests
{
    private readonly NullLogger<IssueTrackerService> _logger = NullLogger<IssueTrackerService>.Instance;

    [Fact]
    public async Task GetMyAssignedIssuesAsync_AggregatesFromAllConfiguredProviders()
    {
        var githubIssue = new TrackerIssue(
            "github:owner/repo#1", "#1", "GitHub Issue", null, ["bug"], ["alice"], "owner/repo", null, "github",
            UpdatedAt: DateTimeOffset.UtcNow.AddHours(-2));

        var jiraIssue = new TrackerIssue(
            "jira:PROJ-42", "PROJ-42", "Jira Issue", null, ["frontend"], ["alice"], "PROJ", null, "jira",
            UpdatedAt: DateTimeOffset.UtcNow.AddHours(-1));

        var ghProvider = new StubIssueProvider("github", "GitHub", isConfigured: true, [githubIssue]);
        var jiraProvider = new StubIssueProvider("jira", "Jira", isConfigured: true, [jiraIssue]);
        var unconfiguredProvider = new StubIssueProvider("linear", "Linear", isConfigured: false, []);

        var service = new IssueTrackerService(
            [ghProvider, jiraProvider, unconfiguredProvider],
            [],
            new StubConfigService(),
            _logger);

        var (issues, errors) = await service.GetMyAssignedIssuesAsync();

        Assert.Empty(errors);
        Assert.Equal(2, issues.Count);
        // Jira issue is more recent, should be first
        Assert.Equal("PROJ-42", issues[0].Key);
        Assert.Equal("#1", issues[1].Key);
    }

    [Fact]
    public async Task GetMyAssignedIssuesAsync_CollectsErrorsFromFailingProvidersWhileReturningSuccessfulIssues()
    {
        var githubIssue = new TrackerIssue(
            "github:owner/repo#1", "#1", "GitHub Issue", null, [], ["alice"], "owner/repo", null, "github");

        var ghProvider = new StubIssueProvider("github", "GitHub", isConfigured: true, [githubIssue]);
        var failingProvider = new StubIssueProvider("jira", "Jira", isConfigured: true, [], errorToReturn: "401 Unauthorized");

        var service = new IssueTrackerService(
            [ghProvider, failingProvider],
            [],
            new StubConfigService(),
            _logger);

        var (issues, errors) = await service.GetMyAssignedIssuesAsync();

        Assert.Single(issues);
        Assert.Equal("#1", issues[0].Key);
        Assert.Single(errors);
        Assert.Contains("401 Unauthorized", errors[0]);
    }

    [Fact]
    public async Task GetReviewRequestsAsync_AggregatesReviewsAcrossReviewProviders()
    {
        var review = new TrackerReviewItem(
            "github:owner/repo#100", "#100", "Fix security bug", null, [], ["alice"], "owner/repo",
            "https://github.com/owner/repo/pull/100", "fix/security", "github",
            UpdatedAt: DateTimeOffset.UtcNow);

        var ghProvider = new StubReviewProvider("github", "GitHub", isConfigured: true, [review]);

        var service = new IssueTrackerService(
            [],
            [ghProvider],
            new StubConfigService(),
            _logger);

        var (reviews, errors) = await service.GetReviewRequestsAsync();

        Assert.Empty(errors);
        Assert.Single(reviews);
        Assert.Equal("#100", reviews[0].Key);
    }

    [Fact]
    public async Task GetProjectIssuesAsync_RoutesToConfiguredTrackerProvider()
    {
        var jiraIssue = new TrackerIssue(
            "jira:MOBILE-1", "MOBILE-1", "Crash on launch", null, [], [], "MOBILE", null, "jira");

        var jiraProvider = new StubIssueProvider("jira", "Jira", isConfigured: true, [jiraIssue]);
        var ghProvider = new StubIssueProvider("github", "GitHub", isConfigured: true, []);

        var service = new IssueTrackerService(
            [ghProvider, jiraProvider],
            [],
            new StubConfigService(),
            _logger);

        var project = new ProjectConfig
        {
            Name = "MobileApp",
            IssueTracker = new ProjectTrackerConfig { Provider = "jira", ProjectKey = "MOBILE" }
        };

        var (issues, errors) = await service.GetProjectIssuesAsync(project, new TrackerIssueQuery());

        Assert.Empty(errors);
        Assert.Single(issues);
        Assert.Equal("MOBILE-1", issues[0].Key);
    }

    [Fact]
    public async Task GetProjectIssuesAsync_DefaultsToGitHubWhenNoProviderSpecified()
    {
        var ghIssue = new TrackerIssue(
            "github:owner/repo#5", "#5", "Default GitHub Issue", null, [], [], "owner/repo", null, "github");

        var ghProvider = new StubIssueProvider("github", "GitHub", isConfigured: true, [ghIssue]);

        var service = new IssueTrackerService(
            [ghProvider],
            [],
            new StubConfigService(),
            _logger);

        var project = new ProjectConfig
        {
            Name = "DefaultProject"
        };

        var (issues, errors) = await service.GetProjectIssuesAsync(project, new TrackerIssueQuery());

        Assert.Empty(errors);
        Assert.Single(issues);
        Assert.Equal("#5", issues[0].Key);
    }

    private sealed class StubIssueProvider(
        string providerId,
        string displayName,
        bool isConfigured,
        IReadOnlyList<TrackerIssue> issues,
        string? errorToReturn = null) : IIssueTrackerProvider
    {
        public string ProviderId => providerId;
        public string DisplayName => displayName;
        public Icons Icon => Icons.CircleDot;

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(isConfigured);

        public Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(CancellationToken ct = default) =>
            errorToReturn != null
                ? Task.FromResult(ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(errorToReturn, []))
                : Task.FromResult(ProviderResult<IReadOnlyList<TrackerIssue>>.Success(issues));

        public Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesAsync(
            ProjectConfig project, TrackerIssueQuery query, CancellationToken ct = default) =>
            errorToReturn != null
                ? Task.FromResult(ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(errorToReturn, []))
                : Task.FromResult(ProviderResult<IReadOnlyList<TrackerIssue>>.Success(issues));
    }

    private sealed class StubReviewProvider(
        string providerId,
        string displayName,
        bool isConfigured,
        IReadOnlyList<TrackerReviewItem> reviews) : IReviewTrackerProvider
    {
        public string ProviderId => providerId;
        public string DisplayName => displayName;
        public Icons Icon => Icons.GitPullRequest;

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(isConfigured);

        public Task<ProviderResult<IReadOnlyList<TrackerReviewItem>>> GetReviewRequestsAsync(CancellationToken ct = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<TrackerReviewItem>>.Success(reviews));
    }

    private sealed class StubConfigService : IConfigService
    {
        public TendrilSettings Settings { get; set; } = new();
        public string TendrilHome => "";
        public string ConfigPath => "";
        public string PlanFolder => "";
        public List<ProjectConfig> Projects => Settings.Projects;
        public List<LevelConfig> Levels => Settings.Levels;
        public string[] LevelNames => [];
        public EditorConfig Editor => Settings.Editor;
        public bool NeedsOnboarding => false;
        public ConfigParseError? ParseError => null;
#pragma warning disable CS0067
        public event EventHandler? SettingsReloaded;
#pragma warning restore CS0067

        public ProjectConfig? GetProject(string name) => Settings.Projects.FirstOrDefault(p => p.Name == name);
        public bool TryAutoHeal() => false;
        public void ResetToDefaults() {}
        public void RetryLoadConfig() {}
        public Colors? GetLevelColor(string level) => null;
        public Colors? GetProjectColor(string projectName) => null;
        public void SaveSettings() {}
        public void MutateAndSave(Action<TendrilSettings> mutate) => mutate(Settings);
        public void ReloadSettings() {}
        public void SetPendingTendrilHome(string path) {}
        public string? GetPendingTendrilHome() => null;
        public void SetPendingProject(ProjectConfig project) {}
        public ProjectConfig? GetPendingProject() => null;
        public void SetPendingCodingAgent(string name) {}
        public string? GetPendingCodingAgent() => null;
        public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions) {}
        public List<VerificationConfig>? GetPendingVerificationDefinitions() => null;
        public void CompleteOnboarding(string tendrilHome) {}
        public void OpenInEditor(string path) {}
        public string PolishMarkdown(string content) => content;
        public void Dispose() {}
    }
}
