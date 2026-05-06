using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class CreatePrTests
{
    private readonly PromptwareTestFixture _fixture;

    public CreatePrTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreatePr_CreatesGitHubPR_AddsPrUrlToPlanYaml()
    {
        // Setup: create a plan and execute it first to get commits on a branch
        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            "Add Pr Test Comment",
            "Add a comment '// PR test' to the top of Program.cs",
            "E2ETest",
            steps: ["Add '// PR test' as a comment at the top of Program.cs"],
            verifications: ["DotnetBuild"]);

        // Execute the plan first to produce commits
        var execResult = await _fixture.Runner.RunAsync(
            "ExecutePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath);

        PromptwareAssertions.AssertExitSuccess(execResult, "ExecutePlan");
        PromptwareAssertions.AssertPlanState(planFolder, "ReadyForReview");

        // Now create the PR
        var result = await _fixture.Runner.RunAsync(
            "CreatePr",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath);

        PromptwareAssertions.AssertExitSuccess(result, "CreatePr");

        // Verify PR URL was added to plan.yaml
        PromptwareAssertions.AssertPlanYamlContains(planFolder, "prs:");
    }
}
