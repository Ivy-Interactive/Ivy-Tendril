using System.Collections.Concurrent;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

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

    [Theory]
    [InlineData("D:\\Plans\\01234-TestPlan")]
    [InlineData("/home/user/Plans/01234-TestPlan")]
    public void ExtractPlanIdFromFolder_ReturnsCorrectId(string path)
    {
        var result = PlanYamlHelper.ExtractPlanIdFromFolder(path);
        Assert.Equal("01234", result);
    }

    [Theory]
    [InlineData("D:\\Plans\\SomePlan")]
    [InlineData("/home/user/Plans/SomePlan")]
    public void ExtractPlanIdFromFolder_HandlesNoDash(string path)
    {
        var result = PlanYamlHelper.ExtractPlanIdFromFolder(path);
        Assert.Null(result);
    }

    [Fact]
    public void HasAgentDirectProgram_ReturnsTrueWhenProgramMdExistsWithoutScript()
    {
        var programFolder = Path.Combine(_tempPromptsRoot, "TestPromptware");
        Directory.CreateDirectory(programFolder);
        File.WriteAllText(Path.Combine(programFolder, "Program.md"), "# Test Program");

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

        var method = typeof(JobLauncher).GetMethod("HasAgentDirectProgram",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = (bool?)method?.Invoke(null, new object[] { programFolder, "TestPromptware" });

        Assert.False(result);
    }

    [Fact]
    public async Task RunStaleOutputWatchdog_DoesNotFlagRecentOutput()
    {
        var configService = new ConfigService(new TendrilSettings());
        configService.SetTendrilHome(_tempTendrilHome);

        var job = new JobItem
        {
            Id = "test-1",
            Type = "CreatePlan",
            TypedArgs = new CreatePlanArgs("Test", "Auto"),
            Project = "TestProject",
            Status = JobStatus.Running,
            LastOutputAt = DateTime.UtcNow
        };
        var jobs = new ConcurrentDictionary<string, JobItem>();
        jobs[job.Id] = job;

        var cts = new CancellationTokenSource();
        var watchdogTask = JobMonitor.RunStaleOutputWatchdog(
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
            TypedArgs = new CreatePlanArgs("Test", "Auto"),
            Project = "TestProject",
            Status = JobStatus.Running,
            StatusFilePath = Path.Combine(_tempTendrilHome, "status-test-1.json")
        };
        var jobs = new ConcurrentDictionary<string, JobItem>();
        jobs[job.Id] = job;

        var cts = new CancellationTokenSource();
        var pollerTask = JobMonitor.RunStatusFilePoller(job.Id, cts, jobs);

        job.Status = JobStatus.Completed;
        await Task.Delay(TimeSpan.FromSeconds(2));

        await pollerTask;

        Assert.Equal(JobStatus.Completed, job.Status);
    }
}
