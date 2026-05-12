using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class UpdatePlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public UpdatePlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task UpdatePlan_ModifiesPlan_KeepsStateDraft(string agent)
    {
        var cliLog = Path.Combine(_fixture.TendrilHome, $"update-plan-{agent}.jsonl");

        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            $"Simple File Change {agent}",
            "Make a simple change to a file",
            "E2ETest",
            steps: ["Modify Program.cs"]);

        var originalYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));

        var result = await _fixture.Runner.RunAsync(
            "UpdatePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            cliLogPath: cliLog);

        PromptwareAssertions.AssertExitSuccess(result, $"UpdatePlan ({agent})");
        PromptwareAssertions.AssertPlanState(planFolder, "Draft");

        // Assert CLI calls
        CliLogAssertions.AssertAllCommandsSucceeded(cliLog);

        // The plan should have changed
        var updatedYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var revisionsDir = Path.Combine(planFolder, "revisions");
        var hasRevisions = Directory.Exists(revisionsDir) &&
            Directory.GetFiles(revisionsDir, "*.md").Length > 0;

        Assert.True(updatedYaml != originalYaml || hasRevisions,
            $"UpdatePlan ({agent}) should modify plan.yaml or create a revision.\n" +
            $"YAML changed: {updatedYaml != originalYaml}\n" +
            $"Has revisions: {hasRevisions}");
    }
}
