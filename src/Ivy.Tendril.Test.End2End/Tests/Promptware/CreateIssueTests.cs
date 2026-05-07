using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class CreateIssueTests
{
    private readonly PromptwareTestFixture _fixture;

    public CreateIssueTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task CreateIssue_CreatesGitHubIssue(string agent)
    {
        var cliLog = Path.Combine(_fixture.TendrilHome, $"create-issue-{agent}.jsonl");

        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            $"Fix Null Reference Bug {agent}",
            "There is a null reference exception when the config file is missing",
            "E2ETest",
            steps: ["Add null check before accessing config properties"]);

        var result = await _fixture.Runner.RunAsync(
            "CreateIssue",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            cliLogPath: cliLog,
            extraValues: new Dictionary<string, string>
            {
                ["Repo"] = _fixture.TestRepo.LocalClonePath
            });

        PromptwareAssertions.AssertExitSuccess(result, $"CreateIssue ({agent})");

        // Assert CLI calls
        CliLogAssertions.AssertAllCommandsSucceeded(cliLog);

        // Verify the issue was created
        var planYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var stdoutAll = string.Join("\n", result.StdoutLines);

        var hasIssueRef = planYaml.Contains("issue", StringComparison.OrdinalIgnoreCase) ||
                          planYaml.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                          stdoutAll.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                          stdoutAll.Contains("issue", StringComparison.OrdinalIgnoreCase);

        Assert.True(hasIssueRef,
            $"CreateIssue ({agent}) should produce an issue URL in plan.yaml or stdout.\n" +
            $"plan.yaml:\n{planYaml[..Math.Min(300, planYaml.Length)]}\n" +
            $"Stdout (last 10 lines): {string.Join("\n", result.StdoutLines.TakeLast(10))}");
    }
}
