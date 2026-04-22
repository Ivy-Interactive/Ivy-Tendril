using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class PlanCliCommandTests : IDisposable
{
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;
    private readonly string _plansDir;
    private readonly string _tempDir;

    public PlanCliCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-cli-test-{Guid.NewGuid():N}");
        _plansDir = Path.Combine(_tempDir, "Plans");
        Directory.CreateDirectory(_plansDir);

        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalTendrilPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _originalTendrilPlans);
        if (Directory.Exists(_tempDir))
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
    }

    private string CreatePlanFolder(string id, string title, PlanYaml? plan = null)
    {
        var folderName = $"{id}-{title}";
        var planDir = Path.Combine(_plansDir, folderName);
        Directory.CreateDirectory(planDir);

        plan ??= new PlanYaml
        {
            State = "Draft",
            Project = "TestProject",
            Title = title,
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        var yaml = YamlHelper.Serializer.Serialize(plan);
        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), yaml);
        return planDir;
    }

    private PlanYaml ReadPlan(string planId)
    {
        var folder = PlanCommandHelpers.ResolvePlanFolder(planId);
        return PlanCommandHelpers.ReadPlan(folder);
    }

    // ==================== ResolvePlanFolder ====================

    [Fact]
    public void ResolvePlanFolder_FindsExistingPlan()
    {
        CreatePlanFolder("20001", "ResolveFindTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20001");

        Assert.Contains("20001-ResolveFindTest", folder);
    }

    [Fact]
    public void ResolvePlanFolder_FullPath_Resolves()
    {
        CreatePlanFolder("20003", "FullPathTest");

        var fullPath = Path.Combine(_plansDir, "20003-FullPathTest");
        var folder = PlanCommandHelpers.ResolvePlanFolder(fullPath);

        Assert.Contains("20003-FullPathTest", folder);
    }

    [Fact]
    public void ResolvePlanFolder_FolderName_Resolves()
    {
        CreatePlanFolder("20004", "FolderNameTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20004-FolderNameTest");

        Assert.Contains("20004-FolderNameTest", folder);
    }

    [Fact]
    public void ResolvePlanFolder_BareNumber_Resolves()
    {
        CreatePlanFolder("00015", "BareNumberTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("15");

        Assert.Contains("00015-BareNumberTest", folder);
    }

    [Fact]
    public void ResolvePlanFolder_ZeroPaddedId_Resolves()
    {
        CreatePlanFolder("00016", "PaddedTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("00016");

        Assert.Contains("00016-PaddedTest", folder);
    }

    [Fact]
    public void NormalizePlanId_FullPath_ExtractsId()
    {
        var result = PlanCommandHelpers.NormalizePlanId(
            @"D:\Plans\00015-LogWarning", _plansDir);
        Assert.Equal("00015", result);
    }

    [Fact]
    public void NormalizePlanId_FolderName_ExtractsId()
    {
        var result = PlanCommandHelpers.NormalizePlanId(
            "00015-LogWarningInRemoveWorktrees", _plansDir);
        Assert.Equal("00015", result);
    }

    [Fact]
    public void NormalizePlanId_BareNumber_Pads()
    {
        var result = PlanCommandHelpers.NormalizePlanId("15", _plansDir);
        Assert.Equal("00015", result);
    }

    [Fact]
    public void NormalizePlanId_ZeroPadded_PassesThrough()
    {
        var result = PlanCommandHelpers.NormalizePlanId("00015", _plansDir);
        Assert.Equal("00015", result);
    }

    [Fact]
    public void ResolvePlanFolder_NonExistentPlan_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            PlanCommandHelpers.ResolvePlanFolder("99999"));
    }

    [Fact]
    public void ResolvePlanFolder_MultipleSameId_Throws()
    {
        Directory.CreateDirectory(Path.Combine(_plansDir, "20002-PlanA"));
        Directory.CreateDirectory(Path.Combine(_plansDir, "20002-PlanB"));

        Assert.Throws<InvalidOperationException>(() =>
            PlanCommandHelpers.ResolvePlanFolder("20002"));
    }

    // ==================== PlanCreate ====================

    [Fact]
    public void PlanCreate_CreatesNewPlanYaml()
    {
        var planDir = Path.Combine(_plansDir, "20010-NewPlan");
        Directory.CreateDirectory(planDir);

        var plan = new PlanYaml
        {
            State = "Draft",
            Project = "Auto",
            Level = "NiceToHave",
            Title = "NewPlan",
            Repos = [_tempDir],
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };
        PlanCommandHelpers.WritePlan(planDir, plan);

        var result = PlanCommandHelpers.ReadPlan(planDir);
        Assert.Equal("Draft", result.State);
        Assert.Equal("Auto", result.Project);
        Assert.Equal("NiceToHave", result.Level);
        Assert.Equal("NewPlan", result.Title);
    }

    [Fact]
    public void PlanCreate_ExistingYaml_OverwritesIfCalled()
    {
        var planDir = CreatePlanFolder("20011", "OverwriteTest");

        var plan = new PlanYaml
        {
            State = "Failed",
            Project = "NewProject",
            Title = "OverwriteTest",
            Repos = [_tempDir],
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };
        PlanCommandHelpers.WritePlan(planDir, plan);

        var result = PlanCommandHelpers.ReadPlan(planDir);
        Assert.Equal("Failed", result.State);
        Assert.Equal("NewProject", result.Project);
    }

    // ==================== PlanGet ====================

    [Fact]
    public void PlanGet_ReadsFullYaml()
    {
        CreatePlanFolder("20020", "GetFullTest");

        var plan = ReadPlan("20020");

        Assert.Equal("Draft", plan.State);
        Assert.Equal("TestProject", plan.Project);
        Assert.Equal("GetFullTest", plan.Title);
    }

    [Fact]
    public void PlanGet_ReadsIndividualFields()
    {
        CreatePlanFolder("20021", "GetFieldTest");

        var plan = ReadPlan("20021");

        Assert.Equal("Draft", plan.State);
        Assert.Equal("TestProject", plan.Project);
        Assert.Equal("NiceToHave", plan.Level);
        Assert.Equal("GetFieldTest", plan.Title);
        Assert.Equal(0, plan.Priority);
    }

    // ==================== PlanSet ====================

    [Fact]
    public void PlanSet_UpdatesState()
    {
        CreatePlanFolder("20030", "SetStateTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20030");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.State = "Failed";
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20030");
        Assert.Equal("Failed", result.State);
    }

    [Fact]
    public void PlanSet_UpdatesProject()
    {
        CreatePlanFolder("20031", "SetProjectTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20031");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Project = "NewProject";
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20031");
        Assert.Equal("NewProject", result.Project);
    }

    [Fact]
    public void PlanSet_UpdatesLevel()
    {
        CreatePlanFolder("20032", "SetLevelTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20032");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Level = "Critical";
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20032");
        Assert.Equal("Critical", result.Level);
    }

    [Fact]
    public void PlanSet_UpdatesTitle()
    {
        CreatePlanFolder("20033", "SetTitleTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20033");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Title = "Renamed Title";
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20033");
        Assert.Equal("Renamed Title", result.Title);
    }

    [Fact]
    public void PlanSet_UpdatesPriority()
    {
        CreatePlanFolder("20034", "SetPriorityTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20034");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Priority = 5;
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20034");
        Assert.Equal(5, result.Priority);
    }

    [Fact]
    public void PlanSet_InvalidState_ThrowsOnValidation()
    {
        CreatePlanFolder("20035", "InvalidStateTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20035");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.State = "BogusState";

        Assert.Throws<ArgumentException>(() =>
            PlanCommandHelpers.WritePlan(folder, plan));
    }

    [Fact]
    public void PlanSet_InvalidLevel_ThrowsOnValidation()
    {
        CreatePlanFolder("20036", "InvalidLevelTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20036");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Level = "BogusLevel";

        Assert.Throws<ArgumentException>(() =>
            PlanCommandHelpers.WritePlan(folder, plan));
    }

    [Fact]
    public void PlanSet_EmptyTitle_ThrowsOnValidation()
    {
        CreatePlanFolder("20037", "EmptyTitleTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20037");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Title = "";

        Assert.Throws<ArgumentException>(() =>
            PlanCommandHelpers.WritePlan(folder, plan));
    }

    // ==================== PlanAddRepo / RemoveRepo ====================

    [Fact]
    public void PlanAddRepo_AddsNewRepo()
    {
        CreatePlanFolder("20040", "AddRepoTest");
        var newRepoDir = Path.Combine(_tempDir, "extra-repo");
        Directory.CreateDirectory(newRepoDir);

        var folder = PlanCommandHelpers.ResolvePlanFolder("20040");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Repos.Add(newRepoDir);
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20040");
        Assert.Equal(2, result.Repos.Count);
        Assert.Contains(newRepoDir, result.Repos);
    }

    [Fact]
    public void PlanAddRepo_Idempotent_DoesNotDuplicate()
    {
        CreatePlanFolder("20041", "IdempotentRepoTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20041");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        var existingRepo = plan.Repos[0];

        if (!plan.Repos.Contains(existingRepo, StringComparer.OrdinalIgnoreCase))
            plan.Repos.Add(existingRepo);
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20041");
        Assert.Single(result.Repos);
    }

    [Fact]
    public void PlanRemoveRepo_RemovesExistingRepo()
    {
        var extraDir = Path.Combine(_tempDir, "remove-repo-extra");
        Directory.CreateDirectory(extraDir);

        CreatePlanFolder("20042", "RemoveRepoTest", new PlanYaml
        {
            State = "Draft",
            Project = "TestProject",
            Title = "RemoveRepoTest",
            Repos = [_tempDir, extraDir],
            Created = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
        });

        var folder = PlanCommandHelpers.ResolvePlanFolder("20042");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Repos.RemoveAll(r => r.Equals(extraDir, StringComparison.OrdinalIgnoreCase));
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20042");
        Assert.Single(result.Repos);
        Assert.Equal(_tempDir, result.Repos[0]);
    }

    [Fact]
    public void PlanRemoveRepo_NonExistentRepo_NoMatch()
    {
        CreatePlanFolder("20043", "RemoveNonExistentTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20043");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        var removed = plan.Repos.RemoveAll(r => r.Equals("/non/existent", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(0, removed);
    }

    // ==================== PlanAddPr ====================

    [Fact]
    public void PlanAddPr_AddsPrUrl()
    {
        CreatePlanFolder("20050", "AddPrTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20050");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Prs.Add("https://github.com/org/repo/pull/42");
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20050");
        Assert.Single(result.Prs);
        Assert.Equal("https://github.com/org/repo/pull/42", result.Prs[0]);
    }

    [Fact]
    public void PlanAddPr_Idempotent()
    {
        CreatePlanFolder("20051", "IdempotentPrTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20051");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Prs.Add("https://github.com/org/repo/pull/1");
        PlanCommandHelpers.WritePlan(folder, plan);

        plan = PlanCommandHelpers.ReadPlan(folder);
        if (!plan.Prs.Contains("https://github.com/org/repo/pull/1"))
            plan.Prs.Add("https://github.com/org/repo/pull/1");
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20051");
        Assert.Single(result.Prs);
    }

    [Fact]
    public void PlanAddPr_InvalidUrl_ThrowsOnValidation()
    {
        CreatePlanFolder("20052", "InvalidPrTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20052");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Prs.Add("not-a-url");

        Assert.Throws<ArgumentException>(() =>
            PlanCommandHelpers.WritePlan(folder, plan));
    }

    [Fact]
    public void PlanAddPr_MultiplePrs()
    {
        CreatePlanFolder("20053", "MultiplePrsTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20053");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Prs.Add("https://github.com/org/repo/pull/1");
        plan.Prs.Add("https://github.com/org/repo/pull/2");
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20053");
        Assert.Equal(2, result.Prs.Count);
    }

    // ==================== PlanAddCommit ====================

    [Fact]
    public void PlanAddCommit_AddsValidSha()
    {
        CreatePlanFolder("20060", "AddCommitTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20060");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Commits.Add("abc1234");
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20060");
        Assert.Single(result.Commits);
        Assert.Equal("abc1234", result.Commits[0]);
    }

    [Fact]
    public void PlanAddCommit_FullSha()
    {
        CreatePlanFolder("20061", "FullShaTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20061");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Commits.Add("abc1234567890abcdef1234567890abcdef1234");
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20061");
        Assert.Single(result.Commits);
    }

    [Fact]
    public void PlanAddCommit_InvalidSha_TooShort_Throws()
    {
        CreatePlanFolder("20062", "ShortShaTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20062");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Commits.Add("abc");

        Assert.Throws<ArgumentException>(() =>
            PlanCommandHelpers.WritePlan(folder, plan));
    }

    [Fact]
    public void PlanAddCommit_InvalidSha_NonHex_Throws()
    {
        CreatePlanFolder("20063", "NonHexShaTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20063");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Commits.Add("xyz1234invalid!");

        Assert.Throws<ArgumentException>(() =>
            PlanCommandHelpers.WritePlan(folder, plan));
    }

    [Fact]
    public void PlanAddCommit_Idempotent()
    {
        CreatePlanFolder("20064", "IdempotentCommitTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20064");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Commits.Add("abc1234");
        PlanCommandHelpers.WritePlan(folder, plan);

        plan = PlanCommandHelpers.ReadPlan(folder);
        if (!plan.Commits.Contains("abc1234"))
            plan.Commits.Add("abc1234");
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20064");
        Assert.Single(result.Commits);
    }

    // ==================== PlanSetVerification ====================

    [Fact]
    public void PlanSetVerification_AddsNewVerification()
    {
        CreatePlanFolder("20070", "AddVerifTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20070");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Verifications.Add(new PlanVerificationEntry { Name = "DotnetBuild", Status = "Pass" });
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20070");
        Assert.Single(result.Verifications);
        Assert.Equal("DotnetBuild", result.Verifications[0].Name);
        Assert.Equal("Pass", result.Verifications[0].Status);
    }

    [Fact]
    public void PlanSetVerification_UpdatesExisting()
    {
        CreatePlanFolder("20071", "UpdateVerifTest", new PlanYaml
        {
            State = "Draft",
            Project = "TestProject",
            Title = "UpdateVerifTest",
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            Verifications = [new PlanVerificationEntry { Name = "DotnetBuild", Status = "Pending" }]
        });

        var folder = PlanCommandHelpers.ResolvePlanFolder("20071");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        var verif = plan.Verifications.First(v => v.Name.Equals("DotnetBuild", StringComparison.OrdinalIgnoreCase));
        verif.Status = "Pass";
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20071");
        Assert.Single(result.Verifications);
        Assert.Equal("Pass", result.Verifications[0].Status);
    }

    [Fact]
    public void PlanSetVerification_InvalidStatus_Throws()
    {
        CreatePlanFolder("20072", "InvalidVerifTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20072");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Verifications.Add(new PlanVerificationEntry { Name = "DotnetBuild", Status = "BogusStatus" });

        Assert.Throws<ArgumentException>(() =>
            PlanCommandHelpers.WritePlan(folder, plan));
    }

    [Fact]
    public void PlanSetVerification_AllValidStatuses()
    {
        CreatePlanFolder("20073", "AllStatusesTest");
        var validStatuses = new[] { "Pending", "Pass", "Fail", "Skipped" };

        var folder = PlanCommandHelpers.ResolvePlanFolder("20073");

        foreach (var status in validStatuses)
        {
            var plan = PlanCommandHelpers.ReadPlan(folder);
            plan.Verifications.Clear();
            plan.Verifications.Add(new PlanVerificationEntry { Name = "TestVerif", Status = status });
            PlanCommandHelpers.WritePlan(folder, plan);

            var result = ReadPlan("20073");
            Assert.Equal(status, result.Verifications[0].Status);
        }
    }

    // ==================== PlanValidate ====================

    [Fact]
    public void PlanValidate_ValidPlan_Passes()
    {
        CreatePlanFolder("20080", "ValidPlan");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20080");
        var plan = PlanCommandHelpers.ReadPlan(folder);

        var ex = Record.Exception(() => PlanValidationService.Validate(plan));
        Assert.Null(ex);
    }

    [Fact]
    public void PlanValidate_MissingState_Throws()
    {
        var plan = new PlanYaml { State = "", Project = "X", Title = "Y", Repos = [_tempDir] };

        Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));
    }

    [Fact]
    public void PlanValidate_MissingProject_Throws()
    {
        var plan = new PlanYaml { State = "Draft", Project = "", Title = "Y", Repos = [_tempDir] };

        Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));
    }

    [Fact]
    public void PlanValidate_MissingTitle_Throws()
    {
        var plan = new PlanYaml { State = "Draft", Project = "X", Title = "", Repos = [_tempDir] };

        Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));
    }

    [Fact]
    public void PlanValidate_EmptyRepos_Throws()
    {
        var plan = new PlanYaml { State = "Draft", Project = "X", Title = "Y", Repos = [] };

        Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));
    }

    [Fact]
    public void PlanValidate_CompletedWithPrs_NoReposOk()
    {
        var plan = new PlanYaml
        {
            State = "Completed",
            Project = "X",
            Title = "Y",
            Repos = [],
            Prs = ["https://github.com/org/repo/pull/1"]
        };

        var ex = Record.Exception(() => PlanValidationService.Validate(plan));
        Assert.Null(ex);
    }

    [Fact]
    public void PlanValidate_NonExistentRepoPath_Throws()
    {
        var plan = new PlanYaml { State = "Draft", Project = "X", Title = "Y", Repos = ["/nonexistent/path"] };

        Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));
    }

    // ==================== PlanUpdate (full YAML from stdin) ====================

    [Fact]
    public void PlanUpdate_RoundtripFromYaml()
    {
        CreatePlanFolder("20090", "RoundtripTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20090");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.State = "Completed";
        plan.Prs = ["https://github.com/org/repo/pull/99"];
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var result = ReadPlan("20090");
        Assert.Equal("Completed", result.State);
        Assert.Single(result.Prs);
    }

    [Fact]
    public void PlanUpdate_WritePlan_AtomicNoCorruption()
    {
        CreatePlanFolder("20091", "AtomicTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("20091");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.State = "BogusState";

        Assert.Throws<ArgumentException>(() =>
            PlanCommandHelpers.WritePlan(folder, plan));

        var result = ReadPlan("20091");
        Assert.Equal("Draft", result.State);
    }

    // ==================== Timestamp Updates ====================

    [Fact]
    public void PlanSet_UpdatesTimestamp()
    {
        CreatePlanFolder("20100", "TimestampTest");
        var before = ReadPlan("20100").Updated;

        Thread.Sleep(50);

        var folder = PlanCommandHelpers.ResolvePlanFolder("20100");
        var plan = PlanCommandHelpers.ReadPlan(folder);
        plan.Project = "Changed";
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(folder, plan);

        var after = ReadPlan("20100").Updated;
        Assert.True(after > before);
    }

    // ==================== PlanList ====================

    [Fact]
    public void PlanList_ReturnsAllPlans()
    {
        CreatePlanFolder("20120", "ListAll1");
        CreatePlanFolder("20121", "ListAll2");

        var results = PlanListCommand.ScanPlans(_plansDir, new PlanListSettings());

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void PlanList_FilterByState()
    {
        CreatePlanFolder("20130", "DraftPlan");
        CreatePlanFolder("20131", "FailedPlan", new PlanYaml
        {
            State = "Failed",
            Project = "Test",
            Title = "FailedPlan",
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var results = PlanListCommand.ScanPlans(_plansDir, new PlanListSettings { State = "Failed" });

        Assert.Single(results);
        Assert.Equal("20131", results[0].Id);
    }

    [Fact]
    public void PlanList_FilterByProject()
    {
        CreatePlanFolder("20140", "ProjA", new PlanYaml
        {
            State = "Draft",
            Project = "Alpha",
            Title = "ProjA",
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        CreatePlanFolder("20141", "ProjB", new PlanYaml
        {
            State = "Draft",
            Project = "Beta",
            Title = "ProjB",
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var results = PlanListCommand.ScanPlans(_plansDir, new PlanListSettings { Project = "Alpha" });

        Assert.Single(results);
        Assert.Equal("20140", results[0].Id);
    }

    [Fact]
    public void PlanList_FilterByLevel()
    {
        CreatePlanFolder("20150", "CritPlan", new PlanYaml
        {
            State = "Draft",
            Project = "Test",
            Title = "CritPlan",
            Level = "Critical",
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        CreatePlanFolder("20151", "NicePlan");

        var results = PlanListCommand.ScanPlans(_plansDir, new PlanListSettings { Level = "Critical" });

        Assert.Single(results);
        Assert.Equal("20150", results[0].Id);
    }

    [Fact]
    public void PlanList_FilterHasPr()
    {
        CreatePlanFolder("20160", "WithPr", new PlanYaml
        {
            State = "Completed",
            Project = "Test",
            Title = "WithPr",
            Repos = [_tempDir],
            Prs = ["https://github.com/org/repo/pull/1"],
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        CreatePlanFolder("20161", "NoPr");

        var results = PlanListCommand.ScanPlans(_plansDir, new PlanListSettings { HasPr = true });

        Assert.Single(results);
        Assert.Equal("20160", results[0].Id);
    }

    [Fact]
    public void PlanList_FilterHasWorktree()
    {
        CreatePlanFolder("20170", "WithWt");
        var wtDir = Path.Combine(_plansDir, "20170-WithWt", "worktrees", "SomeRepo");
        Directory.CreateDirectory(wtDir);

        CreatePlanFolder("20171", "NoWt");

        var results = PlanListCommand.ScanPlans(_plansDir, new PlanListSettings { HasWorktree = true });

        Assert.Single(results);
        Assert.Equal("20170", results[0].Id);
    }

    [Fact]
    public void PlanList_CombinedFilters()
    {
        CreatePlanFolder("20180", "Match", new PlanYaml
        {
            State = "Draft",
            Project = "Tendril",
            Title = "Match",
            Level = "Bug",
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        CreatePlanFolder("20181", "NoMatch", new PlanYaml
        {
            State = "Draft",
            Project = "Other",
            Title = "NoMatch",
            Level = "Bug",
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var results = PlanListCommand.ScanPlans(_plansDir,
            new PlanListSettings { State = "Draft", Project = "Tendril" });

        Assert.Single(results);
        Assert.Equal("20180", results[0].Id);
    }

    [Fact]
    public void PlanList_SkipsFoldersWithoutYaml()
    {
        Directory.CreateDirectory(Path.Combine(_plansDir, "20190-NoYaml"));
        CreatePlanFolder("20191", "HasYaml");

        var results = PlanListCommand.ScanPlans(_plansDir, new PlanListSettings());

        Assert.Single(results);
        Assert.Equal("20191", results[0].Id);
    }

    [Fact]
    public void PlanList_SkipsNonPlanFolders()
    {
        Directory.CreateDirectory(Path.Combine(_plansDir, "not-a-plan"));
        Directory.CreateDirectory(Path.Combine(_plansDir, "random"));
        CreatePlanFolder("20192", "RealPlan");

        var results = PlanListCommand.ScanPlans(_plansDir, new PlanListSettings());

        Assert.Single(results);
    }

    // ==================== Edge Cases ====================

    [Fact]
    public void PlanWithAllFields_SurvivesRoundtrip()
    {
        var plan = new PlanYaml
        {
            State = "ReadyForReview",
            Project = "MyProject",
            Level = "Bug",
            Title = "Full Plan",
            Repos = [_tempDir],
            Created = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 3, 2, 14, 30, 0, DateTimeKind.Utc),
            Prs = ["https://github.com/org/repo/pull/1"],
            Commits = ["abc1234"],
            Verifications = [new PlanVerificationEntry { Name = "Build", Status = "Pass" }],
            RelatedPlans = ["20199-OtherPlan"],
            DependsOn = ["20198-BasePlan"],
            Priority = 3,
            ExecutionProfile = "fast",
            InitialPrompt = "Fix the bug",
            SourceUrl = "https://example.com/issue/1",
            Recommendations =
            [
                new RecommendationYaml
                {
                    Title = "Add tests",
                    Description = "Coverage is low",
                    State = "Pending",
                    Impact = "Medium",
                    Risk = "Small"
                }
            ]
        };

        CreatePlanFolder("20110", "FullRoundtrip", plan);

        var result = ReadPlan("20110");
        Assert.Equal("ReadyForReview", result.State);
        Assert.Equal("MyProject", result.Project);
        Assert.Equal("Bug", result.Level);
        Assert.Equal("Full Plan", result.Title);
        Assert.Single(result.Repos);
        Assert.Single(result.Prs);
        Assert.Single(result.Commits);
        Assert.Single(result.Verifications);
        Assert.Single(result.RelatedPlans!);
        Assert.Single(result.DependsOn!);
        Assert.Equal(3, result.Priority);
        Assert.Equal("fast", result.ExecutionProfile);
        Assert.Equal("Fix the bug", result.InitialPrompt);
        Assert.Equal("https://example.com/issue/1", result.SourceUrl);
        Assert.Single(result.Recommendations!);
        Assert.Equal("Add tests", result.Recommendations![0].Title);
    }

    [Fact]
    public void PlanWithSpecialCharacters_InTitle_Roundtrips()
    {
        CreatePlanFolder("20111", "SpecialChars", new PlanYaml
        {
            State = "Draft",
            Project = "Test",
            Title = "Fix: \"quotes\" & <angle> brackets",
            Repos = [_tempDir],
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var result = ReadPlan("20111");
        Assert.Equal("Fix: \"quotes\" & <angle> brackets", result.Title);
    }

    // --- PlanAddLog Tests ---

    [Fact]
    public void PlanAddLog_CreatesFirstLog()
    {
        CreatePlanFolder("30001", "TestLog");
        var planDir = Path.Combine(_plansDir, "30001-TestLog");

        _ = PlanAddLogCommand.WriteLog(planDir, "CreatePlan");

        var logsDir = Path.Combine(planDir, "logs");
        Assert.True(Directory.Exists(logsDir));
        var logFiles = Directory.GetFiles(logsDir, "*.md");
        Assert.Single(logFiles);
        Assert.Contains("001-CreatePlan.md", logFiles[0]);

        var content = File.ReadAllText(logFiles[0]);
        Assert.Contains("# CreatePlan", content);
        Assert.Contains("Completed", content);
    }

    [Fact]
    public void PlanAddLog_IncrementsLogNumber()
    {
        CreatePlanFolder("30002", "TestLogIncr");
        var planDir = Path.Combine(_plansDir, "30002-TestLogIncr");
        var logsDir = Path.Combine(planDir, "logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "001-ExpandPlan.md"), "first");
        File.WriteAllText(Path.Combine(logsDir, "002-ExecutePlan.md"), "second");

        PlanAddLogCommand.WriteLog(planDir, "CreatePr");

        Assert.True(File.Exists(Path.Combine(logsDir, "003-CreatePr.md")));
    }

    [Fact]
    public void PlanAddLog_IncludesSummary()
    {
        CreatePlanFolder("30003", "TestLogSummary");
        var planDir = Path.Combine(_plansDir, "30003-TestLogSummary");

        PlanAddLogCommand.WriteLog(planDir, "ExecutePlan", "Completed all verifications successfully");

        var logsDir = Path.Combine(planDir, "logs");
        var logFile = Directory.GetFiles(logsDir, "*.md").Single();
        var content = File.ReadAllText(logFile);
        Assert.Contains("Completed all verifications successfully", content);
    }

    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }
}