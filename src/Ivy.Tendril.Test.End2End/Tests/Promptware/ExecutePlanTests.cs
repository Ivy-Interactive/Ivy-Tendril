using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class ExecutePlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public ExecutePlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExecutePlan_ImplementsPlan_TransitionsToReadyForReview()
    {
        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            "Add Comment To Program",
            "Add a single-line comment '// E2E test' at the top of Program.cs",
            "E2ETest",
            steps: ["Add the comment '// E2E test' as the first line of Program.cs"],
            verifications: ["DotnetBuild"]);

        var result = await _fixture.Runner.RunAsync(
            "ExecutePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath);

        PromptwareAssertions.AssertExitSuccess(result, "ExecutePlan");
        PromptwareAssertions.AssertNoAgentErrors(result);
        PromptwareAssertions.AssertPlanState(planFolder, "ReadyForReview");
    }

    [Fact]
    public async Task ExecutePlan_CreatesCommits_WithPlanIdPrefix()
    {
        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            "Rename Namespace In Program",
            "Add a using directive 'using System.Text;' at the top of Program.cs",
            "E2ETest",
            steps: ["Add 'using System.Text;' as the first using directive in Program.cs"],
            verifications: ["DotnetBuild"]);

        var planId = PlanSetupHelper.GetPlanId(planFolder);

        var result = await _fixture.Runner.RunAsync(
            "ExecutePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath);

        PromptwareAssertions.AssertExitSuccess(result, "ExecutePlan");

        // Verify commits were made with plan ID prefix
        var repoPath = _fixture.TestRepo.LocalClonePath;
        var gitLog = RunGit(repoPath, "log --all --oneline -20");
        Assert.Contains($"[{planId}]", gitLog,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string RunGit(string repoPath, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }
}
