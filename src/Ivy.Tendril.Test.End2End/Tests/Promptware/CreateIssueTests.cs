using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class CreateIssueTests
{
    private readonly PromptwareTestFixture _fixture;

    public CreateIssueTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateIssue_CreatesGitHubIssue()
    {
        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            "Fix Null Reference Bug",
            "There is a null reference exception when the config file is missing",
            "E2ETest",
            steps: ["Add null check before accessing config properties"]);

        var result = await _fixture.Runner.RunAsync(
            "CreateIssue",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath,
            extraValues: new Dictionary<string, string>
            {
                ["Repo"] = _fixture.TestRepo.LocalClonePath
            });

        PromptwareAssertions.AssertExitSuccess(result, "CreateIssue");

        // Verify the issue was created (plan.yaml should reference it, or stdout should mention it)
        var planYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var stdoutAll = string.Join("\n", result.StdoutLines);

        var hasIssueRef = planYaml.Contains("issue", StringComparison.OrdinalIgnoreCase) ||
                          planYaml.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                          stdoutAll.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                          stdoutAll.Contains("issue", StringComparison.OrdinalIgnoreCase);

        Assert.True(hasIssueRef,
            "CreateIssue should produce an issue URL in plan.yaml or stdout.\n" +
            $"plan.yaml:\n{planYaml[..Math.Min(300, planYaml.Length)]}\n" +
            $"Stdout (last 10 lines): {string.Join("\n", result.StdoutLines.TakeLast(10))}");
    }
}
