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
        return new BugReportService(config);
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
        var service = new BugReportService(config);

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

    [Fact]
    public void CollectFilesForJob_Includes_Plan_Context_And_Worktrees_Manifest()
    {
        var service = CreateService("codingAgent: claude\n");

        // A plan folder owns the job when its Logs/ holds a "<jobId>-<action>.md" log.
        var planFolder = Path.Combine(_tempDir.Path, "Plans", "00123-Demo");
        var logsDir = Path.Combine(planFolder, "Logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), "id: 123\nproject: Demo\n");
        File.WriteAllText(Path.Combine(logsDir, "00123-ExecutePlan.md"), "# execute log");
        Directory.CreateDirectory(Path.Combine(planFolder, "Worktrees", "Ivy-Framework"));

        var files = service.CollectFilesForJob("123");

        static string Entry(BugReportService.BugReportFile f) => f.ZipEntryPath.Replace('\\', '/');

        Assert.Contains(files, f => Entry(f) == "plan.yaml");
        Assert.Contains(files, f => Entry(f) == "Logs/00123-ExecutePlan.md");

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
}
