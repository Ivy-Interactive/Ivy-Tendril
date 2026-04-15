using Ivy.Tendril.Commands;

namespace Ivy.Tendril.Test;

public class DoctorCommandPlansTests : IDisposable
{
    private readonly string _plansDir;

    public DoctorCommandPlansTests()
    {
        _plansDir = Path.Combine(Path.GetTempPath(), $"tendril-doctor-plans-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_plansDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_plansDir))
            try { Directory.Delete(_plansDir, true); }
            catch { /* best effort */ }
    }

    private string CreatePlan(string folderName, string? yamlContent = null)
    {
        var planDir = Path.Combine(_plansDir, folderName);
        Directory.CreateDirectory(planDir);
        if (yamlContent != null)
            File.WriteAllText(Path.Combine(planDir, "plan.yaml"), yamlContent);
        return planDir;
    }

    private static readonly string ValidYaml = """
        state: Completed
        project: TestProject
        title: Test Plan
        repos:
        - /dummy/repo
        commits: []
        """;

    [Fact]
    public void DoctorPlans_HealthyPlan_ReturnsOK()
    {
        CreatePlan("00001-HealthyPlan", ValidYaml);

        var results = DoctorCommand.ScanPlans(_plansDir);

        Assert.Single(results);
        Assert.Equal("00001", results[0].Id);
        Assert.Equal("HealthyPlan", results[0].Title);
        Assert.Equal("Completed", results[0].State);
        Assert.Equal(0, results[0].Worktrees);
        Assert.Equal("OK", results[0].Health);
        Assert.True(results[0].IsHealthy);
    }

    [Fact]
    public void DoctorPlans_MissingYaml_ReportsError()
    {
        CreatePlan("00002-MissingYaml");

        var results = DoctorCommand.ScanPlans(_plansDir);

        Assert.Single(results);
        Assert.False(results[0].IsHealthy);
        Assert.Contains("YAML:Missing", results[0].Health);
        Assert.Equal("Unknown", results[0].State);
    }

    [Fact]
    public void DoctorPlans_InvalidYaml_ReportsError()
    {
        CreatePlan("00003-InvalidYaml", "title: OnlyTitle\nrepos: []\n");

        var results = DoctorCommand.ScanPlans(_plansDir);

        Assert.Single(results);
        Assert.False(results[0].IsHealthy);
        Assert.Contains("YAML:No repos", results[0].Health);
    }

    [Fact]
    public void DoctorPlans_WithWorktrees_CountsCorrectly()
    {
        var planDir = CreatePlan("00004-WithWorktrees", ValidYaml);
        var wtDir = Path.Combine(planDir, "worktrees");
        Directory.CreateDirectory(Path.Combine(wtDir, "RepoA"));
        Directory.CreateDirectory(Path.Combine(wtDir, "RepoB"));

        var results = DoctorCommand.ScanPlans(_plansDir);

        Assert.Single(results);
        Assert.Equal(2, results[0].Worktrees);
        Assert.False(results[0].IsHealthy);
        Assert.Contains("StaleWorktree", results[0].Health);
    }

    [Fact]
    public void DoctorPlans_NestedWorktree_DetectsIssue()
    {
        var planDir = CreatePlan("00005-NestedWt", ValidYaml);
        var wtRepoDir = Path.Combine(planDir, "worktrees", "SomeRepo");
        Directory.CreateDirectory(wtRepoDir);
        File.WriteAllText(Path.Combine(wtRepoDir, ".git"), "gitdir: /some/path");
        var nestedDir = Path.Combine(wtRepoDir, "subdir");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, ".git"), "gitdir: /nested/path");

        var results = DoctorCommand.ScanPlans(_plansDir);

        Assert.Single(results);
        Assert.False(results[0].IsHealthy);
        Assert.Contains("NestedWorktree", results[0].Health);
    }

    [Fact]
    public void DoctorPlans_UnhealthyFlag_FiltersResults()
    {
        CreatePlan("00010-Healthy", ValidYaml);
        CreatePlan("00011-Broken");

        var allResults = DoctorCommand.ScanPlans(_plansDir);
        var filtered = allResults.Where(r => !r.IsHealthy).ToList();

        Assert.Equal(2, allResults.Count);
        Assert.Single(filtered);
        Assert.Equal("00011", filtered[0].Id);
    }

    [Fact]
    public void HasNestedWorktrees_WithNestedGit_ReturnsTrue()
    {
        var planDir = CreatePlan("00020-WithNested", ValidYaml);
        var wtRepoDir = Path.Combine(planDir, "worktrees", "SomeRepo");
        Directory.CreateDirectory(wtRepoDir);
        File.WriteAllText(Path.Combine(wtRepoDir, ".git"), "gitdir: /some/path");
        var nestedDir = Path.Combine(wtRepoDir, "subdir");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, ".git"), "gitdir: /nested/path");

        var result = DoctorCommand.HasNestedWorktrees(planDir);

        Assert.True(result);
    }

    [Fact]
    public void HasNestedWorktrees_NoWorktrees_ReturnsFalse()
    {
        var planDir = CreatePlan("00021-NoWt", ValidYaml);

        var result = DoctorCommand.HasNestedWorktrees(planDir);

        Assert.False(result);
    }

    [Fact]
    public void HasStaleWorktrees_WithStaleDir_ReturnsTrue()
    {
        var planDir = CreatePlan("00022-Stale", ValidYaml);
        var wtDir = Path.Combine(planDir, "worktrees", "StaleRepo");
        Directory.CreateDirectory(wtDir);

        var result = DoctorCommand.HasStaleWorktrees(planDir);

        Assert.True(result);
    }

    [Fact]
    public void HasStaleWorktrees_WithValidGit_ReturnsFalse()
    {
        var planDir = CreatePlan("00023-Valid", ValidYaml);
        var wtDir = Path.Combine(planDir, "worktrees", "ValidRepo");
        Directory.CreateDirectory(wtDir);
        File.WriteAllText(Path.Combine(wtDir, ".git"), "gitdir: /some/path");

        var result = DoctorCommand.HasStaleWorktrees(planDir);

        Assert.False(result);
    }

    [Fact]
    public void RepairPlan_StaleWorktree_RemovesDirectory()
    {
        var planDir = CreatePlan("00024-RepairStale", ValidYaml);
        var wtDir = Path.Combine(planDir, "worktrees", "StaleRepo");
        Directory.CreateDirectory(wtDir);
        File.WriteAllText(Path.Combine(wtDir, "dummy.txt"), "test");

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00024", "RepairStale", "Draft", 1, "StaleWorktree", false);

        var result = DoctorCommand.RepairPlan(planDir, healthResult);

        Assert.True(result.Success);
        Assert.Contains("removed stale worktrees", result.Message);
        Assert.False(Directory.Exists(wtDir));
    }

    [Fact]
    public void RepairPlan_NestedWorktree_RemovesNested()
    {
        var planDir = CreatePlan("00025-RepairNested", ValidYaml);
        var wtDir = Path.Combine(planDir, "worktrees", "SomeRepo");
        Directory.CreateDirectory(wtDir);
        var nestedPlansDir = Path.Combine(wtDir, "Plans");
        Directory.CreateDirectory(nestedPlansDir);
        File.WriteAllText(Path.Combine(nestedPlansDir, "dummy.txt"), "test");

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00025", "RepairNested", "Draft", 1, "NestedWorktree", false);

        var result = DoctorCommand.RepairPlan(planDir, healthResult);

        Assert.True(result.Success);
        Assert.Contains("cleaned nested worktrees", result.Message);
        Assert.False(Directory.Exists(nestedPlansDir));
    }

    [Fact]
    public void CheckYamlHealth_EmptyFile_ReportsEmpty()
    {
        var path = Path.Combine(_plansDir, "empty.yaml");
        File.WriteAllText(path, "");

        var (healthy, error, state) = DoctorCommand.CheckYamlHealth(path);

        Assert.False(healthy);
        Assert.Equal("Empty", error);
        Assert.Equal("Unknown", state);
    }

    [Fact]
    public void CheckYamlHealth_ValidFile_ExtractsState()
    {
        var path = Path.Combine(_plansDir, "valid.yaml");
        File.WriteAllText(path, ValidYaml);

        var (healthy, error, state) = DoctorCommand.CheckYamlHealth(path);

        Assert.True(healthy);
        Assert.Null(error);
        Assert.Equal("Completed", state);
    }

    [Fact]
    public void TitleFromFolderName_PascalCase_InsertsSpaces()
    {
        var title = DoctorCommand.TitleFromFolderName("03236-AgentFailsToRecover");
        Assert.Equal("Agent Fails To Recover", title);
    }

    [Fact]
    public void TitleFromFolderName_HyphenSeparated_ReplacesHyphens()
    {
        var title = DoctorCommand.TitleFromFolderName("00001-my-great-plan");
        Assert.Equal("my great plan", title);
    }

    [Fact]
    public void TitleFromFolderName_Mixed_HandlesCorrectly()
    {
        var title = DoctorCommand.TitleFromFolderName("03284-ConsolidateSubtaskWorktreePlans");
        Assert.Equal("Consolidate Subtask Worktree Plans", title);
    }

    [Fact]
    public void RepairPlan_MissingYaml_CreatesScaffold()
    {
        var planDir = CreatePlan("00030-NewScaffold");

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00030", "NewScaffold", "Unknown", 0, "YAML:Missing", false);

        var result = DoctorCommand.RepairPlan(planDir, healthResult);

        Assert.True(result.Success);
        Assert.Contains("created missing plan.yaml", result.Message);
        var yamlPath = Path.Combine(planDir, "plan.yaml");
        Assert.True(File.Exists(yamlPath));
        var content = File.ReadAllText(yamlPath);
        Assert.Contains("title: New Scaffold", content);
        Assert.Contains("state: Draft", content);
        Assert.Contains("project: Auto", content);
    }

    [Fact]
    public void RepairPlan_MissingTitle_FillsFromFolder()
    {
        var planDir = CreatePlan("00031-FixAuthBug", """
            state: Completed
            project: Auto
            title: ""
            repos:
            - /dummy/repo
            """);

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00031", "FixAuthBug", "Completed", 0, "YAML:Missing title", false);

        var result = DoctorCommand.RepairPlan(planDir, healthResult);

        Assert.True(result.Success);
        var content = File.ReadAllText(Path.Combine(planDir, "plan.yaml"));
        Assert.Contains("title: Fix Auth Bug", content);
    }

    [Fact]
    public void RepairPlan_MissingProject_SetsDefault()
    {
        var planDir = CreatePlan("00032-SomePlan", """
            state: Draft
            project:
            title: Some Plan
            repos:
            - /dummy/repo
            """);

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00032", "SomePlan", "Draft", 0, "YAML:Missing project", false);

        var result = DoctorCommand.RepairPlan(planDir, healthResult);

        Assert.True(result.Success);
        var content = File.ReadAllText(Path.Combine(planDir, "plan.yaml"));
        Assert.Contains("project: Auto", content);
    }

    [Fact]
    public void RepairPlan_NullReposList_SetsEmptyList()
    {
        var planDir = CreatePlan("00033-NullRepos", """
            state: Draft
            project: Tendril
            title: Null Repos
            repos:
            commits: []
            """);

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00033", "NullRepos", "Draft", 0, "YAML:No repos", false);

        var result = DoctorCommand.RepairPlan(planDir, healthResult);

        Assert.True(result.Success);
        var content = File.ReadAllText(Path.Combine(planDir, "plan.yaml"));
        Assert.Contains("repos: []", content);
    }

    [Fact]
    public void RepairRecommendationsYaml_BacktickTitle_Quoted()
    {
        var input = "- title: `Backtick title` causes parse failure\n  state: Pending\n";
        var repaired = DoctorCommand.RepairRecommendationsYaml(input);
        Assert.DoesNotContain("- title: `", repaired);
        Assert.Contains("`Backtick title`", repaired);
    }

    [Fact]
    public void RepairPlan_BadRecs_RepairsFile()
    {
        var planDir = CreatePlan("00034-BadRecs", ValidYaml);
        var artifactsDir = Path.Combine(planDir, "artifacts");
        Directory.CreateDirectory(artifactsDir);
        File.WriteAllText(Path.Combine(artifactsDir, "recommendations.yaml"),
            "- title: `Backtick` breaks\n  state: Pending\n");

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00034", "BadRecs", "Completed", 0, "Recs:Parse error: some error", false);

        var result = DoctorCommand.RepairPlan(planDir, healthResult);

        Assert.True(result.Success);
        Assert.Contains("repaired recommendations.yaml", result.Message);
    }

    [Fact]
    public void GetPruneReason_NoPrsCommitsRevisions_ReturnReason()
    {
        var planDir = CreatePlan("00035-JunkPlan", """
            state: Draft
            project: Test
            title: Junk
            repos: []
            commits: []
            prs: []
            """);

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00035", "JunkPlan", "Draft", 0, "YAML:No repos", false);

        var reason = DoctorCommand.GetPruneReason(planDir, healthResult);

        Assert.NotNull(reason);
        Assert.Contains("no PRs/commits/revisions", reason);
    }

    [Fact]
    public void GetPruneReason_WithPrs_ReturnsNull()
    {
        var planDir = CreatePlan("00036-RealPlan", """
            state: Completed
            project: Auto
            title: Real Plan
            repos: []
            commits: []
            prs:
              - https://github.com/org/repo/pull/1
            """);

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00036", "RealPlan", "Completed", 0, "YAML:No repos", false);

        var reason = DoctorCommand.GetPruneReason(planDir, healthResult);

        Assert.Null(reason);
    }

    [Fact]
    public void GetPruneReason_WithRevisions_ReturnsNull()
    {
        var planDir = CreatePlan("00037-HasRevisions", """
            state: Draft
            project: Test
            title: Has Revisions
            repos: []
            commits: []
            prs: []
            """);
        var revisionsDir = Path.Combine(planDir, "revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "v1.md"), "# Plan v1");

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00037", "HasRevisions", "Draft", 0, "YAML:No repos", false);

        var reason = DoctorCommand.GetPruneReason(planDir, healthResult);

        Assert.Null(reason);
    }

    [Fact]
    public void GetPruneReason_MissingYaml_ReturnsReason()
    {
        CreatePlan("00038-NoYaml");

        var healthResult = new DoctorCommand.PlanHealthResult(
            "00038", "NoYaml", "Unknown", 0, "YAML:Missing", false);

        var reason = DoctorCommand.GetPruneReason(
            Path.Combine(_plansDir, "00038-NoYaml"), healthResult);

        Assert.NotNull(reason);
        Assert.Contains("no plan.yaml", reason);
    }

    [Fact]
    public void FindPruneCandidates_MixedPlans_OnlyReturnsUnhealthyWithoutContent()
    {
        CreatePlan("00040-Healthy", ValidYaml);
        CreatePlan("00041-Junk", """
            state: Draft
            project: Test
            title: Junk
            repos: []
            commits: []
            prs: []
            """);
        CreatePlan("00042-NoYaml");

        var allResults = DoctorCommand.ScanPlans(_plansDir);
        var candidates = DoctorCommand.FindPruneCandidates(_plansDir, allResults);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Result.Id == "00041");
        Assert.Contains(candidates, c => c.Result.Id == "00042");
        Assert.DoesNotContain(candidates, c => c.Result.Id == "00040");
    }

    [Fact]
    public void RepairYamlFields_AddsFieldsWhenMissing()
    {
        var content = "repos:\n- /some/repo\n";
        var repaired = DoctorCommand.RepairYamlFields(content, "My Plan");

        Assert.Contains("state: Draft", repaired);
        Assert.Contains("project: Auto", repaired);
        Assert.Contains("title: My Plan", repaired);
    }
}
