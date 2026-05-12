using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class ExecutePlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public ExecutePlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task ExecutePlan_ImplementsPlan_TransitionsToReadyForReview(string agent)
    {
        var cliLog = Path.Combine(_fixture.TendrilHome, $"execute-plan-{agent}.jsonl");

        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            $"Add Comment To Program {agent}",
            "Add a single-line comment '// E2E test' at the top of Program.cs",
            "E2ETest",
            steps: ["Add the comment '// E2E test' as the first line of Program.cs"],
            verifications: ["DotnetBuild"]);

        var result = await _fixture.Runner.RunAsync(
            "ExecutePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            cliLogPath: cliLog);

        PromptwareAssertions.AssertExitSuccess(result, $"ExecutePlan ({agent})");
        PromptwareAssertions.AssertNoAgentErrors(result);

        // Assert expected CLI calls
        CliLogAssertions.AssertCommandCalled(cliLog, "plan add-commit");
        CliLogAssertions.AssertCommandCalled(cliLog, "plan set-verification");
        CliLogAssertions.AssertAllCommandsSucceeded(cliLog);

        // Assert state transition
        PromptwareAssertions.AssertPlanState(planFolder, "ReadyForReview");
    }

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task ExecutePlan_CreatesCommits_WithPlanIdPrefix(string agent)
    {
        var cliLog = Path.Combine(_fixture.TendrilHome, $"execute-commits-{agent}.jsonl");

        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            $"Rename Namespace {agent}",
            "Add a using directive 'using System.Text;' at the top of Program.cs",
            "E2ETest",
            steps: ["Add 'using System.Text;' as the first using directive in Program.cs"],
            verifications: ["DotnetBuild"]);

        var planId = PlanSetupHelper.GetPlanId(planFolder);

        var result = await _fixture.Runner.RunAsync(
            "ExecutePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            cliLogPath: cliLog);

        PromptwareAssertions.AssertExitSuccess(result, $"ExecutePlan ({agent})");

        // Assert CLI calls include the plan ID
        CliLogAssertions.AssertCommandCalledWithArgs(cliLog, "plan add-commit", planId);

        // Verify commits in git
        var gitLog = RunGit(_fixture.TestRepo.LocalClonePath, "log --all --oneline -20");
        Assert.Contains($"[{planId}]", gitLog, StringComparison.OrdinalIgnoreCase);
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
