using Ivy.Tendril.Mcp;
using Ivy.Tendril.Mcp.Tools;
using Xunit;

namespace Ivy.Tendril.Test.Mcp;

public class PlanToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalTendrilHome;
    private readonly string? _originalToken;
    private readonly PlanTools _planTools;

    public PlanToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalToken = Environment.GetEnvironmentVariable("TENDRIL_MCP_TOKEN");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null); // No auth for tests
        _planTools = new PlanTools(new McpAuthenticationService());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", _originalToken);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void TestTransitionPlanSuccess()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, "Plans");
        var planFolder = Path.Combine(plansDir, "00001-TestPlan");
        Directory.CreateDirectory(planFolder);

        var planYaml = """
            state: Draft
            project: TestProject
            level: NiceToHave
            title: Test Plan
            repos: []
            commits: []
            prs: []
            created: 2026-04-01T10:00:00.0000000Z
            updated: 2026-04-01T10:00:00.0000000Z
            verifications: []
            relatedPlans: []
            dependsOn: []
            executionProfile: balanced
            initialPrompt: Test prompt
            sourceUrl:
            priority: 0
            """;

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        // Act
        var result = _planTools.TransitionPlan("00001", "Icebox");

        // Assert
        Assert.Contains("Successfully transitioned", result);
        Assert.Contains("Draft to Icebox", result);

        var updatedYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        Assert.Contains("state: Icebox", updatedYaml);
    }

    [Fact]
    public void TestTransitionPlanInvalidState()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, "Plans");
        var planFolder = Path.Combine(plansDir, "00001-TestPlan");
        Directory.CreateDirectory(planFolder);

        var planYaml = """
            state: Draft
            project: TestProject
            level: NiceToHave
            title: Test Plan
            repos: []
            commits: []
            prs: []
            created: 2026-04-01T10:00:00.0000000Z
            updated: 2026-04-01T10:00:00.0000000Z
            verifications: []
            relatedPlans: []
            dependsOn: []
            executionProfile: balanced
            initialPrompt: Test prompt
            sourceUrl:
            priority: 0
            """;

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        // Act
        var result = _planTools.TransitionPlan("00001", "InvalidState");

        // Assert
        Assert.Contains("Error: Invalid state", result);
        Assert.Contains("InvalidState", result);
    }

    [Fact]
    public void TestTransitionPlanNonexistentPlan()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, "Plans");
        Directory.CreateDirectory(plansDir);

        // Act
        var result = _planTools.TransitionPlan("99999", "Draft");

        // Assert
        Assert.Contains("Error: Plan '99999' not found", result);
    }

    [Fact]
    public void TestTransitionPlanPreservesFields()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, "Plans");
        var planFolder = Path.Combine(plansDir, "00001-TestPlan");
        Directory.CreateDirectory(planFolder);

        var planYaml = """
            state: Draft
            project: TestProject
            level: Critical
            title: Test Plan With Many Fields
            repos:
            - D:\Repos\TestRepo
            commits:
            - abc123
            prs:
            - https://github.com/test/repo/pull/1
            created: 2026-04-01T10:00:00.0000000Z
            updated: 2026-04-01T10:00:00.0000000Z
            verifications:
            - name: DotnetBuild
              status: Pending
            - name: DotnetTest
              status: Skipped
            relatedPlans:
            - D:\Plans\00002-RelatedPlan
            dependsOn:
            - D:\Plans\00003-Dependency
            executionProfile: performance
            initialPrompt: Test prompt with details
            sourceUrl: https://example.com
            priority: 10
            sessionId: session-123
            """;

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        // Act
        var result = _planTools.TransitionPlan("00001", "Executing");

        // Assert
        Assert.Contains("Successfully transitioned", result);

        var updatedYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        Assert.Contains("state: Executing", updatedYaml);
        Assert.Contains("project: TestProject", updatedYaml);
        Assert.Contains("level: Critical", updatedYaml);
        Assert.Contains("title: Test Plan With Many Fields", updatedYaml);
        Assert.Contains("- D:\\Repos\\TestRepo", updatedYaml);
        Assert.Contains("- abc123", updatedYaml);
        Assert.Contains("https://github.com/test/repo/pull/1", updatedYaml);
        Assert.Contains("name: DotnetBuild", updatedYaml);
        Assert.Contains("status: Pending", updatedYaml);
        Assert.Contains("- D:\\Plans\\00002-RelatedPlan", updatedYaml);
        Assert.Contains("- D:\\Plans\\00003-Dependency", updatedYaml);
        Assert.Contains("executionProfile: performance", updatedYaml);
        Assert.Contains("initialPrompt: Test prompt with details", updatedYaml);
        Assert.Contains("sourceUrl: https://example.com", updatedYaml);
        Assert.Contains("priority: 10", updatedYaml);
        Assert.Contains("sessionId: session-123", updatedYaml);
    }

    [Fact]
    public void TestTransitionPlanUpdatesTimestamp()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, "Plans");
        var planFolder = Path.Combine(plansDir, "00001-TestPlan");
        Directory.CreateDirectory(planFolder);

        var oldTimestamp = DateTime.UtcNow.AddHours(-1);
        var planYaml = $"""
            state: Draft
            project: TestProject
            level: NiceToHave
            title: Test Plan
            repos: []
            commits: []
            prs: []
            created: {oldTimestamp:O}
            updated: {oldTimestamp:O}
            verifications: []
            relatedPlans: []
            dependsOn: []
            executionProfile: balanced
            initialPrompt: Test prompt
            sourceUrl:
            priority: 0
            """;

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        // Act
        var beforeTransition = DateTime.UtcNow;
        var result = _planTools.TransitionPlan("00001", "Icebox");
        var afterTransition = DateTime.UtcNow;

        // Assert
        Assert.Contains("Successfully transitioned", result);

        var updatedYaml = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));

        // Extract the updated timestamp
        var updatedMatch = System.Text.RegularExpressions.Regex.Match(updatedYaml, @"updated: (.+)");
        Assert.True(updatedMatch.Success, "Updated timestamp not found in YAML");

        var updatedTimestamp = DateTime.Parse(updatedMatch.Groups[1].Value).ToUniversalTime();

        // Verify the timestamp was updated to a recent time
        Assert.True(updatedTimestamp >= beforeTransition, "Updated timestamp should be after transition started");
        Assert.True(updatedTimestamp <= afterTransition.AddSeconds(1), "Updated timestamp should be before transition completed");
        Assert.True(updatedTimestamp > oldTimestamp, "Updated timestamp should be newer than original");
    }

    [Fact]
    public void TestTransitionPlanTendrilHomeNotSet()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_HOME", null);

        // Act
        var result = _planTools.TransitionPlan("00001", "Draft");

        // Assert
        Assert.Contains("Error: TENDRIL_HOME is not set", result);
    }
}
