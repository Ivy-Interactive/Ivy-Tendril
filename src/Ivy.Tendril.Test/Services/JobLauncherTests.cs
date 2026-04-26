using System.Collections.Concurrent;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class JobLauncherTests : IDisposable
{
    private readonly string _tempTendrilHome;
    private readonly string _tempPromptsRoot;

    public JobLauncherTests()
    {
        _tempTendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-launcher-test-{Guid.NewGuid()}");
        _tempPromptsRoot = Path.Combine(_tempTendrilHome, "Promptwares");
        Directory.CreateDirectory(_tempTendrilHome);
        Directory.CreateDirectory(_tempPromptsRoot);
        Directory.CreateDirectory(Path.Combine(_tempTendrilHome, "Inbox"));
        Directory.CreateDirectory(Path.Combine(_tempTendrilHome, "Plans"));

        File.WriteAllText(Path.Combine(_tempTendrilHome, "config.yaml"), @"
gitTimeout: 30
jobTimeout: 30
staleOutputTimeout: 10
maxConcurrentJobs: 5
agentProfiles:
  - name: default
    model: sonnet
projects:
  - name: TestProject
    repos:
      - path: D:\TestRepo
        prRule: default
");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempTendrilHome))
        {
            try
            {
                Directory.Delete(_tempTendrilHome, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void ExtractPlanIdFromFolder_ReturnsCorrectId()
    {
        var launcher = new JobLauncher(null, NullLogger.Instance, _tempPromptsRoot);
        var method = typeof(JobLauncher).GetMethod("ExtractPlanIdFromFolder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method?.Invoke(null, new object[] { "D:\\Plans\\01234-TestPlan" });

        Assert.Equal("01234", result);
    }

    [Fact]
    public void ExtractPlanIdFromFolder_HandlesNoDash()
    {
        var launcher = new JobLauncher(null, NullLogger.Instance, _tempPromptsRoot);
        var method = typeof(JobLauncher).GetMethod("ExtractPlanIdFromFolder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method?.Invoke(null, new object[] { "D:\\Plans\\SomePlan" });

        Assert.Null(result);
    }

    [Fact]
    public void HasAgentDirectProgram_ReturnsTrueWhenProgramMdExistsWithoutScript()
    {
        var programFolder = Path.Combine(_tempPromptsRoot, "TestPromptware");
        Directory.CreateDirectory(programFolder);
        File.WriteAllText(Path.Combine(programFolder, "Program.md"), "# Test Program");

        var launcher = new JobLauncher(null, NullLogger.Instance, _tempPromptsRoot);
        var method = typeof(JobLauncher).GetMethod("HasAgentDirectProgram",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = (bool?)method?.Invoke(null, new object[] { programFolder, "TestPromptware" });

        Assert.True(result);
    }

    [Fact]
    public void HasAgentDirectProgram_ReturnsFalseWhenScriptExists()
    {
        var programFolder = Path.Combine(_tempPromptsRoot, "TestPromptware");
        Directory.CreateDirectory(programFolder);
        File.WriteAllText(Path.Combine(programFolder, "Program.md"), "# Test Program");
        File.WriteAllText(Path.Combine(programFolder, "TestPromptware.ps1"), "# Script");

        var launcher = new JobLauncher(null, NullLogger.Instance, _tempPromptsRoot);
        var method = typeof(JobLauncher).GetMethod("HasAgentDirectProgram",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = (bool?)method?.Invoke(null, new object[] { programFolder, "TestPromptware" });

        Assert.False(result);
    }

    [Fact]
    public void HasAgentDirectProgram_ReturnsFalseWhenProgramMdMissing()
    {
        var programFolder = Path.Combine(_tempPromptsRoot, "TestPromptware");
        Directory.CreateDirectory(programFolder);

        var launcher = new JobLauncher(null, NullLogger.Instance, _tempPromptsRoot);
        var method = typeof(JobLauncher).GetMethod("HasAgentDirectProgram",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = (bool?)method?.Invoke(null, new object[] { programFolder, "TestPromptware" });

        Assert.False(result);
    }

    [Fact]
    public async Task RunStaleOutputWatchdog_DetectsStaleOutput()
    {
        var configService = new ConfigService(new TendrilSettings());
        configService.SetTendrilHome(_tempTendrilHome);

        var launcher = new JobLauncher(configService, NullLogger.Instance, _tempPromptsRoot);
        var job = new JobItem
        {
            Id = "test-1",
            Type = "CreatePlan",
            Args = new[] { "Test" },
            Project = "TestProject",
            Status = JobStatus.Running,
            LastOutputAt = DateTime.UtcNow.AddSeconds(-70)
        };
        var jobs = new ConcurrentDictionary<string, JobItem>();
        jobs[job.Id] = job;

        var cts = new CancellationTokenSource();
        var watchdogTask = launcher.RunStaleOutputWatchdog(
            job.Id,
            cts,
            jobs,
            TimeSpan.FromSeconds(10));

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.True(job.StaleOutputDetected);
        cts.Cancel();
        await watchdogTask;
    }

    [Fact]
    public async Task RunStaleOutputWatchdog_DoesNotFlagRecentOutput()
    {
        var configService = new ConfigService(new TendrilSettings());
        configService.SetTendrilHome(_tempTendrilHome);

        var launcher = new JobLauncher(configService, NullLogger.Instance, _tempPromptsRoot);
        var job = new JobItem
        {
            Id = "test-1",
            Type = "CreatePlan",
            Args = new[] { "Test" },
            Project = "TestProject",
            Status = JobStatus.Running,
            LastOutputAt = DateTime.UtcNow
        };
        var jobs = new ConcurrentDictionary<string, JobItem>();
        jobs[job.Id] = job;

        var cts = new CancellationTokenSource();
        var watchdogTask = launcher.RunStaleOutputWatchdog(
            job.Id,
            cts,
            jobs,
            TimeSpan.FromMinutes(10));

        await Task.Delay(TimeSpan.FromSeconds(2));
        cts.Cancel();

        await watchdogTask;

        Assert.False(job.StaleOutputDetected);
    }

    [Fact]
    public async Task RunStatusFilePoller_ExitsWhenJobCompletes()
    {
        var job = new JobItem
        {
            Id = "test-1",
            Type = "CreatePlan",
            Args = new[] { "Test" },
            Project = "TestProject",
            Status = JobStatus.Running,
            StatusFilePath = Path.Combine(_tempTendrilHome, "status-test-1.json")
        };
        var jobs = new ConcurrentDictionary<string, JobItem>();
        jobs[job.Id] = job;

        var cts = new CancellationTokenSource();
        var pollerTask = JobLauncher.RunStatusFilePoller(job.Id, cts, jobs);

        job.Status = JobStatus.Completed;
        await Task.Delay(TimeSpan.FromSeconds(2));

        await pollerTask;

        Assert.Equal(JobStatus.Completed, job.Status);
    }
}
