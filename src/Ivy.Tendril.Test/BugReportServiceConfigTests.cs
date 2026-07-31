using System.Text;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class BugReportServiceConfigTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("ivy-bugreport-test");

    public void Dispose() => _tempDir.Dispose();

    private BugReportService CreateService(string yaml)
    {
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), yaml);
        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        return new BugReportService(config, new TendrilArgs());
    }

    private static string ReadContent(BugReportService.BugReportFile file) =>
        Encoding.UTF8.GetString(file.Content!);

    [Fact]
    public void CollectSanitizedConfig_Redacts_Secrets()
    {
        var yaml = @"
codingAgent: claude
auth:
  username: admin
  password: super-secret-password
  hashSecret: argon2-hash-value
llm:
  endpoint: https://example.com
  apiKey: sk-llm-secret
api:
  apiKey: sk-api-secret
codingAgents:
  - name: claude
    environmentVariables:
      ANTHROPIC_API_KEY: sk-ant-secret
      SOME_FLAG: keep-shape-only
";
        var service = CreateService(yaml);

        var file = service.CollectSanitizedConfig();

        Assert.NotNull(file);
        var content = ReadContent(file!);

        Assert.DoesNotContain("super-secret-password", content);
        Assert.DoesNotContain("argon2-hash-value", content);
        Assert.DoesNotContain("sk-llm-secret", content);
        Assert.DoesNotContain("sk-api-secret", content);
        Assert.DoesNotContain("sk-ant-secret", content);
        Assert.DoesNotContain("keep-shape-only", content);

        // Non-secret structure is preserved so the report stays useful.
        Assert.Contains("admin", content);
        Assert.Contains("ANTHROPIC_API_KEY", content);
        Assert.Contains("SOME_FLAG", content);
        Assert.Contains("[REDACTED]", content);
    }

    [Fact]
    public void CollectSanitizedConfig_Keeps_VariableReferences_Literal()
    {
        var yaml = @"
codingAgent: claude
projects:
  - name: Demo
    repos:
      - path: '%USERPROFILE%\repos\demo'
";
        var service = CreateService(yaml);

        var content = ReadContent(service.CollectSanitizedConfig()!);

        // Reads from disk, not expanded Settings, so the variable token survives.
        Assert.Contains("%USERPROFILE%", content);
    }

    [Fact]
    public void CollectSanitizedConfig_Returns_Null_When_No_Config()
    {
        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var service = new BugReportService(config, new TendrilArgs());

        Assert.Null(service.CollectSanitizedConfig());
    }

    [Fact]
    public void CollectFilesForJob_Includes_Sanitized_Config()
    {
        var service = CreateService("codingAgent: claude\nauth:\n  password: secret\n");

        var files = service.CollectFilesForJob("123");

        var config = Assert.Single(files, f => f.ZipEntryPath == "config.sanitized.yaml");
        Assert.DoesNotContain("secret", ReadContent(config));
    }

    /// <summary>Writes the four artifacts job 00123 produces for plan 00044.</summary>
    private void SeedJobArtifacts()
    {
        var jobsDir = Path.Combine(_tempDir.Path, "Jobs");
        Directory.CreateDirectory(jobsDir);
        const string stem = "00123-00044-ExecutePlan";
        File.WriteAllText(Path.Combine(jobsDir, $"{stem}.md"), "# execute log");
        File.WriteAllText(Path.Combine(jobsDir, $"{stem}.prompt.md"), "the prompt");
        File.WriteAllText(Path.Combine(jobsDir, $"{stem}.raw.jsonl"), "{}");
        File.WriteAllText(Path.Combine(jobsDir, $"{stem}.eventwire.jsonl"), "{}");
    }

    /// <summary>
    /// A CreatePlan job (id 00500) that produced plan 00044. Its stem carries no plan id — the link
    /// exists only as the PlanId line JobLogWriter puts in the log header.
    /// </summary>
    private void SeedCreatePlanJobArtifacts(string planId = "00044")
    {
        var jobsDir = Path.Combine(_tempDir.Path, "Jobs");
        Directory.CreateDirectory(jobsDir);
        const string stem = "00500-CreatePlan";
        File.WriteAllText(Path.Combine(jobsDir, $"{stem}.md"),
            $"# Job Log {stem}\n\n- **JobId:** 00500\n- **PlanId:** {planId}\n- **Status:** Completed\n");
        File.WriteAllText(Path.Combine(jobsDir, $"{stem}.prompt.md"), "the prompt");
        File.WriteAllText(Path.Combine(jobsDir, $"{stem}.raw.jsonl"), "{}");
        File.WriteAllText(Path.Combine(jobsDir, $"{stem}.eventwire.jsonl"), "{}");
    }

    private string SeedPlanFolder(string folderName = "00044-Demo")
    {
        var planFolder = Path.Combine(_tempDir.Path, "Plans", folderName);
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), "id: 44\nproject: Demo\n");
        return planFolder;
    }

    [Fact]
    public void CollectFilesForJob_CreatePlanJob_ResolvesPlanContextFromItsLog()
    {
        // The CreatePlan job's filename has no plan id, so the plan folder must come from its log header.
        var service = CreateService("codingAgent: claude\n");
        SeedCreatePlanJobArtifacts();
        SeedPlanFolder();

        var files = service.CollectFilesForJob("500");
        static string Entry(BugReportService.BugReportFile f) => f.ZipEntryPath.Replace('\\', '/');

        Assert.Contains(files, f => Entry(f) == "Jobs/00500-CreatePlan.md");
        Assert.Contains(files, f => Entry(f) == "Jobs/00500-CreatePlan.eventwire.jsonl");
        Assert.Contains(files, f => Entry(f) == "plan.yaml");
    }

    [Fact]
    public void CollectFilesForPlan_IncludesTheCreatePlanJobThatAuthoredThePlan()
    {
        // Globbing "*-00044-*" never matches "00500-CreatePlan"; it is recovered via its PlanId line.
        var service = CreateService("codingAgent: claude\n");
        SeedJobArtifacts();
        SeedCreatePlanJobArtifacts();
        var planFolder = SeedPlanFolder();

        var files = service.CollectFilesForPlan(planFolder);
        static string Entry(BugReportService.BugReportFile f) => f.ZipEntryPath.Replace('\\', '/');

        Assert.Contains(files, f => Entry(f) == "Jobs/00123-00044-ExecutePlan.md");
        Assert.Contains(files, f => Entry(f) == "Jobs/00500-CreatePlan.md");
        Assert.Contains(files, f => Entry(f) == "Jobs/00500-CreatePlan.raw.jsonl");
    }

    [Fact]
    public void CollectFilesForPlan_ExcludesAPlanlessJobBelongingToAnotherPlan()
    {
        var service = CreateService("codingAgent: claude\n");
        SeedCreatePlanJobArtifacts(planId: "00099");
        var planFolder = SeedPlanFolder();

        var files = service.CollectFilesForPlan(planFolder);
        static string Entry(BugReportService.BugReportFile f) => f.ZipEntryPath.Replace('\\', '/');

        Assert.DoesNotContain(files, f => Entry(f) == "Jobs/00500-CreatePlan.md");
    }

    [Fact]
    public void CollectFilesForJob_Includes_All_Four_Job_Artifacts()
    {
        var service = CreateService("codingAgent: claude\n");
        SeedJobArtifacts();

        var files = service.CollectFilesForJob("123");
        static string Entry(BugReportService.BugReportFile f) => f.ZipEntryPath.Replace('\\', '/');

        Assert.Contains(files, f => Entry(f) == "Jobs/00123-00044-ExecutePlan.md");
        Assert.Contains(files, f => Entry(f) == "Jobs/00123-00044-ExecutePlan.prompt.md");
        Assert.Contains(files, f => Entry(f) == "Jobs/00123-00044-ExecutePlan.raw.jsonl");
        Assert.Contains(files, f => Entry(f) == "Jobs/00123-00044-ExecutePlan.eventwire.jsonl");
    }

    [Fact]
    public void CollectFilesForJob_Includes_Plan_Context_And_Worktrees_Manifest()
    {
        var service = CreateService("codingAgent: claude\n");

        // The plan that owns the job is read off the job artifact's middle stem segment.
        SeedJobArtifacts();
        var planFolder = Path.Combine(_tempDir.Path, "Plans", "00044-Demo");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), "id: 44\nproject: Demo\n");
        Directory.CreateDirectory(Path.Combine(planFolder, "Worktrees", "Ivy-Framework"));

        var files = service.CollectFilesForJob("123");

        static string Entry(BugReportService.BugReportFile f) => f.ZipEntryPath.Replace('\\', '/');

        Assert.Contains(files, f => Entry(f) == "plan.yaml");

        // worktrees.txt lists each worktree dir's identity (git fields are "(unknown)" for a non-repo dir).
        var manifest = Assert.Single(files, f => f.ZipEntryPath == "worktrees.txt");
        Assert.Contains("Ivy-Framework", ReadContent(manifest));

        // The worktree trees themselves stay excluded for size.
        Assert.DoesNotContain(files, f => Entry(f).StartsWith("Worktrees/"));
    }

    [Fact]
    public void CollectFilesForJob_Without_Plan_Omits_Plan_Context()
    {
        var service = CreateService("codingAgent: claude\n");

        var files = service.CollectFilesForJob("999");

        Assert.DoesNotContain(files, f => f.ZipEntryPath == "plan.yaml");
        Assert.DoesNotContain(files, f => f.ZipEntryPath == "worktrees.txt");
    }

    [Fact]
    public void CollectFilesForJob_PlanlessJob_CollectsArtifactsButNoPlanContext()
    {
        var service = CreateService("codingAgent: claude\n");
        var jobsDir = Path.Combine(_tempDir.Path, "Jobs");
        Directory.CreateDirectory(jobsDir);
        File.WriteAllText(Path.Combine(jobsDir, "00500-CreatePlan.md"), "# create log");

        var files = service.CollectFilesForJob("500");
        static string Entry(BugReportService.BugReportFile f) => f.ZipEntryPath.Replace('\\', '/');

        Assert.Contains(files, f => Entry(f) == "Jobs/00500-CreatePlan.md");
        Assert.DoesNotContain(files, f => f.ZipEntryPath == "plan.yaml");
    }

    [Fact]
    public void CollectFilesForPlan_FindsJobsByPlanIdInTheStem()
    {
        var service = CreateService("codingAgent: claude\n");
        SeedJobArtifacts();
        var planFolder = Path.Combine(_tempDir.Path, "Plans", "00044-Demo");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), "id: 44\n");

        var files = service.CollectFilesForPlan(planFolder);
        static string Entry(BugReportService.BugReportFile f) => f.ZipEntryPath.Replace('\\', '/');

        Assert.Contains(files, f => Entry(f) == "plan.yaml");
        Assert.Contains(files, f => Entry(f) == "Jobs/00123-00044-ExecutePlan.md");
        Assert.Contains(files, f => Entry(f) == "Jobs/00123-00044-ExecutePlan.eventwire.jsonl");
    }
}
