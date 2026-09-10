using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class JobLauncherCreatePrBranchTests : IDisposable
{
    private readonly string _tempTendrilHome;

    public JobLauncherCreatePrBranchTests()
    {
        _tempTendrilHome = Path.Combine(Path.GetTempPath(), $"launcher-branch-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempTendrilHome);
        Directory.CreateDirectory(Path.Combine(_tempTendrilHome, "Promptwares"));
        Directory.CreateDirectory(Path.Combine(_tempTendrilHome, "Plans"));

        File.WriteAllText(Path.Combine(_tempTendrilHome, "config.yaml"), @"
projects:
  - name: TestProject
    repos:
      - path: D:\TestRepo
        baseBranch: development
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
    public void AddCreatePrOptions_PropagatesPrBaseBranch_ToFirmwareValues()
    {
        var job = new JobItem
        {
            Id = "pr-1",
            Type = "CreatePr",
            TypedArgs = new CreatePrArgs(@"D:\Plans\01234-TestPlan", BaseBranch: "feature/custom-target"),
            Project = "TestProject"
        };

        var values = new Dictionary<string, string>();
        var method = typeof(JobLauncher).GetMethod("AddCreatePrOptions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method?.Invoke(null, new object[] { job, values });

        Assert.True(values.ContainsKey("PrBaseBranch"));
        Assert.Equal("feature/custom-target", values["PrBaseBranch"]);
    }

    [Fact]
    public void AddPlanRepos_UsesCustomBaseBranch_WhenCreatePrSpecifiesBaseBranch()
    {
        var configService = new ConfigService(new TendrilSettings());
        configService.SetTendrilHome(_tempTendrilHome);
        var launcher = new JobLauncher(configService, null, NullLogger<JobLauncher>.Instance, Path.Combine(_tempTendrilHome, "Promptwares"));

        var plan = new PlanYaml
        {
            Project = "TestProject",
            Repos = { @"D:\TestRepo" }
        };

        var job = new JobItem
        {
            Id = "pr-2",
            Type = "CreatePr",
            TypedArgs = new CreatePrArgs(@"D:\Plans\01234-TestPlan", BaseBranch: "release/1.2"),
            Project = "TestProject"
        };

        var values = new Dictionary<string, string>();
        var addRepoConfigsMethod = typeof(JobLauncher).GetMethod("AddRepoConfigsIfNeeded",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        addRepoConfigsMethod?.Invoke(launcher, new object[] { job, plan, values });

        Assert.True(values.ContainsKey("RepoConfigs"));
        var repoConfigs = values["RepoConfigs"];
        Assert.Contains("baseBranch: release/1.2", repoConfigs);
        Assert.DoesNotContain("baseBranch: development", repoConfigs);
    }
}
