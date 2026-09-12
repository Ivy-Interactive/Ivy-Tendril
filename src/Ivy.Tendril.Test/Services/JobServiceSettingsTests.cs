using System.Diagnostics;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test.Services;

public class JobServiceSettingsTests
{
    private static string CreateTempConfigFile(string yamlContent)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-jobservice-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "Inbox"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Plans"));
        File.WriteAllText(Path.Combine(tempDir, "config.yaml"), yamlContent);
        return tempDir;
    }

    [Fact]
    public void JobService_UpdatesTimeoutsOnConfigReload()
    {
        var yaml = @"
jobTimeout: 30
staleOutputTimeout: 10
maxConcurrentJobs: 5
";
        var tempDir = CreateTempConfigFile(yaml);
        var config = new ConfigService(new TendrilSettings());
        config.SetTendrilHome(tempDir);

        try
        {
            var jobService = new JobService(config);

            // Verify initial values
            Assert.Equal(TimeSpan.FromMinutes(30), jobService.JobTimeout);
            Assert.Equal(TimeSpan.FromMinutes(10), jobService.StaleOutputTimeout);

            File.WriteAllText(Path.Combine(tempDir, "config.yaml"), @"
jobTimeout: 60
staleOutputTimeout: 20
maxConcurrentJobs: 8
");
            config.ReloadSettings();

            Assert.Equal(60, config.Settings.JobTimeout);
            Assert.Equal(20, config.Settings.StaleOutputTimeout);
            Assert.Equal(8, config.Settings.MaxConcurrentJobs);

            // Verify JobService's internal values are updated
            Assert.Equal(TimeSpan.FromMinutes(60), jobService.JobTimeout);
            Assert.Equal(TimeSpan.FromMinutes(20), jobService.StaleOutputTimeout);

            jobService.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void JobService_UnsubscribesOnDispose()
    {
        var yaml = @"
jobTimeout: 30
staleOutputTimeout: 10
maxConcurrentJobs: 5
";
        var tempDir = CreateTempConfigFile(yaml);
        var config = new ConfigService(new TendrilSettings());
        config.SetTendrilHome(tempDir);

        try
        {
            var jobService = new JobService(config);
            jobService.Dispose();

            File.WriteAllText(Path.Combine(tempDir, "config.yaml"), @"
jobTimeout: 99
staleOutputTimeout: 99
maxConcurrentJobs: 99
");
            config.ReloadSettings();

            Assert.Equal(99, config.Settings.JobTimeout);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveSettings_TriggersJobServiceReload()
    {
        var yaml = @"
jobTimeout: 30
staleOutputTimeout: 10
maxConcurrentJobs: 5
";
        var tempDir = CreateTempConfigFile(yaml);
        var config = new ConfigService(new TendrilSettings());
        config.SetTendrilHome(tempDir);

        try
        {
            var jobService = new JobService(config);

            config.Settings.JobTimeout = 45;
            config.Settings.StaleOutputTimeout = 15;
            config.SaveSettings();

            Assert.Equal(45, config.Settings.JobTimeout);
            Assert.Equal(15, config.Settings.StaleOutputTimeout);

            jobService.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    ///     Starting a plan job flushes queued plan.yaml writes first, so the agent reads what the user
    ///     just changed. That wait has to be bounded: a queued write can be parked on the cross-process
    ///     plan lock behind a CLI process, and waiting for it froze whichever thread started the job —
    ///     the UI thread, when the user clicks Execute (#2571).
    /// </summary>
    [Fact]
    public void StartJob_WhenPlanWritesNeverFlush_ReturnsWithinTheFlushTimeout()
    {
        var tempDir = CreateTempConfigFile("jobTimeout: 30\n");
        var planFolder = Path.Combine(tempDir, "Plans", "00001-TestPlan");
        Directory.CreateDirectory(planFolder);

        var entries = new List<(LogLevel Level, string Message)>();
        var planReader = new FakePlanReaderService { FlushTask = new TaskCompletionSource().Task };
        // maxConcurrentJobs 0: the job reaches the flush and then parks in the queue, instead of
        // launching an agent process.
        var jobService = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), null, 0,
            planReaderService: planReader,
            logger: new CapturingLogger<JobService>(entries))
        {
            PlanWriteFlushTimeout = TimeSpan.FromMilliseconds(200)
        };

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var jobId = jobService.StartJob(new ExecutePlanArgs(planFolder));
            stopwatch.Stop();

            Assert.Equal(JobStatus.Queued, jobService.GetJob(jobId)?.Status);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                $"StartJob waited {stopwatch.ElapsedMilliseconds}ms on a write queue that never drains");
            Assert.Contains(entries, e => e.Level == LogLevel.Warning && e.Message.Contains("did not flush"));
        }
        finally
        {
            jobService.Dispose();
            Directory.Delete(tempDir, true);
        }
    }
}
