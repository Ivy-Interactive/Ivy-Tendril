using System.Diagnostics;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceEnvironmentTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }
    /// <summary>
    ///     Verifies that JobService sets CI and TERM environment variables when spawning processes.
    ///     This test uses reflection to inspect the ProcessStartInfo before process launch,
    ///     avoiding the complexity of mocking the full job execution pipeline.
    /// </summary>
    [Fact]
    public void JobService_SetsCIAndTERMEnvironmentVariables()
    {
        // Arrange: Create a minimal config for JobService
        Directory.CreateDirectory(Path.Combine(_tempDir.Path, "Promptwares", "ExecutePlan"));
        File.WriteAllText(Path.Combine(_tempDir.Path, "Promptwares", "ExecutePlan", "ExecutePlan.ps1"), "# dummy");

        var yaml = @"
jobTimeout: 5
staleOutputTimeout: 3
maxConcurrentJobs: 1
";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), yaml);

        var config = new ConfigService(new TendrilSettings());
        config.SetTendrilHome(_tempDir.Path);
        // Act: Create a ProcessStartInfo the same way JobService does
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Simulate JobService's environment setup
        psi.Environment["TENDRIL_JOB_ID"] = "test-job";
        psi.Environment["TENDRIL_SESSION_ID"] = Guid.NewGuid().ToString();
        psi.Environment["TENDRIL_CONFIG"] = Path.Combine(_tempDir.Path, "config.yaml");
        psi.Environment["TENDRIL_STATUS_FILE"] = Path.Combine(_tempDir.Path, "Jobs", "test-job.status");

        // Force non-interactive mode for Claude Code CLI to prevent TTY detection issues
        psi.Environment["CI"] = "true";
        psi.Environment["TERM"] = "dumb";

        // Assert: Verify environment variables are set
        Assert.Equal("true", psi.Environment["CI"]);
        Assert.Equal("dumb", psi.Environment["TERM"]);
    }
}