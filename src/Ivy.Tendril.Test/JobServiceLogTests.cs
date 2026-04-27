using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class JobServiceLogTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }
    [Fact]
    public void WriteJobLog_IncludesSessionId()
    {
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
            var planReaderService = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
            var jobService = new JobService(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
                planReaderService: planReaderService);

            var sessionId = Guid.NewGuid().ToString();
            var job = new JobItem
            {
                Id = "1",
                Type = "ExecutePlan",
                PlanFile = "00001-TestPlan",
                Status = JobStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-2),
                CompletedAt = DateTime.UtcNow,
                DurationSeconds = 120,
                SessionId = sessionId
            };

            jobService.WriteJobLog(job);

        var logsDir = Path.Combine(_tempDir.Path, "Plans", "00001-TestPlan", "logs");
            var logFiles = Directory.GetFiles(logsDir, "*.md");
            Assert.Single(logFiles);

            var logContent = File.ReadAllText(logFiles[0]);
            Assert.Contains("**SessionId:**", logContent);
            Assert.Contains(sessionId, logContent);
    }

    [Fact]
    public void WriteJobLog_OmitsSessionIdWhenNull()
    {
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
            var planReaderService = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
            var jobService = new JobService(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
                planReaderService: planReaderService);

            var job = new JobItem
            {
                Id = "2",
                Type = "ExecutePlan",
                PlanFile = "00002-TestPlan",
                Status = JobStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-1),
                CompletedAt = DateTime.UtcNow,
                DurationSeconds = 60,
                SessionId = null
            };

            jobService.WriteJobLog(job);

            var logsDir = Path.Combine(tempDir, "Plans", "00002-TestPlan", "logs");
            var logFiles = Directory.GetFiles(logsDir, "*.md");
            Assert.Single(logFiles);

            var logContent = File.ReadAllText(logFiles[0]);
            Assert.DoesNotContain("**SessionId:**", logContent);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}