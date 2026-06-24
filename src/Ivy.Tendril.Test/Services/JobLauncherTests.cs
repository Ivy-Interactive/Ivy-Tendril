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
      - path: D:\YoloRepo
        prRule: yolo
        baseBranch: development
");
    }

    private JobLauncher CreateLauncher()
    {
        var configService = new ConfigService(new TendrilSettings());
        configService.SetTendrilHome(_tempTendrilHome);
        return new JobLauncher(configService, null,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, _tempPromptsRoot);
    }

    private string? InvokeBuildRepoConfigsYaml(JobLauncher launcher, PlanYaml plan, string project)
    {
        var method = typeof(JobLauncher).GetMethod("BuildRepoConfigsYaml",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (string?)method?.Invoke(launcher, new object[] { plan, project });
    }

    // prRule is a UI-only concept (it decides whether the PR dialog is shown and its defaults).
    // It must NOT leak into the firmware header — the CreatePr promptware acts solely on the
    // explicit Pr* flags, and a stray "prRule: yolo" used to make the agent merge despite the
    // user opting out (issue #1272).
    [Fact]
    public void BuildRepoConfigsYaml_DoesNotEmitPrRule_EvenForYoloRepo()
    {
        var launcher = CreateLauncher();
        var plan = new PlanYaml { Project = "TestProject", Repos = { @"D:\YoloRepo" } };

        var yaml = InvokeBuildRepoConfigsYaml(launcher, plan, "TestProject");

        Assert.NotNull(yaml);
        Assert.DoesNotContain("prRule", yaml);
        Assert.DoesNotContain("yolo", yaml);
    }

    [Fact]
    public void BuildRepoConfigsYaml_StillEmitsBaseBranch()
    {
        var launcher = CreateLauncher();
        var plan = new PlanYaml { Project = "TestProject", Repos = { @"D:\YoloRepo" } };

        var yaml = InvokeBuildRepoConfigsYaml(launcher, plan, "TestProject");

        Assert.NotNull(yaml);
        Assert.Contains("baseBranch: development", yaml);
    }

    private static Dictionary<string, string> InvokeAddCreatePrOptions(JobItem job)
    {
        var values = new Dictionary<string, string>();
        var method = typeof(JobLauncher).GetMethod("AddCreatePrOptions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method?.Invoke(null, new object[] { job, values });
        return values;
    }

    // The PR dialog's "Reviewer" field flows into CreatePrArgs.Reviewer and must reach the
    // CreatePr promptware as the PrReviewer firmware value (issue #1311). It used to be the
    // PrAssignee value, which emitted a GitHub assignee instead of a requested reviewer.
    [Fact]
    public void AddCreatePrOptions_EmitsPrReviewer_WhenReviewerSet()
    {
        var job = new JobItem
        {
            Id = "pr-1",
            Type = "CreatePr",
            TypedArgs = new CreatePrArgs(@"D:\Plans\01234-TestPlan", Reviewer: "octocat"),
            Project = "TestProject"
        };

        var values = InvokeAddCreatePrOptions(job);

        Assert.Equal("octocat", values["PrReviewer"]);
        Assert.False(values.ContainsKey("PrAssignee"));
    }

    [Fact]
    public void AddCreatePrOptions_OmitsPrReviewer_WhenReviewerNull()
    {
        var job = new JobItem
        {
            Id = "pr-2",
            Type = "CreatePr",
            TypedArgs = new CreatePrArgs(@"D:\Plans\01234-TestPlan"),
            Project = "TestProject"
        };

        var values = InvokeAddCreatePrOptions(job);

        Assert.False(values.ContainsKey("PrReviewer"));
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

}
