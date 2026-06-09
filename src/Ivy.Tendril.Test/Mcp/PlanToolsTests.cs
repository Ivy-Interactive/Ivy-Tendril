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
        Assert.Contains("Found 2 plans:", result);
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

        var logsDir = Path.Combine(planFolder, "Logs");
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

    // --- PlanCreate (direct) ---

    [Fact]
    public void PlanCreate_ReturnsIdAndFolder()
    {
        var plansDir = Path.Combine(_tempDir, "Plans");
        Directory.CreateDirectory(plansDir);

        var result = _planTools.PlanCreate("Direct Plan Test", repos: _repoDir);

        Assert.Contains("Plan created:", result);
        Assert.Contains("PlanId:", result);
        Assert.Contains("Directory:", result);
    }

    [Fact]
    public void PlanCreate_WithAllOptions_SetsAllFields()
    {
        var plansDir = Path.Combine(_tempDir, "Plans");
        Directory.CreateDirectory(plansDir);

        var result = _planTools.PlanCreate(
            "Full Plan",
            project: "MyProject",
            level: "Critical",
            initialPrompt: "Do the thing",
            executionProfile: "deep",
            sourceUrl: "https://github.com/org/repo/issues/1",
            repos: _repoDir,
            verifications: "DotnetBuild,DotnetTest");

        Assert.Contains("Plan created:", result);

        var planId = result.Split('\n')
            .First(l => l.StartsWith("PlanId:"))
            .Replace("PlanId: ", "").Trim();

        var plan = _planTools.GetPlan(planId);
        Assert.Contains("MyProject", plan);
        Assert.Contains("Critical", plan);
        Assert.Contains("Do the thing", plan);
    }

    // --- WriteRevision ---

    [Fact]
    public void WriteRevision_CreatesRevisionFile()
    {
        var planFolder = CreateTestPlan();

        var result = _planTools.WriteRevision("00001", "# Test Revision\n\nContent here.");

        Assert.Contains("Revision written: 001.md", result);
        var revFile = Path.Combine(planFolder, "Revisions", "001.md");
        Assert.True(File.Exists(revFile));
        Assert.Contains("Test Revision", File.ReadAllText(revFile));
    }

    [Fact]
    public void WriteRevision_IncrementsNumber()
    {
        var planFolder = CreateTestPlan();
        var revisionsDir = Path.Combine(planFolder, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "First");

        var result = _planTools.WriteRevision("00001", "Second revision");

        Assert.Contains("002.md", result);
    }

    // --- AddRelated / RemoveRelated ---

    [Fact]
    public void AddRelated_AddsLink()
    {
        CreateTestPlan();
        CreateTestPlan("00010", "OtherPlan");

        var result = _planTools.AddRelated("00001", "00010-OtherPlan");

        Assert.Contains("Added related plan", result);
        var plan = _planTools.GetPlan("00001", "relatedPlans");
        Assert.Contains("00010-OtherPlan", plan);
    }

    [Fact]
    public void AddRelated_Duplicate_IsIdempotent()
    {
        CreateTestPlan();
        CreateTestPlan("00010", "OtherPlan");
        _planTools.AddRelated("00001", "00010-OtherPlan");

        var result = _planTools.AddRelated("00001", "00010-OtherPlan");

        Assert.Contains("Added related plan", result);
    }

    [Fact]
    public void RemoveRelated_RemovesLink()
    {
        CreateTestPlan();
        CreateTestPlan("00010", "OtherPlan");
        _planTools.AddRelated("00001", "00010-OtherPlan");

        var result = _planTools.RemoveRelated("00001", "00010-OtherPlan");

        Assert.Contains("Removed related plan", result);
        var plan = _planTools.GetPlan("00001", "relatedPlans");
        Assert.DoesNotContain("00010-OtherPlan", plan);
    }

    [Fact]
    public void RemoveRelated_NotFound_ReturnsError()
    {
        CreateTestPlan();

        var result = _planTools.RemoveRelated("00001", "NonExistent");

        Assert.Contains("Error:", result);
    }

    // --- AddDependsOn / RemoveDependsOn ---

    [Fact]
    public void AddDependsOn_AddsDependency()
    {
        CreateTestPlan();
        CreateTestPlan("00005", "BasePlan");

        var result = _planTools.AddDependsOn("00001", "00005-BasePlan");

        Assert.Contains("Added dependency", result);
        var plan = _planTools.GetPlan("00001", "dependsOn");
        Assert.Contains("00005-BasePlan", plan);
    }

    [Fact]
    public void AddDependsOn_Duplicate_IsIdempotent()
    {
        CreateTestPlan();
        CreateTestPlan("00005", "BasePlan");
        _planTools.AddDependsOn("00001", "00005-BasePlan");

        var result = _planTools.AddDependsOn("00001", "00005-BasePlan");

        Assert.Contains("Added dependency", result);
    }

    [Fact]
    public void RemoveDependsOn_RemovesDependency()
    {
        CreateTestPlan();
        CreateTestPlan("00005", "BasePlan");
        _planTools.AddDependsOn("00001", "00005-BasePlan");

        var result = _planTools.RemoveDependsOn("00001", "00005-BasePlan");

        Assert.Contains("Removed dependency", result);
        var plan = _planTools.GetPlan("00001", "dependsOn");
        Assert.DoesNotContain("00005-BasePlan", plan);
    }

    [Fact]
    public void RemoveDependsOn_NotFound_ReturnsError()
    {
        CreateTestPlan();

        var result = _planTools.RemoveDependsOn("00001", "NonExistent");

        Assert.Contains("Error:", result);
    }

    // --- Validate ---

    [Fact]
    public void Validate_ValidPlan_ReturnsValid()
    {
        CreateTestPlan();

        var result = _planTools.Validate("00001");

        Assert.Contains("Plan is valid", result);
    }

    [Fact]
    public void Validate_InvalidPlan_ReturnsFailure()
    {
        var plansDir = Path.Combine(_tempDir, "Plans");
        var planFolder = Path.Combine(plansDir, "00002-BadPlan");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), """
            state: InvalidState
            project: TestProject
            level: NiceToHave
            title: Bad Plan
            repos:
            - /nonexistent/path
            commits: []
            prs: []
            created: 2026-04-01T10:00:00.0000000Z
            updated: 2026-04-01T10:00:00.0000000Z
            verifications: []
            relatedPlans: []
            dependsOn: []
            priority: 0
            """);

        var result = _planTools.Validate("00002");

        Assert.Contains("Validation failed:", result);
    }

    // --- VerificationList ---

    [Fact]
    public void VerificationList_ReturnsAll()
    {
        CreateTestPlan();
        _planTools.VerificationAdd("00001", "Build");
        _planTools.VerificationAdd("00001", "Test", "Pass");

        var result = _planTools.VerificationList("00001");

        Assert.Contains("Build", result);
        Assert.Contains("Test", result);
    }

    [Fact]
    public void VerificationList_FilterByStatus()
    {
        CreateTestPlan();
        _planTools.VerificationAdd("00001", "Build", "Pending");
        _planTools.VerificationAdd("00001", "Test", "Pass");

        var result = _planTools.VerificationList("00001", "Pass");

        Assert.Contains("Test", result);
        Assert.DoesNotContain("Build", result);
    }

    // --- VerificationAdd ---

    [Fact]
    public void VerificationAdd_AddsNew()
    {
        CreateTestPlan();

        var result = _planTools.VerificationAdd("00001", "NewCheck");

        Assert.Contains("Added verification", result);
        var list = _planTools.VerificationList("00001");
        Assert.Contains("NewCheck", list);
        Assert.Contains("Pending", list);
    }

    [Fact]
    public void VerificationAdd_Duplicate_ReturnsError()
    {
        CreateTestPlan();
        _planTools.VerificationAdd("00001", "Build");

        var result = _planTools.VerificationAdd("00001", "Build");

        Assert.Contains("Error:", result);
    }

    // --- VerificationRemove ---

    [Fact]
    public void VerificationRemove_Removes()
    {
        CreateTestPlan();
        _planTools.VerificationAdd("00001", "Build");

        var result = _planTools.VerificationRemove("00001", "Build");

        Assert.Contains("Removed verification", result);
        var list = _planTools.VerificationList("00001");
        Assert.DoesNotContain("Build", list);
    }

    [Fact]
    public void VerificationRemove_NotFound_ReturnsError()
    {
        CreateTestPlan();

        var result = _planTools.VerificationRemove("00001", "NonExistent");

        Assert.Contains("Error:", result);
    }

    // --- RecSet ---

    [Fact]
    public void RecSet_UpdatesDescription()
    {
        CreateTestPlan();
        _planTools.RecAdd("00001", "MyRec", "Original desc");

        var result = _planTools.RecSet("00001", "MyRec", "description", "Updated desc");

        Assert.Contains("Updated recommendation", result);
        var list = _planTools.RecList("00001");
        Assert.Contains("MyRec", list);
    }

    [Fact]
    public void RecSet_UnknownField_ReturnsError()
    {
        CreateTestPlan();
        _planTools.RecAdd("00001", "MyRec", "desc");

        var result = _planTools.RecSet("00001", "MyRec", "nonexistent", "value");

        Assert.Contains("Error:", result);
        Assert.Contains("Unknown field", result);
    }

    [Fact]
    public void RecSet_NotFound_ReturnsError()
    {
        CreateTestPlan();

        var result = _planTools.RecSet("00001", "NonExistent", "description", "value");

        Assert.Contains("Error:", result);
    }
}