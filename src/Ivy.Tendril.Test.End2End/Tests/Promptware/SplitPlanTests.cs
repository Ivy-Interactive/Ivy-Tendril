using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class SplitPlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public SplitPlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SplitPlan_CreatesChildPlans_MarksOriginalSkipped()
    {
        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            "Large Refactoring Task",
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
            extraValues: new Dictionary<string, string>
            {
                ["PlansDirectory"] = _fixture.PlansDir
            });

        PromptwareAssertions.AssertExitSuccess(result, "SplitPlan");

        // Original plan should be marked as Skipped (split into children)
        var planYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var isSkipped = planYaml.Contains("state: Skipped", StringComparison.OrdinalIgnoreCase) ||
                        planYaml.Contains("state: Split", StringComparison.OrdinalIgnoreCase);

        // Child plans should have been created
        var planCountAfter = Directory.GetDirectories(_fixture.PlansDir)
            .Count(d => !Path.GetFileName(d).StartsWith("."));

        Assert.True(planCountAfter > planCountBefore || isSkipped,
            $"SplitPlan should create child plans or mark original as Skipped/Split.\n" +
            $"Plans before: {planCountBefore}, after: {planCountAfter}\n" +
            $"Original state: {(isSkipped ? "Skipped/Split" : "unchanged")}\n" +
            $"plan.yaml:\n{planYaml[..Math.Min(500, planYaml.Length)]}");
    }
}
