using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class UpdateProjectTests
{
    private readonly PromptwareTestFixture _fixture;

    public UpdateProjectTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task UpdateProject_ConfiguresVerifications(string agent)
    {
        var cliLog = Path.Combine(_fixture.TendrilHome, $"update-project-{agent}.jsonl");

        var result = await _fixture.Runner.RunAsync(
            "UpdateProject",
            args: [],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            cliLogPath: cliLog,
            extraValues: new Dictionary<string, string>
            {
                ["Args"] = "Setup verifications for the E2ETest project. Add a dotnet build verification.",
                ["Project"] = "E2ETest"
            });

        PromptwareAssertions.AssertExitSuccess(result, $"UpdateProject ({agent})");

        // Assert expected CLI calls — should use project/verification commands
        var entries = CliLogAssertions.ReadLog(cliLog);
        var hasProjectOrVerificationCall = entries.Any(e =>
            e.Command.Contains("project", StringComparison.OrdinalIgnoreCase) ||
            e.Command.Contains("verification", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasProjectOrVerificationCall,
            $"UpdateProject ({agent}) should call tendril project or verification commands.\n" +
            $"Actual calls: [{string.Join(", ", entries.Select(e => $"\"{e.Command}\""))}]");

        CliLogAssertions.AssertAllCommandsSucceeded(cliLog);
    }
}
