using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class ExpandPlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public ExpandPlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExpandPlan_AddsDetailedSubSteps()
    {
        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            "Add Logging To Program",
            "Add structured logging throughout the application",
            "E2ETest",
            steps:
            [
                "Add logging to startup",
                "Add logging to request handling",
                "Add logging to error paths"
            ]);

        var result = await _fixture.Runner.RunAsync(
            "ExpandPlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath);

        PromptwareAssertions.AssertExitSuccess(result, "ExpandPlan");
        PromptwareAssertions.AssertPlanState(planFolder, "Draft");

        // After expand, the plan should have more detailed steps or a new revision
        var planYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var revisionsDir = Path.Combine(planFolder, "revisions");
        var hasNewRevisions = Directory.Exists(revisionsDir) &&
            Directory.GetFiles(revisionsDir, "*.md").Length > 0;
        var hasMoreSteps = planYaml.Split("- ").Length > 4;

        Assert.True(hasNewRevisions || hasMoreSteps,
            "ExpandPlan should produce either a new revision or more detailed steps in plan.yaml.\n" +
            $"Revisions dir exists: {Directory.Exists(revisionsDir)}\n" +
            $"plan.yaml step count: {planYaml.Split("- ").Length - 1}");
    }
}
