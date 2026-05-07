using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class CreatePrTests
{
    private readonly PromptwareTestFixture _fixture;

    public CreatePrTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task CreatePr_CreatesGitHubPR_AddsPrUrlToPlanYaml(string agent)
    {
        var cliLog = Path.Combine(_fixture.TendrilHome, $"create-pr-{agent}.jsonl");

        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            $"Add Pr Test Comment {agent}",
            "Add a comment '// PR test' to the top of Program.cs",
            "E2ETest",
            steps: ["Add '// PR test' as a comment at the top of Program.cs"],
            verifications: ["DotnetBuild"]);

        // Execute the plan first to produce commits
        var execResult = await _fixture.Runner.RunAsync(
            "ExecutePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent);

        PromptwareAssertions.AssertExitSuccess(execResult, $"ExecutePlan ({agent})");
        PromptwareAssertions.AssertPlanState(planFolder, "ReadyForReview");

        // Now create the PR
        var result = await _fixture.Runner.RunAsync(
            "CreatePr",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            cliLogPath: cliLog);

        PromptwareAssertions.AssertExitSuccess(result, $"CreatePr ({agent})");

        // Assert expected CLI calls
        CliLogAssertions.AssertCommandCalled(cliLog, "plan add-pr");
        CliLogAssertions.AssertCommandCalled(cliLog, "plan set");
        CliLogAssertions.AssertAllCommandsSucceeded(cliLog);

        // Verify PR URL in plan.yaml
        PromptwareAssertions.AssertPlanYamlContains(planFolder, "prs:");
    }
}
