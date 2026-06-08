using System.Collections;
using System.Text.Json;
using Ivy.Tendril.Controllers;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class PlanControllerTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;
    private readonly string _repoDir;

    public PlanControllerTests()
    {
        _repoDir = Path.Combine(_tempDir.Path, "repos", "TestRepo");
        Directory.CreateDirectory(_repoDir);
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalTendrilPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _originalTendrilPlans);
        _tempDir.Dispose();
    }

    private string CreateTestPlan(string id = "00001", string title = "Test Plan", string state = "Draft")
    {
        var plansDir = Path.Combine(_tempDir.Path, "Plans");
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

    private static PlanController CreateController()
    {
        var controller = new PlanController(new NullPlanWatcherService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    // --- GetPlan ---

    [Fact]
    public void GetPlan_ReturnsFullPlan()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.GetPlan("00001");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"title\":\"Test Plan\"", json);
        Assert.Contains("\"state\":\"Draft\"", json);
        Assert.Contains("\"project\":\"TestProject\"", json);
    }

    [Fact]
    public void GetPlan_WithField_ReturnsFieldValue()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.GetPlan("00001", "state");

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"value\":\"Draft\"", json);
    }

    [Fact]
    public void GetPlan_NotFound_Returns404()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir.Path, "Plans"));
        var controller = CreateController();

        var result = controller.GetPlan("99999");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- ListPlans ---

    [Fact]
    public void ListPlans_ReturnsAllPlans()
    {
        CreateTestPlan("00001", "First Plan");
        CreateTestPlan("00002", "Second Plan", "Executing");
        var controller = CreateController();

        var result = controller.ListPlans();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("First Plan", json);
        Assert.Contains("Second Plan", json);
    }

    [Fact]
    public void ListPlans_FilterByState()
    {
        CreateTestPlan("00001", "DraftPlan");
        CreateTestPlan("00002", "ExecPlan", "Executing");
        var controller = CreateController();

        var result = controller.ListPlans("Draft");

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("DraftPlan", json);
        Assert.DoesNotContain("ExecPlan", json);
    }

    [Fact]
    public void ListPlans_FilterByProject()
    {
        CreateTestPlan("00001", "MyPlan");
        var controller = CreateController();

        var result = controller.ListPlans(project: "TestProject");

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("MyPlan", json);
    }

    [Fact]
    public void ListPlans_FilterByProject_NoMatch()
    {
        CreateTestPlan("00001", "MyPlan");
        var controller = CreateController();

        var result = controller.ListPlans(project: "OtherProject");

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Equal("[]", json);
    }

    [Fact]
    public void ListPlans_RespectsLimit()
    {
        CreateTestPlan("00001", "Plan1");
        CreateTestPlan("00002", "Plan2");
        CreateTestPlan("00003", "Plan3");
        var controller = CreateController();

        var result = controller.ListPlans(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = ok.Value as IList
                   ?? (ok.Value as IEnumerable)?.Cast<object>().ToList();
        Assert.NotNull(list);
        Assert.Equal(2, list.Count);
    }

    // --- SetField ---

    [Fact]
    public void SetField_UpdatesState()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.SetField("00001", new SetFieldRequest("state", "Icebox"));

        Assert.IsType<OkObjectResult>(result);

        var getResult = controller.GetPlan("00001", "state");
        var ok = Assert.IsType<OkObjectResult>(getResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Icebox", json);
    }

    [Fact]
    public void SetField_UpdatesTitle()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.SetField("00001", new SetFieldRequest("title", "New Title"));

        Assert.IsType<OkObjectResult>(result);

        var getResult = controller.GetPlan("00001", "title");
        var ok = Assert.IsType<OkObjectResult>(getResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("New Title", json);
    }

    [Fact]
    public void SetField_UpdatesPriority()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.SetField("00001", new SetFieldRequest("priority", "5"));

        Assert.IsType<OkObjectResult>(result);

        var getResult = controller.GetPlan("00001", "priority");
        var ok = Assert.IsType<OkObjectResult>(getResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("5", json);
    }

    [Fact]
    public void SetField_InvalidPriority_ReturnsBadRequest()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.SetField("00001", new SetFieldRequest("priority", "abc"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void SetField_UnknownField_ReturnsBadRequest()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.SetField("00001", new SetFieldRequest("nosuchfield", "value"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void SetField_NotFound_Returns404()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir.Path, "Plans"));
        var controller = CreateController();

        var result = controller.SetField("99999", new SetFieldRequest("state", "Draft"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- AddRepo / RemoveRepo ---

    [Fact]
    public void AddRepo_AddsRepository()
    {
        CreateTestPlan();
        var controller = CreateController();
        var newRepo = Path.Combine(_tempDir.Path, "repos", "AnotherRepo");
        Directory.CreateDirectory(newRepo);

        var result = controller.AddRepo("00001", new AddRepoRequest(newRepo));

        Assert.IsType<OkObjectResult>(result);

        var getResult = controller.GetPlan("00001");
        var ok = Assert.IsType<OkObjectResult>(getResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("AnotherRepo", json);
    }

    [Fact]
    public void AddRepo_Duplicate_ReturnsOkMessage()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.AddRepo("00001", new AddRepoRequest(_repoDir));

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("already in plan", json);
    }

    [Fact]
    public void RemoveRepo_RemovesRepository()
    {
        CreateTestPlan();
        var controller = CreateController();
        var extraRepo = Path.Combine(_tempDir.Path, "repos", "ExtraRepo");
        Directory.CreateDirectory(extraRepo);
        controller.AddRepo("00001", new AddRepoRequest(extraRepo));

        var result = controller.RemoveRepo("00001", new RemoveRepoRequest(extraRepo));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void RemoveRepo_NotFound_Returns404()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.RemoveRepo("00001", new RemoveRepoRequest(@"D:\Repos\NonExistent"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- AddPr ---

    [Fact]
    public void AddPr_AddsPrUrl()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.AddPr("00001", new AddPrRequest("https://github.com/owner/repo/pull/1"));

        Assert.IsType<OkObjectResult>(result);

        var getResult = controller.GetPlan("00001");
        var ok = Assert.IsType<OkObjectResult>(getResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("https://github.com/owner/repo/pull/1", json);
    }

    [Fact]
    public void AddPr_Duplicate_ReturnsOkMessage()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddPr("00001", new AddPrRequest("https://github.com/owner/repo/pull/1"));

        var result = controller.AddPr("00001", new AddPrRequest("https://github.com/owner/repo/pull/1"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("already in plan", json);
    }

    // --- AddCommit ---

    [Fact]
    public void AddCommit_AddsCommitSha()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.AddCommit("00001", new AddCommitRequest("abc123def456"));

        Assert.IsType<OkObjectResult>(result);

        var getResult = controller.GetPlan("00001");
        var ok = Assert.IsType<OkObjectResult>(getResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("abc123def456", json);
    }

    [Fact]
    public void AddCommit_Duplicate_ReturnsOkMessage()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddCommit("00001", new AddCommitRequest("abc123def456"));

        var result = controller.AddCommit("00001", new AddCommitRequest("abc123def456"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("already in plan", json);
    }

    // --- SetVerification ---

    [Fact]
    public void SetVerification_AddsNew()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.SetVerification("00001", new SetVerificationRequest("DotnetBuild", "Pass"));

        Assert.IsType<OkObjectResult>(result);

        var getResult = controller.GetPlan("00001");
        var ok = Assert.IsType<OkObjectResult>(getResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("DotnetBuild", json);
        Assert.Contains("Pass", json);
    }

    [Fact]
    public void SetVerification_UpdatesExisting()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.SetVerification("00001", new SetVerificationRequest("DotnetBuild", "Pending"));

        var result = controller.SetVerification("00001", new SetVerificationRequest("DotnetBuild", "Pass"));

        Assert.IsType<OkObjectResult>(result);
    }

    // --- AddLog ---

    [Fact]
    public void AddLog_WritesLogFile()
    {
        var planFolder = CreateTestPlan();
        var controller = CreateController();

        var result = controller.AddLog("00001", new AddLogRequest("ExecutePlan", "Test summary"));

        Assert.IsType<OkObjectResult>(result);
        var logsDir = Path.Combine(planFolder, "Logs");
        Assert.True(Directory.Exists(logsDir));
        var logFiles = Directory.GetFiles(logsDir, "*.md");
        Assert.Single(logFiles);
    }

    // --- Recommendations ---

    [Fact]
    public void AddRecommendation_AddsRec()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.AddRecommendation("00001",
            new AddRecRequest("Add tests", "Need unit tests", "Medium", "Small"));

        Assert.IsType<OkObjectResult>(result);

        var listResult = controller.ListRecommendations("00001");
        var ok = Assert.IsType<OkObjectResult>(listResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Add tests", json);
    }

    [Fact]
    public void AddRecommendation_Duplicate_ReturnsConflict()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRecommendation("00001", new AddRecRequest("Add tests"));

        var result = controller.AddRecommendation("00001", new AddRecRequest("Add tests"));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void AcceptRecommendation_AcceptsRec()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRecommendation("00001", new AddRecRequest("Add tests"));

        var result = controller.AcceptRecommendation("00001", "Add tests");

        Assert.IsType<OkObjectResult>(result);

        var listResult = controller.ListRecommendations("00001");
        var ok = Assert.IsType<OkObjectResult>(listResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Accepted", json);
    }

    [Fact]
    public void AcceptRecommendation_WithNotes_SetsAcceptedWithNotes()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRecommendation("00001", new AddRecRequest("Add tests"));

        var result = controller.AcceptRecommendation("00001", "Add tests", new AcceptRecRequest("Only integration"));

        Assert.IsType<OkObjectResult>(result);

        var listResult = controller.ListRecommendations("00001");
        var ok = Assert.IsType<OkObjectResult>(listResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("AcceptedWithNotes", json);
    }

    [Fact]
    public void AcceptRecommendation_NotFound_Returns404()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.AcceptRecommendation("00001", "NonExistent");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void DeclineRecommendation_DeclinesRec()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRecommendation("00001", new AddRecRequest("Add tests"));

        var result = controller.DeclineRecommendation("00001", "Add tests", new DeclineRecRequest("Not needed"));

        Assert.IsType<OkObjectResult>(result);

        var listResult = controller.ListRecommendations("00001");
        var ok = Assert.IsType<OkObjectResult>(listResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Declined", json);
    }

    [Fact]
    public void RemoveRecommendation_RemovesRec()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRecommendation("00001", new AddRecRequest("Add tests"));

        var result = controller.RemoveRecommendation("00001", "Add tests");

        Assert.IsType<OkObjectResult>(result);

        var listResult = controller.ListRecommendations("00001");
        var ok = Assert.IsType<OkObjectResult>(listResult);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("Add tests", json);
    }

    [Fact]
    public void RemoveRecommendation_NotFound_Returns404()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.RemoveRecommendation("00001", "NonExistent");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void ListRecommendations_FilterByState()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRecommendation("00001", new AddRecRequest("Rec1"));
        controller.AddRecommendation("00001", new AddRecRequest("Rec2"));
        controller.AcceptRecommendation("00001", "Rec1");

        var pendingResult = controller.ListRecommendations("00001", "Pending");
        var ok1 = Assert.IsType<OkObjectResult>(pendingResult);
        var json1 = JsonSerializer.Serialize(ok1.Value);
        Assert.Contains("Rec2", json1);
        Assert.DoesNotContain("Rec1", json1);

        var acceptedResult = controller.ListRecommendations("00001", "Accepted");
        var ok2 = Assert.IsType<OkObjectResult>(acceptedResult);
        var json2 = JsonSerializer.Serialize(ok2.Value);
        Assert.Contains("Rec1", json2);
        Assert.DoesNotContain("Rec2", json2);
    }

    // --- CreatePlanDirect ---

    [Fact]
    public void CreatePlanDirect_ReturnsIdAndFolder()
    {
        var plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(plansDir);
        var controller = CreateController();

        var result = controller.CreatePlanDirect(new CreatePlanDirectRequest("Test Direct Plan", Repos: [_repoDir]));

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"id\":", json);
        Assert.Contains("\"folder\":", json);
        Assert.Contains("Test Direct Plan", json);
    }

    [Fact]
    public void CreatePlanDirect_WithAllOptions_SetsAllFields()
    {
        var plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(plansDir);
        var controller = CreateController();

        var request = new CreatePlanDirectRequest(
            "Full Plan",
            Project: "MyProject",
            Level: "Critical",
            InitialPrompt: "Do the thing",
            ExecutionProfile: "deep",
            SourceUrl: "https://github.com/org/repo/issues/1",
            Repos: [_repoDir],
            Verifications: ["DotnetBuild", "DotnetTest"],
            RelatedPlans: ["00010-OtherPlan"],
            DependsOn: ["00005-BasePlan"]);

        var result = controller.CreatePlanDirect(request);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"id\":", json);

        var idStr = JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;
        var getPlanResult = controller.GetPlan(idStr);
        var getOk = Assert.IsType<OkObjectResult>(getPlanResult);
        var planJson = JsonSerializer.Serialize(getOk.Value);
        Assert.Contains("MyProject", planJson);
        Assert.Contains("Critical", planJson);
        Assert.Contains("Do the thing", planJson);
        Assert.Contains("deep", planJson);
    }

    // --- WriteRevision ---

    [Fact]
    public void WriteRevision_CreatesRevisionFile()
    {
        var planFolder = CreateTestPlan();
        var controller = CreateController();

        var result = controller.WriteRevision("00001", new WriteRevisionRequest("# Test Revision\n\nContent here."));

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("001.md", json);

        var revFile = Path.Combine(planFolder, "Revisions", "001.md");
        Assert.True(System.IO.File.Exists(revFile));
        Assert.Contains("Test Revision", System.IO.File.ReadAllText(revFile));
    }

    [Fact]
    public void WriteRevision_IncrementsNumber()
    {
        var planFolder = CreateTestPlan();
        var revisionsDir = Path.Combine(planFolder, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        System.IO.File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "First");

        var controller = CreateController();
        var result = controller.WriteRevision("00001", new WriteRevisionRequest("Second revision"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("002.md", json);
    }

    // --- AddRelatedPlan ---

    [Fact]
    public void AddRelatedPlan_AddsLink()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.AddRelatedPlan("00001", new AddRelatedPlanRequest("00010-OtherPlan"));

        Assert.IsType<OkObjectResult>(result);
        var getResult = controller.GetPlan("00001");
        var json = JsonSerializer.Serialize(((OkObjectResult)getResult).Value);
        Assert.Contains("00010-OtherPlan", json);
    }

    [Fact]
    public void AddRelatedPlan_Duplicate_ReturnsOk()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRelatedPlan("00001", new AddRelatedPlanRequest("00010-OtherPlan"));

        var result = controller.AddRelatedPlan("00001", new AddRelatedPlanRequest("00010-OtherPlan"));

        Assert.IsType<OkObjectResult>(result);
    }

    // --- RemoveRelatedPlan ---

    [Fact]
    public void RemoveRelatedPlan_RemovesLink()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRelatedPlan("00001", new AddRelatedPlanRequest("00010-OtherPlan"));

        var result = controller.RemoveRelatedPlan("00001", new RemoveRelatedPlanRequest("00010-OtherPlan"));

        Assert.IsType<OkObjectResult>(result);
        var getResult = controller.GetPlan("00001");
        var json = JsonSerializer.Serialize(((OkObjectResult)getResult).Value);
        Assert.DoesNotContain("00010-OtherPlan", json);
    }

    [Fact]
    public void RemoveRelatedPlan_NotFound_Returns404()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.RemoveRelatedPlan("00001", new RemoveRelatedPlanRequest("NonExistent"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- AddDependsOn ---

    [Fact]
    public void AddDependsOn_AddsDependency()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.AddDependsOn("00001", new AddDependsOnRequest("00005-BasePlan"));

        Assert.IsType<OkObjectResult>(result);
        var getResult = controller.GetPlan("00001");
        var json = JsonSerializer.Serialize(((OkObjectResult)getResult).Value);
        Assert.Contains("00005-BasePlan", json);
    }

    [Fact]
    public void AddDependsOn_Duplicate_ReturnsOk()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddDependsOn("00001", new AddDependsOnRequest("00005-BasePlan"));

        var result = controller.AddDependsOn("00001", new AddDependsOnRequest("00005-BasePlan"));

        Assert.IsType<OkObjectResult>(result);
    }

    // --- RemoveDependsOn ---

    [Fact]
    public void RemoveDependsOn_RemovesDependency()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddDependsOn("00001", new AddDependsOnRequest("00005-BasePlan"));

        var result = controller.RemoveDependsOn("00001", new RemoveDependsOnRequest("00005-BasePlan"));

        Assert.IsType<OkObjectResult>(result);
        var getResult = controller.GetPlan("00001");
        var json = JsonSerializer.Serialize(((OkObjectResult)getResult).Value);
        Assert.DoesNotContain("00005-BasePlan", json);
    }

    [Fact]
    public void RemoveDependsOn_NotFound_Returns404()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.RemoveDependsOn("00001", new RemoveDependsOnRequest("NonExistent"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- ValidatePlan ---

    [Fact]
    public void ValidatePlan_ReturnsValidationResult()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.ValidatePlan("00001");

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"valid\":", json);
    }

    // --- ListVerifications ---

    [Fact]
    public void ListVerifications_ReturnsAll()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddVerification("00001", new AddVerificationRequest("Build"));
        controller.AddVerification("00001", new AddVerificationRequest("Test", "Pass"));

        var result = controller.ListVerifications("00001");

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Build", json);
        Assert.Contains("Test", json);
    }

    [Fact]
    public void ListVerifications_FilterByStatus()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddVerification("00001", new AddVerificationRequest("Build", "Pending"));
        controller.AddVerification("00001", new AddVerificationRequest("Test", "Pass"));

        var result = controller.ListVerifications("00001", "Pass");

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Test", json);
        Assert.DoesNotContain("Build", json);
    }

    // --- AddVerification ---

    [Fact]
    public void AddVerification_AddsNew()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.AddVerification("00001", new AddVerificationRequest("NewCheck"));

        Assert.IsType<OkObjectResult>(result);
        var listResult = controller.ListVerifications("00001");
        var json = JsonSerializer.Serialize(((OkObjectResult)listResult).Value);
        Assert.Contains("NewCheck", json);
        Assert.Contains("Pending", json);
    }

    [Fact]
    public void AddVerification_Duplicate_ReturnsConflict()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddVerification("00001", new AddVerificationRequest("Build"));

        var result = controller.AddVerification("00001", new AddVerificationRequest("Build"));

        Assert.IsType<ConflictObjectResult>(result);
    }

    // --- RemoveVerification ---

    [Fact]
    public void RemoveVerification_Removes()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddVerification("00001", new AddVerificationRequest("Build"));

        var result = controller.RemoveVerification("00001", "Build");

        Assert.IsType<OkObjectResult>(result);
        var listResult = controller.ListVerifications("00001");
        var json = JsonSerializer.Serialize(((OkObjectResult)listResult).Value);
        Assert.DoesNotContain("Build", json);
    }

    [Fact]
    public void RemoveVerification_NotFound_Returns404()
    {
        CreateTestPlan();
        var controller = CreateController();

        var result = controller.RemoveVerification("00001", "NonExistent");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- SetRecField ---

    [Fact]
    public void SetRecField_UpdatesDescription()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRecommendation("00001", new AddRecRequest("MyRec", "Original desc"));

        var result = controller.SetRecField("00001", "MyRec", new SetRecFieldRequest("description", "Updated desc"));

        Assert.IsType<OkObjectResult>(result);
        var listResult = controller.ListRecommendations("00001");
        var json = JsonSerializer.Serialize(((OkObjectResult)listResult).Value);
        Assert.Contains("Updated desc", json);
    }

    [Fact]
    public void SetRecField_UnknownField_ReturnsBadRequest()
    {
        CreateTestPlan();
        var controller = CreateController();
        controller.AddRecommendation("00001", new AddRecRequest("MyRec"));

        var result = controller.SetRecField("00001", "MyRec", new SetRecFieldRequest("nonexistent", "value"));

        Assert.IsType<BadRequestObjectResult>(result);
    }
}

file class NullPlanWatcherService : IPlanWatcherService
{
    public event Action<string?>? PlansChanged;
    public void NotifyChanged(string? changedPlanFolder = null) => PlansChanged?.Invoke(changedPlanFolder);
    public void Dispose() { }
}