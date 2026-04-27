using Ivy.Tendril.Mcp;
using Ivy.Tendril.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Mcp;

[Collection("TendrilHome")]
public class PlanToolsTests : IDisposable
{
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;
    private readonly string? _originalToken;
    private readonly PlanTools _planTools;
    private readonly string _repoDir;
    private readonly string _tempDir;

    public PlanToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _repoDir = Path.Combine(_tempDir, "repos", "TestRepo");
        Directory.CreateDirectory(_repoDir);
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalTendrilPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");
        _originalToken = Environment.GetEnvironmentVariable("TENDRIL_MCP_TOKEN");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        _planTools = new PlanTools(new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _originalTendrilPlans);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", _originalToken);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateTestPlan(string id = "00001", string title = "Test Plan", string state = "Draft")
    {
        var plansDir = Path.Combine(_tempDir, "Plans");
        var planFolder = Path.Combine(plansDir, $"{id}-{title.Replace(" ", "")}");
        Directory.CreateDirectory(planFolder);

        var planYaml = $"""
                        state: {state}
                        project: TestProject
                        level: NiceToHave
                        title: {title}
                        repos:
                        - {_repoDir}
                        commits: []
                        prs: []
                        created: 2026-04-01T10:00:00.0000000Z
                        updated: 2026-04-01T10:00:00.0000000Z
                        verifications: []
                        relatedPlans: []
                        dependsOn: []
                        priority: 0
                        """;

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);
        return planFolder;
    }

    // --- GetPlan ---

    [Fact]
    public void GetPlan_ReturnsSummary()
    {
        CreateTestPlan();
        var result = _planTools.GetPlan("00001");
        Assert.Contains("Plan 00001", result);
        Assert.Contains("Test Plan", result);
        Assert.Contains("Draft", result);
    }

    [Fact]
    public void GetPlan_WithField_ReturnsValue()
    {
        CreateTestPlan();
        Assert.Equal("Draft", _planTools.GetPlan("00001", "state"));
        Assert.Equal("TestProject", _planTools.GetPlan("00001", "project"));
        Assert.Equal("NiceToHave", _planTools.GetPlan("00001", "level"));
        Assert.Equal("Test Plan", _planTools.GetPlan("00001", "title"));
    }

    [Fact]
    public void GetPlan_NotFound_ReturnsError()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Plans"));
        var result = _planTools.GetPlan("99999");
        Assert.Contains("Error:", result);
    }

    // --- ListPlans ---

    [Fact]
    public void ListPlans_ReturnsMatchingPlans()
    {
        CreateTestPlan("00001", "First Plan");
        CreateTestPlan("00002", "Second Plan", "Executing");

        var result = _planTools.ListPlans();
        Assert.Contains("First Plan", result);
        Assert.Contains("Second Plan", result);
        Assert.Contains("Found 2 plan(s)", result);
    }

    [Fact]
    public void ListPlans_FilterByState()
    {
        CreateTestPlan("00001", "Draft Plan");
        CreateTestPlan("00002", "Executing Plan", "Executing");

        var result = _planTools.ListPlans("Draft");
        Assert.Contains("Draft Plan", result);
        Assert.DoesNotContain("Executing Plan", result);
    }

    [Fact]
    public void ListPlans_FilterByProject()
    {
        CreateTestPlan("00001", "MyPlan");

        var result = _planTools.ListPlans(project: "TestProject");
        Assert.Contains("MyPlan", result);

        var result2 = _planTools.ListPlans(project: "OtherProject");
        Assert.Contains("No plans found", result2);
    }

    // --- SetField ---

    [Fact]
    public void SetField_UpdatesState()
    {
        CreateTestPlan();
        var result = _planTools.SetField("00001", "state", "Icebox");
        Assert.Contains("Updated state", result);
        Assert.Equal("Icebox", _planTools.GetPlan("00001", "state"));
    }

    [Fact]
    public void SetField_UpdatesTitle()
    {
        CreateTestPlan();
        var result = _planTools.SetField("00001", "title", "New Title");
        Assert.Contains("Updated title", result);
        Assert.Equal("New Title", _planTools.GetPlan("00001", "title"));
    }

    [Fact]
    public void SetField_UpdatesPriority()
    {
        CreateTestPlan();
        var result = _planTools.SetField("00001", "priority", "5");
        Assert.Contains("Updated priority", result);
        Assert.Equal("5", _planTools.GetPlan("00001", "priority"));
    }

    [Fact]
    public void SetField_InvalidPriority_ReturnsError()
    {
        CreateTestPlan();
        var result = _planTools.SetField("00001", "priority", "abc");
        Assert.Contains("Error:", result);
    }

    [Fact]
    public void SetField_UnknownField_ReturnsError()
    {
        CreateTestPlan();
        var result = _planTools.SetField("00001", "nosuchfield", "value");
        Assert.Contains("Error: Unknown field", result);
    }

    [Fact]
    public void SetField_NonexistentPlan_ReturnsError()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Plans"));
        var result = _planTools.SetField("99999", "state", "Draft");
        Assert.Contains("Error:", result);
    }

    // --- AddRepo / RemoveRepo ---

    [Fact]
    public void AddRepo_AddsRepository()
    {
        CreateTestPlan();
        var newRepo = Path.Combine(_tempDir, "repos", "AnotherRepo");
        Directory.CreateDirectory(newRepo);

        var result = _planTools.AddRepo("00001", newRepo);
        Assert.Contains("Added repository", result);

        var repos = _planTools.GetPlan("00001", "repos");
        Assert.Contains("AnotherRepo", repos);
    }

    [Fact]
    public void AddRepo_Duplicate_ReturnsMessage()
    {
        CreateTestPlan();
        var result = _planTools.AddRepo("00001", _repoDir);
        Assert.Contains("already in plan", result);
    }

    [Fact]
    public void RemoveRepo_RemovesRepository()
    {
        CreateTestPlan();
        var extraRepo = Path.Combine(_tempDir, "repos", "ExtraRepo");
        Directory.CreateDirectory(extraRepo);
        _planTools.AddRepo("00001", extraRepo);

        var result = _planTools.RemoveRepo("00001", extraRepo);
        Assert.Contains("Removed repository", result);

        var repos = _planTools.GetPlan("00001", "repos");
        Assert.DoesNotContain("ExtraRepo", repos);
    }

    [Fact]
    public void RemoveRepo_NotFound_ReturnsError()
    {
        CreateTestPlan();
        var result = _planTools.RemoveRepo("00001", @"D:\Repos\NonExistent");
        Assert.Contains("Error:", result);
    }

    // --- AddPr ---

    [Fact]
    public void AddPr_AddsPrUrl()
    {
        CreateTestPlan();
        var result = _planTools.AddPr("00001", "https://github.com/owner/repo/pull/1");
        Assert.Contains("Added PR", result);

        var prs = _planTools.GetPlan("00001", "prs");
        Assert.Contains("https://github.com/owner/repo/pull/1", prs);
    }

    [Fact]
    public void AddPr_Duplicate_ReturnsMessage()
    {
        CreateTestPlan();
        _planTools.AddPr("00001", "https://github.com/owner/repo/pull/1");
        var result = _planTools.AddPr("00001", "https://github.com/owner/repo/pull/1");
        Assert.Contains("already in plan", result);
    }

    // --- AddCommit ---

    [Fact]
    public void AddCommit_AddsCommitSha()
    {
        CreateTestPlan();
        var result = _planTools.AddCommit("00001", "abc123def456");
        Assert.Contains("Added commit", result);

        var commits = _planTools.GetPlan("00001", "commits");
        Assert.Contains("abc123def456", commits);
    }

    [Fact]
    public void AddCommit_Duplicate_ReturnsMessage()
    {
        CreateTestPlan();
        _planTools.AddCommit("00001", "abc123def456");
        var result = _planTools.AddCommit("00001", "abc123def456");
        Assert.Contains("already in plan", result);
    }

    // --- SetVerification ---

    [Fact]
    public void SetVerification_AddsNew()
    {
        CreateTestPlan();
        var result = _planTools.SetVerification("00001", "DotnetBuild", "Pass");
        Assert.Contains("Set verification", result);

        var verifications = _planTools.GetPlan("00001", "verifications");
        Assert.Contains("DotnetBuild=Pass", verifications);
    }

    [Fact]
    public void SetVerification_UpdatesExisting()
    {
        CreateTestPlan();
        _planTools.SetVerification("00001", "DotnetBuild", "Pending");
        _planTools.SetVerification("00001", "DotnetBuild", "Pass");

        var verifications = _planTools.GetPlan("00001", "verifications");
        Assert.Contains("DotnetBuild=Pass", verifications);
    }

    // --- AddLog ---

    [Fact]
    public void AddLog_WritesLogFile()
    {
        var planFolder = CreateTestPlan();
        var result = _planTools.AddLog("00001", "ExecutePlan", "Test summary");
        Assert.Contains("Log written", result);

        var logsDir = Path.Combine(planFolder, "logs");
        Assert.True(Directory.Exists(logsDir));
        var logFiles = Directory.GetFiles(logsDir, "*.md");
        Assert.Single(logFiles);
        Assert.Contains("001-ExecutePlan.md", Path.GetFileName(logFiles[0]));

        var content = File.ReadAllText(logFiles[0]);
        Assert.Contains("Test summary", content);
    }

    // --- Recommendations ---

    [Fact]
    public void RecAdd_AddsRecommendation()
    {
        CreateTestPlan();
        var result = _planTools.RecAdd("00001", "Add tests", "Need unit tests", "Medium", "Small");
        Assert.Contains("Added recommendation", result);

        var recs = _planTools.RecList("00001");
        Assert.Contains("Add tests", recs);
        Assert.Contains("Medium", recs);
    }

    [Fact]
    public void RecAdd_Duplicate_ReturnsError()
    {
        CreateTestPlan();
        _planTools.RecAdd("00001", "Add tests", "Need unit tests");
        var result = _planTools.RecAdd("00001", "Add tests", "Duplicate");
        Assert.Contains("Error:", result);
        Assert.Contains("already exists", result);
    }

    [Fact]
    public void RecAccept_AcceptsRecommendation()
    {
        CreateTestPlan();
        _planTools.RecAdd("00001", "Add tests", "Need unit tests");
        var result = _planTools.RecAccept("00001", "Add tests");
        Assert.Contains("Accepted", result);

        var recs = _planTools.RecList("00001");
        Assert.Contains("Accepted", recs);
    }

    [Fact]
    public void RecAccept_WithNotes_SetsAcceptedWithNotes()
    {
        CreateTestPlan();
        _planTools.RecAdd("00001", "Add tests", "Need unit tests");
        _planTools.RecAccept("00001", "Add tests", "Only integration tests");

        var recs = _planTools.RecList("00001");
        Assert.Contains("AcceptedWithNotes", recs);
    }

    [Fact]
    public void RecDecline_DeclinesRecommendation()
    {
        CreateTestPlan();
        _planTools.RecAdd("00001", "Add tests", "Need unit tests");
        var result = _planTools.RecDecline("00001", "Add tests", "Not needed");
        Assert.Contains("Declined", result);

        var recs = _planTools.RecList("00001");
        Assert.Contains("Declined", recs);
    }

    [Fact]
    public void RecRemove_RemovesRecommendation()
    {
        CreateTestPlan();
        _planTools.RecAdd("00001", "Add tests", "Need unit tests");
        var result = _planTools.RecRemove("00001", "Add tests");
        Assert.Contains("Removed", result);

        var recs = _planTools.RecList("00001");
        Assert.Contains("No recommendations found", recs);
    }

    [Fact]
    public void RecList_FiltersByState()
    {
        CreateTestPlan();
        _planTools.RecAdd("00001", "Rec1", "Desc1");
        _planTools.RecAdd("00001", "Rec2", "Desc2");
        _planTools.RecAccept("00001", "Rec1");

        var pending = _planTools.RecList("00001", "Pending");
        Assert.Contains("Rec2", pending);
        Assert.DoesNotContain("Rec1", pending);

        var accepted = _planTools.RecList("00001", "Accepted");
        Assert.Contains("Rec1", accepted);
        Assert.DoesNotContain("Rec2", accepted);
    }

    [Fact]
    public void RecAccept_NotFound_ReturnsError()
    {
        CreateTestPlan();
        var result = _planTools.RecAccept("00001", "NonExistent");
        Assert.Contains("Error:", result);
        Assert.Contains("not found", result);
    }

    // --- Authentication ---

    [Fact]
    public void AuthEnabled_WithoutToken_ReturnsError()
    {
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "secret-token");
        var authedService = new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);

        var authedTools = new PlanTools(authedService);
        var result = authedTools.GetPlan("00001");
        Assert.Contains("Error: Authentication failed", result);
    }

    [Fact]
    public void AllToolMethods_RequireAuthentication()
    {
        // Arrange - enable auth but clear token
        CreateTestPlan();
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "secret-token");
        var authedService = new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        var authedTools = new PlanTools(authedService);

        // Act & Assert - verify all 15 tool methods return auth error
        Assert.Contains("Error: Authentication failed", authedTools.GetPlan("00001"));
        Assert.Contains("Error: Authentication failed", authedTools.ListPlans());
        Assert.Contains("Error: Authentication failed", authedTools.CreatePlan("Test"));
        Assert.Contains("Error: Authentication failed", authedTools.SetField("00001", "state", "Draft"));
        Assert.Contains("Error: Authentication failed", authedTools.AddRepo("00001", _repoDir));
        Assert.Contains("Error: Authentication failed", authedTools.RemoveRepo("00001", _repoDir));
        Assert.Contains("Error: Authentication failed", authedTools.AddPr("00001", "https://github.com/test/pr/1"));
        Assert.Contains("Error: Authentication failed", authedTools.AddCommit("00001", "abc123"));
        Assert.Contains("Error: Authentication failed", authedTools.SetVerification("00001", "Build", "Pass"));
        Assert.Contains("Error: Authentication failed", authedTools.AddLog("00001", "Test"));
        Assert.Contains("Error: Authentication failed", authedTools.RecAdd("00001", "Title", "Desc"));
        Assert.Contains("Error: Authentication failed", authedTools.RecAccept("00001", "Title"));
        Assert.Contains("Error: Authentication failed", authedTools.RecDecline("00001", "Title"));
        Assert.Contains("Error: Authentication failed", authedTools.RecRemove("00001", "Title"));
        Assert.Contains("Error: Authentication failed", authedTools.RecList("00001"));
    }

    // --- TENDRIL_HOME not set ---

    [Fact]
    public void GetPlan_NoTendrilHome_ReturnsError()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", null);
        var result = _planTools.GetPlan("00001");
        Assert.Contains("Error:", result);
    }

    // --- CreatePlan (inbox) ---

    [Fact]
    public void CreatePlan_WritesInboxFile()
    {
        var result = _planTools.CreatePlan("Build a widget", "TestProject");
        Assert.Contains("Plan submitted to inbox", result);

        var inboxDir = Path.Combine(_tempDir, "Inbox");
        Assert.True(Directory.Exists(inboxDir));
        var files = Directory.GetFiles(inboxDir, "*.md");
        Assert.Single(files);

        var content = File.ReadAllText(files[0]);
        Assert.Contains("Build a widget", content);
        Assert.Contains("project: TestProject", content);
    }
}