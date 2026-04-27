using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ivy.Tendril.Test;

public class GitServiceTests
{
    private GitService CreateGitService()
    {
        var config = new TestConfigService();
        var logger = new TestLogger();
        return new GitService(config, logger);
    }

    private class TestLogger : ILogger<GitService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private class TestConfigService : IConfigService
    {
        public TendrilSettings Settings => new() { GitTimeout = 5, Projects = [] };
        public string TendrilHome => "";
        public string ConfigPath => "";
        public string PlanFolder => "";
        public List<ProjectConfig> Projects => [];
        public List<LevelConfig> Levels => [];
        public string[] LevelNames => [];
        public EditorConfig Editor => new();
        public bool NeedsOnboarding => false;
        public ConfigParseError? ParseError => null;
        public event EventHandler? SettingsReloaded;

        public ProjectConfig? GetProject(string name) => null;
        public bool TryAutoHeal() => false;
        public void ResetToDefaults() { }
        public void RetryLoadConfig() { }
        public BadgeVariant GetBadgeVariant(string level) => BadgeVariant.Info;
        public Colors? GetProjectColor(string projectName) => null;
        public void SaveSettings() { }
        public void ReloadSettings() { }
        public void SetPendingTendrilHome(string path) { }
        public string? GetPendingTendrilHome() => null;
        public void SetPendingProject(ProjectConfig project) { }
        public ProjectConfig? GetPendingProject() => null;
        public void SetPendingCodingAgent(string name) { }
        public string? GetPendingCodingAgent() => null;
        public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions) { }
        public List<VerificationConfig>? GetPendingVerificationDefinitions() => null;
        public void CompleteOnboarding(string tendrilHome) { }
        public void OpenInEditor(string path) { }
        public string PreprocessForEditing(string path) => path;
    }

    [Fact]
    public void GetCommitTitle_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = CreateGitService();
        var invalidRepoPath = "D:\\NonExistent\\Repo\\Path";

        // Act
        var result = gitService.GetCommitTitle(invalidRepoPath, "abc123");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
        Assert.Contains("Repository path does not exist", result.ErrorMessage);
    }

    [Fact]
    public void GetCommitDiff_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = CreateGitService();
        var invalidRepoPath = "D:\\NonExistent\\Repo\\Path";

        // Act
        var result = gitService.GetCommitDiff(invalidRepoPath, "abc123");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
        Assert.Contains("Repository path does not exist", result.ErrorMessage);
    }

    [Fact]
    public void GetCommitFileCount_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = CreateGitService();
        var invalidRepoPath = "D:\\NonExistent\\Repo\\Path";

        // Act
        var result = gitService.GetCommitFileCount(invalidRepoPath, "abc123");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
        Assert.Contains("Repository path does not exist", result.ErrorMessage);
    }

    [Fact]
    public void GetCommitFiles_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = CreateGitService();
        var invalidRepoPath = "D:\\NonExistent\\Repo\\Path";

        // Act
        var result = gitService.GetCommitFiles(invalidRepoPath, "abc123");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
        Assert.Contains("Repository path does not exist", result.ErrorMessage);
    }

    [Fact]
    public void GetCombinedDiff_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = CreateGitService();
        var invalidRepoPath = "D:\\NonExistent\\Repo\\Path";

        // Act
        var result = gitService.GetCombinedDiff(invalidRepoPath, "abc123", "def456");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
        Assert.Contains("Repository path does not exist", result.ErrorMessage);
    }

    [Fact]
    public void GetCombinedChangedFiles_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = CreateGitService();
        var invalidRepoPath = "D:\\NonExistent\\Repo\\Path";

        // Act
        var result = gitService.GetCombinedChangedFiles(invalidRepoPath, "abc123", "def456");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
        Assert.Contains("Repository path does not exist", result.ErrorMessage);
    }

    [Fact]
    public void GetWorktrees_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = CreateGitService();
        var invalidRepoPath = "D:\\NonExistent\\Repo\\Path";

        // Act
        var result = gitService.GetWorktrees(invalidRepoPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
        Assert.Contains("Repository path does not exist", result.ErrorMessage);
    }

    [Fact]
    public void GetCommitSummaries_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = CreateGitService();
        var invalidRepoPath = "D:\\NonExistent\\Repo\\Path";
        var commits = new[] { "abc123", "def456" };

        // Act
        var result = gitService.GetCommitSummaries(invalidRepoPath, commits);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
        Assert.Contains("Repository path does not exist", result.ErrorMessage);
    }

    [Fact]
    public void GetCommitSummaries_WithEmptyCommits_ReturnsEmptyDictionary()
    {
        // Arrange
        var gitService = CreateGitService();
        var validRepoPath = System.IO.Path.GetTempPath(); // Use temp path as a valid directory

        // Act
        var result = gitService.GetCommitSummaries(validRepoPath, Array.Empty<string>());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }
}
