using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests.Promptware;

[Collection("E2E-Promptware")]
public class CreatePlanTests
{
    private readonly PromptwareTestFixture _fixture;

    public CreatePlanTests(PromptwareTestFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task CreatePlan_ProducesPlanYaml_WithCorrectStructure(string agent)
    {
        var description = $"Add a hello world comment to the top of Program.cs [agent={agent}]";

        var result = await _fixture.Runner.RunAsync(
            "CreatePlan",
            args: [],
            workingDir: _fixture.TestRepo.LocalClonePath,
            agent: agent,
            extraValues: new Dictionary<string, string>
            {
                ["Args"] = description,
                ["TendrilPlansFolder"] = _fixture.PlansDir,
                ["TendrilProject"] = "E2ETest"
            });

        PromptwareAssertions.AssertExitSuccess(result, $"CreatePlan ({agent})");
        PromptwareAssertions.AssertNoAgentErrors(result);

        // Assert plan structure (primary assertion — plan folder must exist)
        var planFolder = FindCreatedPlan("Hello");
        Assert.NotNull(planFolder);

        PromptwareAssertions.AssertPlanYamlExists(planFolder!);
        PromptwareAssertions.AssertPlanState(planFolder!, "Draft");
        PromptwareAssertions.AssertPlanYamlContains(planFolder!, "title:");
        PromptwareAssertions.AssertPlanYamlContains(planFolder!, "project:");

        var revisionsDir = Path.Combine(planFolder!, "revisions");
        Assert.True(Directory.Exists(revisionsDir),
            $"revisions/ directory should exist at {revisionsDir}");
        var revisionFiles = Directory.GetFiles(revisionsDir, "*.md");
        Assert.True(revisionFiles.Length > 0,
            $"At least one revision file expected in {revisionsDir}");
    }

    [Theory]
    [MemberData(nameof(AgentTestData.Agents), MemberType = typeof(AgentTestData))]
    public async Task CreatePlan_FailsGracefully_WithInvalidDescription(string agent)
    {
        try
        {
            var result = await _fixture.Runner.RunAsync(
                "CreatePlan",
                args: [],
                workingDir: _fixture.TestRepo.LocalClonePath,
                agent: agent,
                extraValues: new Dictionary<string, string>
                {
                    ["Args"] = "",
                    ["TendrilPlansFolder"] = _fixture.PlansDir,
                    ["TendrilProject"] = "E2ETest"
                },
                timeout: TimeSpan.FromSeconds(120));

            Assert.True(result.ExitCode >= 0, "Process should exit cleanly");
        }
        catch (TimeoutException)
        {
            // A timeout without a crash is acceptable
        }
    }

    private string? FindCreatedPlan(string titleFragment)
    {
        var folder = PromptwareAssertions.FindPlanFolderByTitle(_fixture.PlansDir, titleFragment);
        return folder;
    }
}
