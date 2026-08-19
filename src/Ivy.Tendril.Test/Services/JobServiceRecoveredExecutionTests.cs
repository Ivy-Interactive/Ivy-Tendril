using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class JobServiceRecoveredExecutionTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private string CreatePlanFolder(
        string state = "Executing",
        List<string>? commits = null,
        List<(string Name, VerificationStatus Status)>? verifications = null,
        VerificationStatus? preExecutionResult = null)
    {
        var dir = Path.Combine(_tempDir.Path, $"plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var commitsList = commits ?? ["abc1234"];
        var verifsList = verifications ?? [("Build", VerificationStatus.Pass), ("Test", VerificationStatus.Pass)];

        var yaml = $"""
                    state: {state}
                    project: TestProject
                    level: Feature
                    title: Test Recovered Plan
                    repos:
                    - /path/to/repo
                    prs: []
                    commits:
                    {string.Join("\n", commitsList.Select(c => $"- {c}"))}
                    verifications:
                    {string.Join("\n", verifsList.Select(v => $"  - name: {v.Name}\n    status: {v.Status}"))}
                    relatedPlans: []
                    dependsOn: []
                    """;

        File.WriteAllText(Path.Combine(dir, "plan.yaml"), yaml);

        if (preExecutionResult.HasValue)
        {
            var preDir = Path.Combine(dir, "Verification");
            Directory.CreateDirectory(preDir);
            File.WriteAllText(Path.Combine(preDir, "PreExecution.md"), $"---\nresult: {preExecutionResult.Value}\n---\n");
        }

        return dir;
    }

    [Fact]
    public void IsRecoveredExecutionJob_WithCommitsAndAllPassingVerifications_ReturnsTrue()
    {
        var planFolder = CreatePlanFolder();
        var job = new JobItem
        {
            Id = "job-1",
            Type = "ExecutePlan",
            TypedArgs = new ExecutePlanArgs(planFolder),
        };

        var isRecovered = JobService.IsRecoveredExecutionJob(job);

        Assert.True(isRecovered);
    }

    [Fact]
    public void IsRecoveredExecutionJob_WithNoCommits_ReturnsFalse()
    {
        var planFolder = CreatePlanFolder(commits: []);
        var job = new JobItem
        {
            Id = "job-1",
            Type = "ExecutePlan",
            TypedArgs = new ExecutePlanArgs(planFolder),
        };

        var isRecovered = JobService.IsRecoveredExecutionJob(job);

        Assert.False(isRecovered);
    }

    [Fact]
    public void IsRecoveredExecutionJob_WithFailedVerification_ReturnsFalse()
    {
        var planFolder = CreatePlanFolder(verifications: [("Build", VerificationStatus.Pass), ("Test", VerificationStatus.Fail)]);
        var job = new JobItem
        {
            Id = "job-1",
            Type = "ExecutePlan",
            TypedArgs = new ExecutePlanArgs(planFolder),
        };

        var isRecovered = JobService.IsRecoveredExecutionJob(job);

        Assert.False(isRecovered);
    }

    [Fact]
    public void IsRecoveredExecutionJob_WithPendingVerification_ReturnsFalse()
    {
        var planFolder = CreatePlanFolder(verifications: [("Build", VerificationStatus.Pass), ("Test", VerificationStatus.Pending)]);
        var job = new JobItem
        {
            Id = "job-1",
            Type = "ExecutePlan",
            TypedArgs = new ExecutePlanArgs(planFolder),
        };

        var isRecovered = JobService.IsRecoveredExecutionJob(job);

        Assert.False(isRecovered);
    }

    [Fact]
    public void IsRecoveredExecutionJob_WithPreExecutionFail_ReturnsFalse()
    {
        var planFolder = CreatePlanFolder(preExecutionResult: VerificationStatus.Fail);
        var job = new JobItem
        {
            Id = "job-1",
            Type = "ExecutePlan",
            TypedArgs = new ExecutePlanArgs(planFolder),
        };

        var isRecovered = JobService.IsRecoveredExecutionJob(job);

        Assert.False(isRecovered);
    }

    [Fact]
    public void CompleteJob_ExitCode0_WithRecoveredAntigravityToolError_CompletesSuccessfully()
    {
        var planFolder = CreatePlanFolder();
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;

        // Simulate Antigravity output where mid-turn tool error occurred, but agent completed work
        job.OutputLines.Enqueue("""{"kind":"result","error":"declaring permissions: cortex tool write_to_file: path is not valid","response":"I have completed execution","is_success":false}""");

        service.CompleteJob(id, 0);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Null(job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_ExitCode0_WithUnrecoveredAntigravityToolError_MarksFailedWithCleanErrorMessage()
    {
        // When verifications failed and no commits were made, job is not recovered and should fail
        var planFolder = CreatePlanFolder(commits: [], verifications: [("Build", VerificationStatus.Fail)]);
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;

        job.OutputLines.Enqueue("""{"kind":"result","error":"declaring permissions: cortex tool write_to_file: path is not valid","response":"I have completed execution","is_success":false}""");

        service.CompleteJob(id, 0);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("declaring permissions: cortex tool write_to_file: path is not valid", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_ExitCode0_CreatePlan_WithRecoveredAntigravityToolError_CompletesSuccessfully()
    {
        var plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(plansDir);
        var createdFolder = Path.Combine(plansDir, "00001-TestPlan");
        Directory.CreateDirectory(createdFolder);
        File.WriteAllText(Path.Combine(createdFolder, "plan.yaml"), "title: Test\n");

        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var planReader = new PlanReaderService(config, NullLogger<PlanReaderService>.Instance);
        var service = new JobService(config, planReaderService: planReader);
        var id = service.CreateTestJob(new CreatePlanArgs("Test task description", "TestProject"));
        var job = service.GetJob(id)!;
        job.ReportedPlanId = "00001";
        job.OutputLines.Enqueue("PlanId: 00001");
        job.OutputLines.Enqueue("""{"kind":"result","error":"declaring permissions: cortex tool view_file: no such file","response":"Plan created","is_success":false}""");

        service.CompleteJob(id, 0);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Null(job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_ExitCode0_CreatePlan_WithoutCreatedPlan_MarksFailed()
    {
        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var planReader = new PlanReaderService(config, NullLogger<PlanReaderService>.Instance);
        var service = new JobService(config, planReaderService: planReader);
        var id = service.CreateTestJob(new CreatePlanArgs("Test task description", "TestProject"));
        var job = service.GetJob(id)!;
        job.ReportedPlanId = "99999";
        job.OutputLines.Enqueue("""{"kind":"result","error":"declaring permissions: cortex tool view_file: no such file","response":"Plan failed","is_success":false}""");

        service.CompleteJob(id, 0);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("declaring permissions: cortex tool view_file: no such file", job.StatusMessage);
    }
}
