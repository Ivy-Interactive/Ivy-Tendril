using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class SplitPlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public SplitPlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task SplitPlan_CreatesChildPlans_MarksOriginalSkipped(string agent)
    {
        var cliLog = Path.Combine(_fixture.TendrilHome, $"split-plan-{agent}.jsonl");

        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            $"Large Refactoring Task {agent}",
            "Refactor the application into multiple modules with separate concerns",
            "E2ETest",
            steps:
            [
                "Extract logging into a separate module",
                "Extract configuration into a separate module",
                "Extract HTTP handling into a separate module",
                "Update all imports and references",
                "Add integration tests for each module"
            ]);

        var planCountBefore = Directory.GetDirectories(_fixture.PlansDir)
            .Count(d => !Path.GetFileName(d).StartsWith("."));

        var result = await _fixture.Runner.RunAsync(
            "SplitPlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            cliLogPath: cliLog,
            extraValues: new Dictionary<string, string>
            {
                ["PlansDirectory"] = _fixture.PlansDir
            });

        PromptwareAssertions.AssertExitSuccess(result, $"SplitPlan ({agent})");

        // Assert expected CLI calls — should create multiple child plans
        CliLogAssertions.AssertMinimumCalls(cliLog, "plan create", 2);
        CliLogAssertions.AssertAllCommandsSucceeded(cliLog);

        // Original plan should be marked as Skipped/Split
        var planYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var isSkipped = planYaml.Contains("state: Skipped", StringComparison.OrdinalIgnoreCase) ||
                        planYaml.Contains("state: Split", StringComparison.OrdinalIgnoreCase);

        // Child plans should have been created
        var planCountAfter = Directory.GetDirectories(_fixture.PlansDir)
            .Count(d => !Path.GetFileName(d).StartsWith("."));

        Assert.True(planCountAfter > planCountBefore || isSkipped,
            $"SplitPlan ({agent}) should create child plans or mark original as Skipped/Split.\n" +
            $"Plans before: {planCountBefore}, after: {planCountAfter}\n" +
            $"Original state: {(isSkipped ? "Skipped/Split" : "unchanged")}\n" +
            $"plan.yaml:\n{planYaml[..Math.Min(500, planYaml.Length)]}");
    }
}
