using Ivy.Tendril.Services;
using System.Diagnostics;

namespace Ivy.Tendril.Test;

public class JobServiceEnvironmentTests
{
    [Fact]
    public async Task StartJob_SetsCIAndTERMEnvironmentVariables()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-env-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "Inbox"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Plans"));

        var yaml = @"
jobTimeout: 5
staleOutputTimeout: 3
maxConcurrentJobs: 1
";
        File.WriteAllText(Path.Combine(tempDir, "config.yaml"), yaml);

        var config = new ConfigService(new TendrilSettings());
        config.SetTendrilHome(tempDir);

        try
        {
            var service = new JobService(config);

            // Create a test script that echoes environment variables
            var testScript = Path.Combine(tempDir, "test-env.ps1");
            var scriptContent = @"
Write-Output ""CI=$env:CI""
Write-Output ""TERM=$env:TERM""
exit 0
";
            File.WriteAllText(testScript, scriptContent);

            // Start a job that runs the test script
            var jobId = service.StartJob("TestEnvJob", "pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", testScript });

            // Wait for job completion
            await Task.Delay(2000);

            var job = service.GetJob(jobId);
            Assert.NotNull(job);

            var output = job.GetOutputSnapshot().ToList();
            var combinedOutput = string.Join("\n", output);

            // Verify CI and TERM are set
            Assert.Contains("CI=true", combinedOutput);
            Assert.Contains("TERM=dumb", combinedOutput);

            service.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
