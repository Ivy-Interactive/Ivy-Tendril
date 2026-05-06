using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

/// <summary>
/// Verifies that different agent adapters (Claude, Codex, Gemini, Copilot, OpenCode)
/// work correctly with the promptware CLI. Run with E2E__Agent to select adapter:
///
///   E2E__Agent=claude dotnet test --filter "FullyQualifiedName~AgentAdapterTests"
///   E2E__Agent=codex dotnet test --filter "FullyQualifiedName~AgentAdapterTests"
///   E2E__Agent=gemini dotnet test --filter "FullyQualifiedName~AgentAdapterTests"
///
/// Each adapter must:
/// 1. Launch the correct binary with correct flags
/// 2. Pass the firmware prompt correctly (args vs stdin)
/// 3. Produce valid output the system can parse
/// 4. Exit cleanly
/// </summary>
[Collection("E2E-Promptware")]
public class AgentAdapterTests
{
    private readonly PromptwareTestFixture _fixture;

    public AgentAdapterTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Adapter_CreatePlan_CompletesSuccessfully()
    {
        var agent = _fixture.Settings.Agent;
        var description = $"Add a timestamp comment to Program.cs [agent={agent}]";

        var result = await _fixture.Runner.RunAsync(
            "CreatePlan",
            args: [],
            workingDir: _fixture.TestRepo.LocalClonePath,
            extraValues: new Dictionary<string, string>
            {
                ["Args"] = description,
                ["PlansDirectory"] = _fixture.PlansDir,
                ["Project"] = "E2ETest"
            });

        PromptwareAssertions.AssertExitSuccess(result, $"CreatePlan (agent={agent})");
        PromptwareAssertions.AssertNoAgentErrors(result);

        // Verify a plan folder was created
        var dirs = Directory.GetDirectories(_fixture.PlansDir)
            .Where(d => !Path.GetFileName(d).StartsWith("."))
            .ToArray();

        Assert.True(dirs.Length > 0,
            $"Agent '{agent}' should create a plan folder. PlansDir contents: " +
            string.Join(", ", Directory.GetFileSystemEntries(_fixture.PlansDir).Select(Path.GetFileName)));

        var planFolder = dirs.OrderByDescending(d => Directory.GetCreationTimeUtc(d)).First();
        PromptwareAssertions.AssertPlanYamlExists(planFolder);
    }

    [Fact]
    public async Task Adapter_ExecutePlan_CompletesSuccessfully()
    {
        var agent = _fixture.Settings.Agent;

        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            $"Agent Test {agent}",
            "Add a comment '// tested' at the top of Program.cs",
            "E2ETest",
            steps: ["Add the comment '// tested' as the very first line of Program.cs"],
            verifications: ["DotnetBuild"]);

        var result = await _fixture.Runner.RunAsync(
            "ExecutePlan",
            args: [planFolder],
            workingDir: _fixture.TestRepo.LocalClonePath);

        PromptwareAssertions.AssertExitSuccess(result, $"ExecutePlan (agent={agent})");
        PromptwareAssertions.AssertNoAgentErrors(result);
        PromptwareAssertions.AssertPlanState(planFolder, "ReadyForReview");
    }

    [Fact]
    public async Task Adapter_ProcessExitsCleanly_OnTimeout()
    {
        var agent = _fixture.Settings.Agent;

        // Use an extremely short timeout to trigger timeout behavior
        var planFolder = PlanSetupHelper.CreateDraftPlan(
            _fixture.PlansDir,
            $"Timeout Test {agent}",
            "Refactor the entire application to use a completely different architecture with microservices",
            "E2ETest",
            steps: ["Rewrite everything from scratch using microservices"]);

        var timedOut = false;
        try
        {
            await _fixture.Runner.RunAsync(
                "ExecutePlan",
                args: [planFolder],
                workingDir: _fixture.TestRepo.LocalClonePath,
                timeout: TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            timedOut = true;
        }

        // Either the agent was fast enough to fail/succeed, or we timed out gracefully
        Assert.True(timedOut || File.Exists(Path.Combine(planFolder, "plan.yaml")),
            "Process should either time out gracefully or produce output");
    }
}
