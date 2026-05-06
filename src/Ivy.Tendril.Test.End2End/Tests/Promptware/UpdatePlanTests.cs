using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class UpdatePlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public UpdatePlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task UpdatePlan_ModifiesPlan_KeepsStateDraft()
    {
        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            "Simple File Change",
            "Make a simple change to a file",
            "E2ETest",
            steps: ["Modify Program.cs"]);

        var originalYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));

        var result = await _fixture.Runner.RunAsync(
            "UpdatePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath);

        PromptwareAssertions.AssertExitSuccess(result, "UpdatePlan");
        PromptwareAssertions.AssertPlanState(planFolder, "Draft");

        // The plan.yaml content should have changed (updated steps, description, or revision)
        var updatedYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var revisionsDir = Path.Combine(planFolder, "revisions");
        var hasRevisions = Directory.Exists(revisionsDir) &&
            Directory.GetFiles(revisionsDir, "*.md").Length > 0;

        Assert.True(updatedYaml != originalYaml || hasRevisions,
            "UpdatePlan should modify plan.yaml or create a revision.\n" +
            $"YAML changed: {updatedYaml != originalYaml}\n" +
            $"Has revisions: {hasRevisions}");
    }
}
