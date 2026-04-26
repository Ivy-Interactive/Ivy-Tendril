using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Ivy.Tendril.Test;

public class GitServiceTests
{
    private readonly ILogger<GitService> _logger;
    private readonly IConfigService _config;

    public GitServiceTests()
    {
        _logger = Substitute.For<ILogger<GitService>>();
        _config = Substitute.For<IConfigService>();
        _config.Settings.Returns(new SettingsFile
        {
            GitTimeout = 5 // 5 seconds timeout
        });
    }

    [Fact]
    public void GetCommitTitle_WithInvalidRepo_ReturnsInvalidRepoPathError()
    {
        // Arrange
        var gitService = new GitService(_config, _logger);
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
        var gitService = new GitService(_config, _logger);
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
        var gitService = new GitService(_config, _logger);
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
        var gitService = new GitService(_config, _logger);
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
        var gitService = new GitService(_config, _logger);
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
        var gitService = new GitService(_config, _logger);
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
        var gitService = new GitService(_config, _logger);
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
        var gitService = new GitService(_config, _logger);
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
        var gitService = new GitService(_config, _logger);
        var validRepoPath = System.IO.Path.GetTempPath(); // Use temp path as a valid directory

        // Act
        var result = gitService.GetCommitSummaries(validRepoPath, Array.Empty<string>());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }
}
