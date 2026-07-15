using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Services;

[Collection("TendrilHome")]
public class JobLauncherProjectGuardTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _originalHome;
    private readonly string? _originalPlans;

    public JobLauncherProjectGuardTests()
    {
        _originalHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalHome);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _originalPlans);
        _tempDir.Dispose();
    }

    // #1340 backstop: launching an ExecutePlan job whose plan references a repo outside the project
    // must fail the job up front rather than execute in the wrong repo.
    [Fact]
    public void StartJob_RefusesExecute_WhenPlanRepoOutsideProject()
    {
        var inProject = Path.Combine(_tempDir.Path, "repos", "InProject");
        Directory.CreateDirectory(inProject);
        var outside = Path.Combine(_tempDir.Path, "repos", "Outside");
        Directory.CreateDirectory(outside);

        var settings = new TendrilSettings
        {
            JobTimeout = 30,
            StaleOutputTimeout = 10,
            Projects = [new ProjectConfig { Name = "TestProject", Repos = [new RepoRef { Path = inProject }] }]
        };
        var service = new JobService(new ConfigService(settings));

        var planFolder = Path.Combine(_tempDir.Path, "plan-1");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"),
            $"state: Executing\nproject: TestProject\nlevel: NiceToHave\ntitle: Test Plan\n" +
            $"created: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\nrepos:\n- {outside}\n" +
            "prs: []\ncommits: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\n");

        var id = service.StartJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("not part of project", job.StatusMessage ?? "");
    }

    // A plan whose repo IS in the project must not be refused by the backstop. (It may still fail
    // later for unrelated reasons, but never with the project-mismatch message.)
    [Fact]
    public void StartJob_DoesNotRefuse_WhenPlanRepoInProject()
    {
        var inProject = Path.Combine(_tempDir.Path, "repos", "InProject");
        Directory.CreateDirectory(inProject);

        var settings = new TendrilSettings
        {
            JobTimeout = 30,
            StaleOutputTimeout = 10,
            Projects = [new ProjectConfig { Name = "TestProject", Repos = [new RepoRef { Path = inProject }] }]
        };
        var service = new JobService(new ConfigService(settings));

        var planFolder = Path.Combine(_tempDir.Path, "plan-2");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"),
            $"state: Executing\nproject: TestProject\nlevel: NiceToHave\ntitle: Test Plan\n" +
            $"created: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\nrepos:\n- {inProject}\n" +
            "prs: []\ncommits: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\n");

        var id = service.StartJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;

        Assert.DoesNotContain("not part of project", job.StatusMessage ?? "");
    }
}
