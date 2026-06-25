using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class SetupProjectTests
{
    private readonly PromptwareTestFixture _fixture;

    public SetupProjectTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task SetupProject_ConfiguresVerifications(string agent)
    {
        var cliLog = Path.Combine(_fixture.TendrilHome, $"setup-project-{agent}.jsonl");

        var result = await _fixture.Runner.RunAsync(
            "SetupProject",
            args: [],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            cliLogPath: cliLog,
            extraValues: new Dictionary<string, string>
            {
                ["Instructions"] = "Setup verifications for the E2ETest project. Add a dotnet build verification.",
                ["TendrilProject"] = "E2ETest"
            });

        PromptwareAssertions.AssertExitSuccess(result, $"SetupProject ({agent})");

        // Assert expected CLI calls — should use project/verification commands
        var entries = CliLogAssertions.ReadLog(cliLog);
        var hasProjectOrVerificationCall = entries.Any(e =>
            e.Command.Contains("project", StringComparison.OrdinalIgnoreCase) ||
            e.Command.Contains("verification", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasProjectOrVerificationCall,
            $"SetupProject ({agent}) should call tendril project or verification commands.\n" +
            $"Actual calls: [{string.Join(", ", entries.Select(e => $"\"{e.Command}\""))}]");

        CliLogAssertions.AssertAllCommandsSucceeded(cliLog);
    }
}
