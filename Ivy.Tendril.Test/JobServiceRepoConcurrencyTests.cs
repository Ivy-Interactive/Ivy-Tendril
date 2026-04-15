using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceRepoConcurrencyTests : IDisposable
{
    private readonly string _testDir;

    public JobServiceRepoConcurrencyTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"RepoConcurrency-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void ExecutePlan_BlocksWhenRepoOverlaps()
    {
        var planA = CreatePlanFolder("PlanA", ["D:\\Repos\\MyRepo"]);
        var planB = CreatePlanFolder("PlanB", ["D:\\Repos\\MyRepo"]);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob("ExecutePlan", planA);
        service.StartJob("ExecutePlan", planB);

        var jobs = service.GetJobs();
        var jobA = jobs.First(j => j.Args[0] == planA);
        var jobB = jobs.First(j => j.Args[0] == planB);

        Assert.Equal(JobStatus.Queued, jobA.Status);
        Assert.Equal(JobStatus.Blocked, jobB.Status);
        Assert.Contains("currently executing", jobB.StatusMessage);
    }

    [Fact]
    public void ExecutePlan_AllowsWhenReposDiffer()
    {
        var planA = CreatePlanFolder("PlanA", ["D:\\Repos\\RepoX"]);
        var planB = CreatePlanFolder("PlanB", ["D:\\Repos\\RepoY"]);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob("ExecutePlan", planA);
        service.StartJob("ExecutePlan", planB);

        var jobs = service.GetJobs();
        Assert.All(jobs, j => Assert.Equal(JobStatus.Queued, j.Status));
    }

    [Fact]
    public void RepoOverlap_NormalizesWindowsPaths()
    {
        var planA = CreatePlanFolder("PlanA", ["D:\\Repos\\MyRepo"]);
        var planB = CreatePlanFolder("PlanB", ["D:/Repos/MyRepo/"]);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob("ExecutePlan", planA);
        service.StartJob("ExecutePlan", planB);

        var jobs = service.GetJobs();
        var jobB = jobs.First(j => j.Args[0] == planB);
        Assert.Equal(JobStatus.Blocked, jobB.Status);
    }

    [Fact]
    public void RepoOverlap_HandlesMultipleRepos()
    {
        var planA = CreatePlanFolder("PlanA", ["D:\\Repos\\X", "D:\\Repos\\Y"]);
        var planB = CreatePlanFolder("PlanB", ["D:\\Repos\\Y", "D:\\Repos\\Z"]);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob("ExecutePlan", planA);
        service.StartJob("ExecutePlan", planB);

        var jobs = service.GetJobs();
        var jobB = jobs.First(j => j.Args[0] == planB);
        Assert.Equal(JobStatus.Blocked, jobB.Status);
    }

    [Fact]
    public void ExecutePlan_NoRepos_AllowsConcurrent()
    {
        var planA = CreatePlanFolder("PlanA", []);
        var planB = CreatePlanFolder("PlanB", []);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob("ExecutePlan", planA);
        service.StartJob("ExecutePlan", planB);

        var jobs = service.GetJobs();
        Assert.All(jobs, j => Assert.Equal(JobStatus.Queued, j.Status));
    }

    [Fact]
    public void BlockedPlan_RetriesWhenBlockingPlanCompletes()
    {
        var planA = CreatePlanFolder("PlanA", ["D:\\Repos\\MyRepo"]);
        var planB = CreatePlanFolder("PlanB", ["D:\\Repos\\MyRepo"]);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var idA = service.StartJob("ExecutePlan", planA);
        service.StartJob("ExecutePlan", planB);

        var jobsBefore = service.GetJobs();
        var jobBBefore = jobsBefore.First(j => j.Args[0] == planB);
        Assert.Equal(JobStatus.Blocked, jobBBefore.Status);

        service.CompleteJob(idA, 0);

        var jobsAfter = service.GetJobs();
        var blockedAfter = jobsAfter.Where(j => j.Args[0] == planB && j.Status == JobStatus.Blocked).ToList();
        Assert.Empty(blockedAfter);
    }

    private string CreatePlanFolder(string name, string[] repos)
    {
        var folder = Path.Combine(_testDir, name);
        Directory.CreateDirectory(folder);

        var reposList = repos.Length > 0
            ? "\n" + string.Join("\n", repos.Select(r => $"- {r}"))
            : " []";

        File.WriteAllText(Path.Combine(folder, "plan.yaml"),
            $"state: Executing\nproject: Auto\nrepos:{reposList}\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\n");

        return folder;
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); }
        catch { /* best effort */ }
    }
}
