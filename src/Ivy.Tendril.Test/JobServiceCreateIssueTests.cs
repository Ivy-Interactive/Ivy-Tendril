using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceCreateIssueTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _planYamlPath;

    public JobServiceCreateIssueTests()
    {
        _planYamlPath = Path.Combine(_tempDir.Path, "plan.yaml");
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private static JobService CreateService()
    {
        return new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
    }

    private void WritePlanYaml(string state)
    {
        var repoDir = Path.Combine(_tempDir.Path, "repo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(_planYamlPath, $"""
                                          state: {state}
                                          project: Tendril
                                          level: NiceToHave
                                          title: Test Plan
                                          created: 2026-04-01T00:00:00Z
                                          updated: 2026-04-01T00:00:00Z
                                          repos:
                                          - {repoDir}
                                          prs: []
                                          commits: []
                                          verifications: []
                                          relatedPlans: []
                                          dependsOn: []
                                          """);
    }

    [Fact]
    public void CompleteJob_CreateIssueSuccess_SetsPlanStateToCompleted()
    {
        WritePlanYaml("Draft");
        var service = CreateService();

        var id = service.StartJob("CreateIssue", _tempDir.Path, "-Repo", "owner/repo", "-Assignee", "", "-Labels", "");
        service.CompleteJob(id, 0);

        var content = File.ReadAllText(_planYamlPath);
        Assert.Contains("state: Completed", content);
    }

    [Fact]
    public void CompleteJob_CreateIssueFailure_DoesNotResetPlanState()
    {
        WritePlanYaml("Draft");
        var service = CreateService();

        var id = service.StartJob("CreateIssue", _tempDir.Path, "-Repo", "owner/repo", "-Assignee", "", "-Labels", "");
        service.CompleteJob(id, 1);

        var content = File.ReadAllText(_planYamlPath);
        Assert.Contains("state: Draft", content);
    }
}