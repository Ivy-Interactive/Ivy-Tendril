using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test;

public class WorktreeValidationTests : IDisposable
{
    private readonly string _tempDir;

    public WorktreeValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wt-validation-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void IsWorktree_Returns_True_For_Worktree_Directory()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, ".git"),
            "gitdir: /path/to/repo/.git/worktrees/name");

        Assert.True(WorktreeValidationHelper.IsWorktree(_tempDir));
    }

    [Fact]
    public void IsWorktree_Returns_False_For_Main_Repository()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        Assert.False(WorktreeValidationHelper.IsWorktree(_tempDir));
    }

    [Fact]
    public void IsWorktree_Returns_False_For_Non_Git_Directory()
    {
        Assert.False(WorktreeValidationHelper.IsWorktree(_tempDir));
    }

    [Fact]
    public void IsWorktree_Returns_False_For_Nonexistent_Directory()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");

        Assert.False(WorktreeValidationHelper.IsWorktree(nonExistent));
    }

    [Fact]
    public void ConfigService_ValidateRepoPathsAreNotWorktrees_Logs_Error_For_Worktree_Repo()
    {
        var fakeRepoDir = Path.Combine(_tempDir, "fake-repo");
        Directory.CreateDirectory(fakeRepoDir);
        File.WriteAllText(
            Path.Combine(fakeRepoDir, ".git"),
            "gitdir: /somewhere/.git/worktrees/fake-repo");

        var yaml = $@"
projects:
  - name: TestProject
    repos:
      - path: {fakeRepoDir.Replace("\\", "\\\\")}
";

        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "config.yaml"), yaml);

        var testLogger = new TestLogger<ConfigService>();
        var service = new ConfigService(logger: testLogger);
        service.SetTendrilHome(configDir);

        service.ValidateRepoPathsAreNotWorktrees();

        var output = testLogger.GetOutput();
        Assert.Contains("CRITICAL", output);
        Assert.Contains("worktree", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigService_ValidateRepoPathsAreNotWorktrees_NoError_For_Normal_Repo()
    {
        var fakeRepoDir = Path.Combine(_tempDir, "normal-repo");
        Directory.CreateDirectory(fakeRepoDir);
        Directory.CreateDirectory(Path.Combine(fakeRepoDir, ".git"));

        var yaml = $@"
projects:
  - name: TestProject
    repos:
      - path: {fakeRepoDir.Replace("\\", "\\\\")}
";

        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "config.yaml"), yaml);

        var testLogger = new TestLogger<ConfigService>();
        var service = new ConfigService(logger: testLogger);
        service.SetTendrilHome(configDir);

        service.ValidateRepoPathsAreNotWorktrees();

        var output = testLogger.GetOutput();
        Assert.DoesNotContain("CRITICAL", output);
    }

    private class TestLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public string GetOutput() => string.Join(Environment.NewLine, _messages);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
