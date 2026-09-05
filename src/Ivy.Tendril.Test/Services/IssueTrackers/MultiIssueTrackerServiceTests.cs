using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.IssueTrackers;
using Ivy.Tendril.Services.IssueTrackers.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services.IssueTrackers;

public class MultiIssueTrackerServiceTests
{
    private readonly NullLogger<IssueTrackerService> _logger = NullLogger<IssueTrackerService>.Instance;

    [Fact]
    public async Task GetMyAssignedIssuesAsync_WithMultipleConnections_AggregatesFromAllConnections()
    {
        var conn1 = new TrackerConnectionConfig
        {
            Id = "jira-work",
            Name = "Work Jira",
            Provider = "jira",
            Url = "https://work.atlassian.net",
            Email = "user@work.com",
            ApiToken = "token1"
        };
        var conn2 = new TrackerConnectionConfig
        {
            Id = "jira-personal",
            Name = "Personal Jira",
            Provider = "jira",
            Url = "https://personal.atlassian.net",
            Email = "user@personal.com",
            ApiToken = "token2"
        };

        var configService = new TestConfigService();
        configService.Settings.TrackerConnections.Add(conn1);
        configService.Settings.TrackerConnections.Add(conn2);

        var issue1 = new TrackerIssue(
            "jira:WORK-1", "WORK-1", "Work Issue", null, [], [], "WORK", null, "jira",
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var issue2 = new TrackerIssue(
            "jira:PERS-1", "PERS-1", "Personal Issue", null, [], [], "PERS", null, "jira",
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var multiJiraProvider = new MultiConnectionStubProvider("jira", "Jira", true, (conn) =>
        {
            if (conn?.Id == "jira-work") return [issue1];
            if (conn?.Id == "jira-personal") return [issue2];
            return [];
        });

        var service = new IssueTrackerService(
            [multiJiraProvider],
            [],
            configService,
            _logger);

        var (issues, errors) = await service.GetMyAssignedIssuesAsync();

        Assert.Empty(errors);
        Assert.Equal(2, issues.Count);
        // More recent first
        Assert.Equal("PERS-1", issues[0].Key);
        Assert.Equal("WORK-1", issues[1].Key);
    }

    [Fact]
    public async Task GetMyAssignedIssuesAsync_DeduplicatesById()
    {
        var conn1 = new TrackerConnectionConfig
        {
            Id = "conn-1",
            Name = "Conn 1",
            Provider = "jira"
        };
        var conn2 = new TrackerConnectionConfig
        {
            Id = "conn-2",
            Name = "Conn 2",
            Provider = "jira"
        };

        var configService = new TestConfigService();
        configService.Settings.TrackerConnections.Add(conn1);
        configService.Settings.TrackerConnections.Add(conn2);

        var duplicateIssue = new TrackerIssue(
            "jira:SAME-1", "SAME-1", "Duplicate Key Issue", null, [], [], "SAME", null, "jira",
            UpdatedAt: DateTimeOffset.UtcNow);

        var multiJiraProvider = new MultiConnectionStubProvider("jira", "Jira", true, _ => [duplicateIssue]);

        var service = new IssueTrackerService(
            [multiJiraProvider],
            [],
            configService,
            _logger);

        var (issues, errors) = await service.GetMyAssignedIssuesAsync();

        Assert.Empty(errors);
        Assert.Single(issues);
        Assert.Equal("SAME-1", issues[0].Key);
    }

    [Fact]
    public async Task GetProjectIssuesAsync_WithMultipleTrackers_AggregatesFromAllTrackers()
    {
        var jiraIssue = new TrackerIssue(
            "jira:PROJ-10", "PROJ-10", "Jira Task", null, [], [], "PROJ", null, "jira",
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var linearIssue = new TrackerIssue(
            "linear:LIN-20", "LIN-20", "Linear Task", null, [], [], "LIN", null, "linear",
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var jiraProvider = new MultiConnectionStubProvider("jira", "Jira", true,
            onProjectTracker: (_, t) => t.ProjectKey == "PROJ" ? [jiraIssue] : []);

        var linearProvider = new MultiConnectionStubProvider("linear", "Linear", true,
            onProjectTracker: (_, t) => t.TeamKey == "ENG" ? [linearIssue] : []);

        var configService = new TestConfigService();
        var service = new IssueTrackerService(
            [jiraProvider, linearProvider],
            [],
            configService,
            _logger);

        var project = new ProjectConfig
        {
            Name = "MultiTrackerProject",
            IssueTrackers =
            [
                new ProjectTrackerConfig { Id = "t1", Provider = "jira", ProjectKey = "PROJ" },
                new ProjectTrackerConfig { Id = "t2", Provider = "linear", TeamKey = "ENG" }
            ]
        };

        var (issues, errors) = await service.GetProjectIssuesAsync(project, new TrackerIssueQuery());

        Assert.Empty(errors);
        Assert.Equal(2, issues.Count);
        Assert.Equal("LIN-20", issues[0].Key);
        Assert.Equal("PROJ-10", issues[1].Key);
    }

    [Fact]
    public async Task GetProjectIssuesAsync_BackwardCompatibility_FallsBackToSingleTracker()
    {
        var legacyIssue = new TrackerIssue(
            "jira:LEG-1", "LEG-1", "Legacy Issue", null, [], [], "LEG", null, "jira",
            UpdatedAt: DateTimeOffset.UtcNow);

        var jiraProvider = new MultiConnectionStubProvider("jira", "Jira", true,
            onProjectTracker: (_, t) => t.ProjectKey == "LEG" ? [legacyIssue] : []);

        var configService = new TestConfigService();
        var service = new IssueTrackerService(
            [jiraProvider],
            [],
            configService,
            _logger);

        var project = new ProjectConfig
        {
            Name = "LegacyProject",
            IssueTracker = new ProjectTrackerConfig { Provider = "jira", ProjectKey = "LEG" }
            // IssueTrackers list is empty
        };

        var (issues, errors) = await service.GetProjectIssuesAsync(project, new TrackerIssueQuery());

        Assert.Empty(errors);
        Assert.Single(issues);
        Assert.Equal("LEG-1", issues[0].Key);
    }

    [Fact]
    public void ConfigService_MigrateIssueTrackers_MigratesLegacySettingsAndProject()
    {
        var settings = new TendrilSettings();
        // Setup legacy issue trackers in settings
        settings.IssueTrackers = new IssueTrackerSettings
        {
            Jira = new JiraTrackerConfig
            {
                Url = "https://legacy-jira.atlassian.net",
                Email = "legacy@jira.com",
                ApiToken = "legacy-token"
            },
            Linear = new LinearTrackerConfig
            {
                ApiKey = "legacy-linear-key"
            }
        };

        // Setup legacy project tracker
        var project = new ProjectConfig
        {
            Name = "Proj1",
            IssueTracker = new ProjectTrackerConfig
            {
                Provider = "jira",
                ProjectKey = "PROJ"
            }
        };
        settings.Projects.Add(project);

        // Run migration
        ConfigService.MigrateIssueTrackers(settings);

        // Verify global connections
        Assert.Equal(2, settings.TrackerConnections.Count);
        var jiraConn = settings.TrackerConnections.FirstOrDefault(c => c.Provider == "jira");
        Assert.NotNull(jiraConn);
        Assert.Equal("https://legacy-jira.atlassian.net", jiraConn.Url);
        Assert.Equal("legacy@jira.com", jiraConn.Email);
        Assert.Equal("legacy-token", jiraConn.ApiToken);

        var linearConn = settings.TrackerConnections.FirstOrDefault(c => c.Provider == "linear");
        Assert.NotNull(linearConn);
        Assert.Equal("legacy-linear-key", linearConn.ApiKey);

        // Verify project tracker migration
        Assert.Single(project.IssueTrackers);
        Assert.Equal("jira", project.IssueTrackers[0].Provider);
        Assert.Equal("PROJ", project.IssueTrackers[0].ProjectKey);
        Assert.Equal(jiraConn.Id, project.IssueTrackers[0].ConnectionId);
        Assert.NotEmpty(project.IssueTrackers[0].Id);

        // Ensure running migration again is idempotent
        ConfigService.MigrateIssueTrackers(settings);
        Assert.Equal(2, settings.TrackerConnections.Count);
        Assert.Single(project.IssueTrackers);
    }

    private sealed class MultiConnectionStubProvider(
        string providerId,
        string displayName,
        bool isConfigured,
        Func<TrackerConnectionConfig?, IReadOnlyList<TrackerIssue>>? onAssigned = null,
        Func<ProjectConfig, ProjectTrackerConfig, IReadOnlyList<TrackerIssue>>? onProjectTracker = null) : IIssueTrackerProvider
    {
        public string ProviderId => providerId;
        public string DisplayName => displayName;
        public Icons Icon => Icons.CircleDot;

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(isConfigured);

        public Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(CancellationToken ct = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<TrackerIssue>>.Success(onAssigned?.Invoke(null) ?? []));

        public Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(
            TrackerConnectionConfig? connection, CancellationToken ct = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<TrackerIssue>>.Success(onAssigned?.Invoke(connection) ?? []));

        public Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesAsync(
            ProjectConfig project, TrackerIssueQuery query, CancellationToken ct = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<TrackerIssue>>.Success([]));

        public Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesForTrackerAsync(
            ProjectConfig project, ProjectTrackerConfig tracker, TrackerIssueQuery query, CancellationToken ct = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<TrackerIssue>>.Success(onProjectTracker?.Invoke(project, tracker) ?? []));
    }

    private sealed class TestConfigService : IConfigService
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
