using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
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
        var jobService = new JobService(configService, planReaderService: planReaderService);

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

        var logFile = Path.Combine(_tempDir.Path, "Jobs", "1-00001-ExecutePlan.md");
        Assert.True(File.Exists(logFile));

        var logContent = File.ReadAllText(logFile);
        Assert.Contains("**SessionId:**", logContent);
        Assert.Contains(sessionId, logContent);
    }

    [Fact]
    public void WriteJobLog_CreatePlanJob_RecordsThePlanIdItProduced()
    {
        // A CreatePlan job's filename carries no plan id, so its log header is the only place the link to
        // the plan it authored survives. BugReportService reads this line back.
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var planReaderService = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
        var jobService = new JobService(configService, planReaderService: planReaderService);

        var job = new JobItem
        {
            Id = "9",
            Type = "CreatePlan",
            PlanFile = "Add a logout button",
            AllocatedPlanId = "00075",
            Status = JobStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        jobService.WriteJobLog(job);

        var logFile = Path.Combine(_tempDir.Path, "Jobs", "9-CreatePlan.md");
        Assert.True(File.Exists(logFile), "the CreatePlan job log must not gain a plan-id segment");
        Assert.Contains("- **PlanId:** 00075", File.ReadAllText(logFile));
    }

    [Fact]
    public void WriteJobLog_OmitsSessionIdWhenNull()
    {
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var planReaderService = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
        var jobService = new JobService(configService, planReaderService: planReaderService);

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

        var logFile = Path.Combine(_tempDir.Path, "Jobs", "2-00002-ExecutePlan.md");
        Assert.True(File.Exists(logFile));

        var logContent = File.ReadAllText(logFile);
        Assert.DoesNotContain("**SessionId:**", logContent);
    }

    [Fact]
    public void WriteJobLog_PreservesAgentLogSectionsAppendedDuringTheRun()
    {
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var planReaderService = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
        var jobService = new JobService(configService, planReaderService: planReaderService);

        var job = new JobItem
        {
            Id = "3",
            Type = "ExecutePlan",
            PlanFile = "00003-TestPlan",
            Status = JobStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        // Simulate `tendril job add-log` appending to the seeded log mid-run.
        var jobsDir = Path.Combine(_tempDir.Path, "Jobs");
        Directory.CreateDirectory(jobsDir);
        var logFile = Path.Combine(jobsDir, "3-00003-ExecutePlan.md");
        File.WriteAllText(logFile,
            $"*Execution in progress...*\n\n{JobLogWriter.AgentLogMarker}\n## Agent Log — ExecutePlan (t)\n\nrebased onto main\n");

        jobService.WriteJobLog(job);

        var logContent = File.ReadAllText(logFile);
        Assert.Contains("# Job Log 3-00003-ExecutePlan", logContent);
        Assert.Contains("## Agent Log — ExecutePlan (t)", logContent);
        Assert.Contains("rebased onto main", logContent);
        Assert.DoesNotContain("Execution in progress", logContent);
    }

    [Fact]
    public void WriteJobLog_DoesNotMutateTheJob_SoCliCommandSurvivesToTheDatabase()
    {
        // JobService.CompleteJob calls WriteJobLog and then PersistJob. A writer that cleared CliCommand
        // would blank the persisted column and empty the "Arguments" row of the Job Debug sheet.
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var planReaderService = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
        var jobService = new JobService(configService, planReaderService: planReaderService);

        var job = new JobItem
        {
            Id = "4",
            Type = "ExecutePlan",
            PlanFile = "00004-TestPlan",
            Status = JobStatus.Completed,
            CompletedAt = DateTime.UtcNow,
            CliCommand = "claude --print --verbose",
            CompiledPrompt = "the compiled prompt"
        };

        jobService.WriteJobLog(job);

        Assert.Equal("claude --print --verbose", job.CliCommand);
        Assert.Equal("the compiled prompt", job.CompiledPrompt);

        var logFile = Path.Combine(_tempDir.Path, "Jobs", "4-00004-ExecutePlan.md");
        Assert.Contains("claude --print --verbose", File.ReadAllText(logFile));
    }

    [Fact]
    public void WriteJobLog_IsIdempotent()
    {
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var planReaderService = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
        var jobService = new JobService(configService, planReaderService: planReaderService);

        var job = new JobItem
        {
            Id = "5",
            Type = "ExecutePlan",
            PlanFile = "00005-TestPlan",
            Status = JobStatus.Completed,
            CompletedAt = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
            CliCommand = "claude --print"
        };

        jobService.WriteJobLog(job);
        var logFile = Path.Combine(_tempDir.Path, "Jobs", "5-00005-ExecutePlan.md");
        var first = File.ReadAllText(logFile);

        jobService.WriteJobLog(job);

        Assert.Equal(first, File.ReadAllText(logFile));
    }

    [Fact]
    public void WriteJobLog_AgentSectionIsNotSpoofedByFinalOutputEchoingTheHeading()
    {
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var planReaderService = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
        var jobService = new JobService(configService, planReaderService: planReaderService);

        var job = new JobItem { Id = "6", Type = "ExecutePlan", PlanFile = "00006-TestPlan", Status = JobStatus.Completed };

        // A log whose body merely mentions the rendered heading, with no marker: nothing to preserve.
        var jobsDir = Path.Combine(_tempDir.Path, "Jobs");
        Directory.CreateDirectory(jobsDir);
        var logFile = Path.Combine(jobsDir, "6-00006-ExecutePlan.md");
        File.WriteAllText(logFile, "*Execution in progress...*\n\n## Agent Log — spoofed\n\nnot a real entry\n");

        jobService.WriteJobLog(job);

        Assert.DoesNotContain("not a real entry", File.ReadAllText(logFile));
    }
}
