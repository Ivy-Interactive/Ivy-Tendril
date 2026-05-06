using Ivy.Tendril.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Commands;

/// <summary>
/// Dry-run tests for all promptwares. Verifies that each promptware's Program.md
/// compiles correctly and produces expected firmware header values in the output.
/// Uses the real Program.md files from the source tree.
/// </summary>
[Collection("TendrilHome")]
public class PromptwareDryRunTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly string _promptwarePath;
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;

    private static readonly string SourcePromptwarePath = Path.GetFullPath(
        Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "Ivy.Tendril", "Promptwares"));

    public PromptwareDryRunTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"promptware-dryrun-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "Plans"));

        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalTendrilPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);

        _configPath = Path.Combine(_tempDir, "config.yaml");
        _promptwarePath = Directory.Exists(SourcePromptwarePath)
            ? SourcePromptwarePath
            : CreateFallbackPromptwareDir();

        WriteConfig();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _originalTendrilPlans);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void WriteConfig()
    {
        var plansDir = Path.Combine(_tempDir, "Plans");
        File.WriteAllText(_configPath, $"""
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
              - name: balanced
                model: sonnet
                effort: high
              - name: quick
                model: haiku
                effort: low
            promptwares:
              CreatePlan:
                profile: balanced
              ExecutePlan:
                profile: deep
              ExpandPlan:
                profile: balanced
              UpdatePlan:
                profile: balanced
              SplitPlan:
                profile: balanced
              CreatePr:
                profile: balanced
              CreateIssue:
                profile: balanced
              UpdateProject:
                profile: deep
            projects:
            - name: TestProject
              repos:
              - path: {plansDir}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation.
            """);
    }

    private string CreateFallbackPromptwareDir()
    {
        var root = Path.Combine(_tempDir, "Promptwares");
        foreach (var name in new[] { "CreatePlan", "ExecutePlan", "ExpandPlan", "UpdatePlan", "SplitPlan", "CreatePr", "CreateIssue", "UpdateProject" })
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Program.md"), $"# {name}\n\nPromptware stub for testing.\n");
        }
        return root;
    }

    private string RunDryRun(string promptware, string[]? values = null, string? plan = null)
    {
        var command = new PromptwareRunCommand(NullLogger<PromptwareRunCommand>.Instance);
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            command.Run(new PromptwareRunSettings
            {
                Promptware = promptware,
                DryRun = true,
                ConfigPath = _configPath,
                PromptwarePath = _promptwarePath,
                Values = values,
                Plan = plan
            });
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // ==================== CreatePlan ====================

    [Fact]
    public void CreatePlan_CompilesFirmware()
    {
        var output = RunDryRun("CreatePlan", ["PlansDirectory=/tmp/plans", "Project=TestProject"]);

        Assert.Contains("PlansDirectory: /tmp/plans", output);
        Assert.Contains("Project: TestProject", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void CreatePlan_ResolvesBalancedProfile()
    {
        var output = RunDryRun("CreatePlan", ["PlansDirectory=/tmp/plans"]);

        Assert.Contains("model: sonnet", output);
        Assert.Contains("effort: high", output);
    }

    // ==================== ExecutePlan ====================

    [Fact]
    public void ExecutePlan_CompilesFirmware()
    {
        var output = RunDryRun("ExecutePlan", ["PlanFolder=/tmp/plans/00001-Test"]);

        Assert.Contains("PlanFolder: /tmp/plans/00001-Test", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void ExecutePlan_ResolvesDeepProfile()
    {
        var output = RunDryRun("ExecutePlan", ["PlanFolder=/tmp/plans/00001-Test"]);

        Assert.Contains("model: opus", output);
        Assert.Contains("effort: max", output);
    }

    [Fact]
    public void ExecutePlan_IncludesOptionalNote()
    {
        var output = RunDryRun("ExecutePlan", ["PlanFolder=/tmp/plans/00001-Test", "Note=Fix the build error first"]);

        Assert.Contains("Note: Fix the build error first", output);
    }

    // ==================== ExpandPlan ====================

    [Fact]
    public void ExpandPlan_CompilesFirmware()
    {
        var output = RunDryRun("ExpandPlan", ["PlanFolder=/tmp/plans/00002-Expand"]);

        Assert.Contains("PlanFolder: /tmp/plans/00002-Expand", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void ExpandPlan_ResolvesBalancedProfile()
    {
        var output = RunDryRun("ExpandPlan", ["PlanFolder=/tmp/plans/00002-Expand"]);

        Assert.Contains("model: sonnet", output);
        Assert.Contains("effort: high", output);
    }

    // ==================== UpdatePlan ====================

    [Fact]
    public void UpdatePlan_CompilesFirmware()
    {
        var output = RunDryRun("UpdatePlan", ["PlanFolder=/tmp/plans/00003-Update"]);

        Assert.Contains("PlanFolder: /tmp/plans/00003-Update", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void UpdatePlan_ResolvesBalancedProfile()
    {
        var output = RunDryRun("UpdatePlan", ["PlanFolder=/tmp/plans/00003-Update"]);

        Assert.Contains("model: sonnet", output);
    }

    // ==================== SplitPlan ====================

    [Fact]
    public void SplitPlan_CompilesFirmware()
    {
        var output = RunDryRun("SplitPlan", ["PlanFolder=/tmp/plans/00004-Split"]);

        Assert.Contains("PlanFolder: /tmp/plans/00004-Split", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void SplitPlan_ResolvesBalancedProfile()
    {
        var output = RunDryRun("SplitPlan", ["PlanFolder=/tmp/plans/00004-Split"]);

        Assert.Contains("model: sonnet", output);
    }

    // ==================== CreatePr ====================

    [Fact]
    public void CreatePr_CompilesFirmware()
    {
        var output = RunDryRun("CreatePr", ["PlanFolder=/tmp/plans/00005-PR"]);

        Assert.Contains("PlanFolder: /tmp/plans/00005-PR", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void CreatePr_IncludesSourceUrl()
    {
        var output = RunDryRun("CreatePr", ["PlanFolder=/tmp/plans/00005-PR", "SourceUrl=https://github.com/org/repo/issues/42"]);

        Assert.Contains("SourceUrl: https://github.com/org/repo/issues/42", output);
    }

    [Fact]
    public void CreatePr_ResolvesBalancedProfile()
    {
        var output = RunDryRun("CreatePr", ["PlanFolder=/tmp/plans/00005-PR"]);

        Assert.Contains("model: sonnet", output);
    }

    // ==================== CreateIssue ====================

    [Fact]
    public void CreateIssue_CompilesFirmware()
    {
        var output = RunDryRun("CreateIssue", ["PlanFolder=/tmp/plans/00006-Issue", "Repo=/repos/myapp"]);

        Assert.Contains("PlanFolder: /tmp/plans/00006-Issue", output);
        Assert.Contains("Repo: /repos/myapp", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void CreateIssue_IncludesOptionalFields()
    {
        var output = RunDryRun("CreateIssue", ["PlanFolder=/tmp/plans/00006-Issue", "Repo=/repos/myapp", "Assignee=octocat", "Comment=Needs urgent fix"]);

        Assert.Contains("Assignee: octocat", output);
        Assert.Contains("Comment: Needs urgent fix", output);
    }

    [Fact]
    public void CreateIssue_ResolvesBalancedProfile()
    {
        var output = RunDryRun("CreateIssue", ["PlanFolder=/tmp/plans/00006-Issue", "Repo=/repos/myapp"]);

        Assert.Contains("model: sonnet", output);
    }

    // ==================== UpdateProject ====================

    [Fact]
    public void UpdateProject_CompilesFirmware()
    {
        var output = RunDryRun("UpdateProject", ["ProjectName=TestProject", "Instructions=Setup verifications"]);

        Assert.Contains("ProjectName: TestProject", output);
        Assert.Contains("Instructions: Setup verifications", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void UpdateProject_ResolvesDeepProfile()
    {
        var output = RunDryRun("UpdateProject", ["ProjectName=TestProject", "Instructions=Setup"]);

        Assert.Contains("model: opus", output);
        Assert.Contains("effort: max", output);
    }

    // ==================== Cross-cutting ====================

    [Fact]
    public void AllPromptwareNames_ResolveWithoutError()
    {
        var promptwares = new[] { "CreatePlan", "ExecutePlan", "ExpandPlan", "UpdatePlan", "SplitPlan", "CreatePr", "CreateIssue", "UpdateProject" };

        foreach (var name in promptwares)
        {
            var output = RunDryRun(name, ["Args=test"]);
            Assert.Contains("Program.md", output);
            Assert.Contains($"promptware: {name}", output);
        }
    }

    [Fact]
    public void FirmwareAlwaysContainsCurrentTime()
    {
        var output = RunDryRun("CreatePlan", ["PlansDirectory=/tmp"]);

        Assert.Contains("CurrentTime:", output);
    }

    [Fact]
    public void DryRun_DoesNotLaunchProcess()
    {
        // Dry-run with a non-existent agent command — should not error since no process is started
        var command = new PromptwareRunCommand(NullLogger<PromptwareRunCommand>.Instance);
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var exitCode = command.Run(new PromptwareRunSettings
            {
                Promptware = "CreatePlan",
                DryRun = true,
                ConfigPath = _configPath,
                PromptwarePath = _promptwarePath,
                AgentCmd = "nonexistent-binary-that-should-not-run",
                Values = ["PlansDirectory=/tmp"]
            });
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
