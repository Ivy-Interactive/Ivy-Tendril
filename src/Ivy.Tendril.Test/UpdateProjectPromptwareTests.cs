using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Agents;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

/// <summary>
/// End-to-end tests for the UpdateProject promptware.
/// These tests verify:
/// 1. The promptware resolves correctly with --dry-run (firmware compilation)
/// 2. The CLI commands the promptware would execute produce the expected config state
///    for various tech stacks (.NET, Node.js, Python, Rust, multi-stack)
/// </summary>
[Collection("TendrilHome")]
public class UpdateProjectPromptwareTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-update-project-e2e");
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;

    public UpdateProjectPromptwareTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalTendrilPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);

        Directory.CreateDirectory(Path.Combine(_tempDir.Path, "Plans"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _originalTendrilPlans);
        _tempDir.Dispose();
    }

    private void WriteConfig(string yaml)
    {
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), yaml);
    }

    private ConfigService LoadConfig() => new();

    private string CreateRepo(string name, params string[] files)
    {
        var repoPath = Path.Combine(_tempDir.Path, "repos", name);
        Directory.CreateDirectory(repoPath);
        foreach (var file in files)
        {
            var filePath = Path.Combine(repoPath, file);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "");
        }
        return repoPath;
    }

    private string CreatePromptwareDir(string name)
    {
        var promptwaresRoot = Path.Combine(_tempDir.Path, "Promptwares");
        var dir = Path.Combine(promptwaresRoot, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Program.md"), $"# {name}\n");
        return promptwaresRoot;
    }

    // ==================== Firmware Compilation Tests ====================

    [Fact]
    public void DryRun_EmitsFirmwareWithProjectNameAndInstructions()
    {
        var repoPath = CreateRepo("MyApp", "MyApp.csproj");
        WriteConfig($"""
            projects:
            - name: MyApp
              repos:
              - path: {repoPath}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation matches the plan.
            promptwares:
              UpdateProject:
                profile: deep
                allowedTools:
                - Read
                - Glob
                - Grep
                - Bash
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            codingAgent: claude
            """);

        var promptwarePath = CreatePromptwareDir("UpdateProject");

        var command = new PromptwareRunCommand(NullLogger<PromptwareRunCommand>.Instance);
        var output = CaptureConsoleOutput(() =>
        {
            command.Run(new PromptwareRunSettings
            {
                Promptware = "UpdateProject",
                DryRun = true,
                ConfigPath = Path.Combine(_tempDir.Path, "config.yaml"),
                PromptwarePath = promptwarePath,
                Values = ["ProjectName=MyApp", "Instructions=Setup verifications and review actions"]
            });
        });

        Assert.Contains("ProjectName: MyApp", output);
        Assert.Contains("Instructions: Setup verifications and review actions", output);
        Assert.Contains("Program.md", output);
    }

    [Fact]
    public void DryRun_ResolvesAgentModelFromConfig()
    {
        var repoPath = CreateRepo("TestRepo", "index.js");
        WriteConfig($"""
            projects:
            - name: TestProject
              repos:
              - path: {repoPath}
            verifications: []
            promptwares:
              UpdateProject:
                profile: deep
                allowedTools:
                - Read
                - Bash
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            codingAgent: claude
            """);

        var promptwarePath = CreatePromptwareDir("UpdateProject");

        var command = new PromptwareRunCommand(NullLogger<PromptwareRunCommand>.Instance);
        var output = CaptureConsoleOutput(() =>
        {
            command.Run(new PromptwareRunSettings
            {
                Promptware = "UpdateProject",
                DryRun = true,
                ConfigPath = Path.Combine(_tempDir.Path, "config.yaml"),
                PromptwarePath = promptwarePath,
                Values = ["ProjectName=TestProject", "Instructions=Setup"]
            });
        });

        Assert.Contains("model: opus", output);
        Assert.Contains("effort: max", output);
    }

    // ==================== .NET Stack Tests ====================

    [Fact]
    public void DotnetStack_AddVerificationsAndReviewAction()
    {
        var repoPath = CreateRepo("MyDotnetApp", "src/MyDotnetApp/MyDotnetApp.csproj", "src/MyDotnetApp/Program.cs");
        WriteConfig($"""
            projects:
            - name: MyDotnetApp
              repos:
              - path: {repoPath}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation matches the plan.
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            """);

        var config = LoadConfig();

        // Simulate what the promptware does: detect .NET, add verifications
        config.Settings.Verifications.Add(new VerificationConfig { Name = "DotnetBuild", Prompt = "Run `dotnet build --warnaserror` in the plan's worktree directory and verify it succeeds with zero errors and zero warnings." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "DotnetFormat", Prompt = "Run `dotnet format` scoped to changed .cs files, commit fixes if any." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "DotnetTest", Prompt = "Run `dotnet test` with filter from plan's Tests section." });
        config.SaveSettings();

        // Add verification references to project
        var config2 = LoadConfig();
        var project = config2.Settings.Projects[0];
        project.Verifications.Add(new ProjectVerificationRef { Name = "DotnetBuild", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "DotnetFormat", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "DotnetTest", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "CheckResult", Required = true });
        project.ReviewActions.Add(new ReviewActionConfig
        {
            Name = "App",
            Condition = "Test-Path \"worktrees/MyDotnetApp/src/MyDotnetApp\"",
            Command = "dotnet run --project worktrees/MyDotnetApp/src/MyDotnetApp --browse --find-available-port"
        });
        config2.SaveSettings();

        // Verify final state
        var result = LoadConfig();
        Assert.Equal(4, result.Settings.Verifications.Count);
        Assert.Contains(result.Settings.Verifications, v => v.Name == "DotnetBuild");
        Assert.Contains(result.Settings.Verifications, v => v.Name == "DotnetFormat");
        Assert.Contains(result.Settings.Verifications, v => v.Name == "DotnetTest");
        Assert.Contains(result.Settings.Verifications, v => v.Name == "CheckResult");

        var proj = result.Settings.Projects[0];
        Assert.Equal(4, proj.Verifications.Count);
        Assert.Single(proj.ReviewActions);
        Assert.Equal("App", proj.ReviewActions[0].Name);
        Assert.Contains("dotnet run", proj.ReviewActions[0].Command);
        Assert.Contains("MyDotnetApp", proj.ReviewActions[0].Condition);
    }

    // ==================== Node.js Stack Tests ====================

    [Fact]
    public void NodeStack_AddVerificationsAndReviewAction()
    {
        var repoPath = CreateRepo("MyNodeApp", "package.json", "src/index.ts", "tsconfig.json");
        WriteConfig($"""
            projects:
            - name: MyNodeApp
              repos:
              - path: {repoPath}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation matches the plan.
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            """);

        var config = LoadConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "NpmLint", Prompt = "Run `npm run lint` on changed files and fix any errors." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "NpmBuild", Prompt = "Run `npm run build` and verify success." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "NpmTest", Prompt = "Run `npm test` with appropriate filter." });
        config.SaveSettings();

        var config2 = LoadConfig();
        var project = config2.Settings.Projects[0];
        project.Verifications.Add(new ProjectVerificationRef { Name = "NpmLint", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "NpmBuild", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "NpmTest", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "CheckResult", Required = true });
        project.ReviewActions.Add(new ReviewActionConfig
        {
            Name = "Dev",
            Condition = "Test-Path \"worktrees/MyNodeApp/package.json\"",
            Command = "cd worktrees/MyNodeApp && npm run dev"
        });
        config2.SaveSettings();

        var result = LoadConfig();
        Assert.Equal(4, result.Settings.Verifications.Count);
        Assert.Contains(result.Settings.Verifications, v => v.Name == "NpmLint");
        Assert.Contains(result.Settings.Verifications, v => v.Name == "NpmBuild");
        Assert.Contains(result.Settings.Verifications, v => v.Name == "NpmTest");

        var proj = result.Settings.Projects[0];
        Assert.Equal(4, proj.Verifications.Count);
        Assert.Single(proj.ReviewActions);
        Assert.Contains("npm run dev", proj.ReviewActions[0].Command);
    }

    // ==================== Python Stack Tests ====================

    [Fact]
    public void PythonStack_AddVerificationsAndReviewAction()
    {
        var repoPath = CreateRepo("MyPythonApp", "pyproject.toml", "src/main.py", "tests/test_main.py");
        WriteConfig($"""
            projects:
            - name: MyPythonApp
              repos:
              - path: {repoPath}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation matches the plan.
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            """);

        var config = LoadConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "PythonLint", Prompt = "Run linter (black/ruff/flake8) on changed .py files." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "PythonTest", Prompt = "Run `pytest` with filter from plan's Tests section." });
        config.SaveSettings();

        var config2 = LoadConfig();
        var project = config2.Settings.Projects[0];
        project.Verifications.Add(new ProjectVerificationRef { Name = "PythonLint", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "PythonTest", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "CheckResult", Required = true });
        project.ReviewActions.Add(new ReviewActionConfig
        {
            Name = "App",
            Condition = "Test-Path \"worktrees/MyPythonApp/src/main.py\"",
            Command = "cd worktrees/MyPythonApp && python -m src.main"
        });
        config2.SaveSettings();

        var result = LoadConfig();
        Assert.Equal(3, result.Settings.Verifications.Count);
        Assert.Contains(result.Settings.Verifications, v => v.Name == "PythonLint");
        Assert.Contains(result.Settings.Verifications, v => v.Name == "PythonTest");

        var proj = result.Settings.Projects[0];
        Assert.Equal(3, proj.Verifications.Count);
        Assert.Single(proj.ReviewActions);
        Assert.Contains("python", proj.ReviewActions[0].Command);
    }

    // ==================== Rust Stack Tests ====================

    [Fact]
    public void RustStack_AddVerificationsAndReviewAction()
    {
        var repoPath = CreateRepo("MyRustApp", "Cargo.toml", "src/main.rs");
        WriteConfig($"""
            projects:
            - name: MyRustApp
              repos:
              - path: {repoPath}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation matches the plan.
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            """);

        var config = LoadConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "RustBuild", Prompt = "Run `cargo build --release` and verify success." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "RustTest", Prompt = "Run `cargo test` and verify all pass." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "RustClippy", Prompt = "Run `cargo clippy -- -D warnings`." });
        config.SaveSettings();

        var config2 = LoadConfig();
        var project = config2.Settings.Projects[0];
        project.Verifications.Add(new ProjectVerificationRef { Name = "RustBuild", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "RustTest", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "RustClippy", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "CheckResult", Required = true });
        project.ReviewActions.Add(new ReviewActionConfig
        {
            Name = "Run",
            Condition = "Test-Path \"worktrees/MyRustApp/Cargo.toml\"",
            Command = "cd worktrees/MyRustApp && cargo run --release"
        });
        config2.SaveSettings();

        var result = LoadConfig();
        Assert.Equal(4, result.Settings.Verifications.Count);
        Assert.Contains(result.Settings.Verifications, v => v.Name == "RustBuild");
        Assert.Contains(result.Settings.Verifications, v => v.Name == "RustClippy");

        var proj = result.Settings.Projects[0];
        Assert.Equal(4, proj.Verifications.Count);
        Assert.Single(proj.ReviewActions);
        Assert.Contains("cargo run", proj.ReviewActions[0].Command);
    }

    // ==================== Shared Verification Reuse Tests ====================

    [Fact]
    public void SharedVerifications_ReuseExistingDefinitions()
    {
        var repo1 = CreateRepo("App1", "App1.csproj");
        var repo2 = CreateRepo("App2", "App2.csproj");
        WriteConfig($"""
            projects:
            - name: App1
              repos:
              - path: {repo1}
              verifications:
              - name: DotnetBuild
                required: true
              - name: DotnetFormat
                required: true
              - name: CheckResult
                required: true
            - name: App2
              repos:
              - path: {repo2}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation matches the plan.
            - name: DotnetBuild
              prompt: Run dotnet build --warnaserror.
            - name: DotnetFormat
              prompt: Run dotnet format on changed files.
            - name: DotnetTest
              prompt: Run dotnet test with filter.
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            """);

        // Simulate UpdateProject for App2 — should reuse existing verifications
        var config = LoadConfig();
        var app2 = config.Settings.Projects.First(p => p.Name == "App2");

        // Verifications already exist globally — just add references
        app2.Verifications.Add(new ProjectVerificationRef { Name = "DotnetBuild", Required = true });
        app2.Verifications.Add(new ProjectVerificationRef { Name = "DotnetFormat", Required = true });
        app2.Verifications.Add(new ProjectVerificationRef { Name = "DotnetTest", Required = true });
        app2.Verifications.Add(new ProjectVerificationRef { Name = "CheckResult", Required = true });
        config.SaveSettings();

        // Verify: no new global definitions were created (still 4)
        var result = LoadConfig();
        Assert.Equal(4, result.Settings.Verifications.Count);

        // Both projects reference the same verifications
        var app2Result = result.Settings.Projects.First(p => p.Name == "App2");
        Assert.Equal(4, app2Result.Verifications.Count);
    }

    // ==================== Multi-Stack Tests ====================

    [Fact]
    public void MultiStack_DotnetAndNode_CombinesVerifications()
    {
        var repoPath = CreateRepo("FullStackApp", "Backend.csproj", "frontend/package.json", "frontend/tsconfig.json");
        WriteConfig($"""
            projects:
            - name: FullStackApp
              repos:
              - path: {repoPath}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation matches the plan.
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            """);

        var config = LoadConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "DotnetBuild", Prompt = "Run dotnet build --warnaserror." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "DotnetFormat", Prompt = "Run dotnet format on changed files." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "DotnetTest", Prompt = "Run dotnet test." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "NpmLint", Prompt = "Run npm run lint." });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "NpmBuild", Prompt = "Run npm run build." });
        config.SaveSettings();

        var config2 = LoadConfig();
        var project = config2.Settings.Projects[0];
        project.Verifications.Add(new ProjectVerificationRef { Name = "DotnetBuild", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "DotnetFormat", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "DotnetTest", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "NpmLint", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "NpmBuild", Required = true });
        project.Verifications.Add(new ProjectVerificationRef { Name = "CheckResult", Required = true });
        project.ReviewActions.Add(new ReviewActionConfig
        {
            Name = "Backend",
            Condition = "Test-Path \"worktrees/FullStackApp/Backend.csproj\"",
            Command = "dotnet run --project worktrees/FullStackApp --browse --find-available-port"
        });
        project.ReviewActions.Add(new ReviewActionConfig
        {
            Name = "Frontend",
            Condition = "Test-Path \"worktrees/FullStackApp/frontend/package.json\"",
            Command = "cd worktrees/FullStackApp/frontend && npm run dev"
        });
        config2.SaveSettings();

        var result = LoadConfig();
        Assert.Equal(6, result.Settings.Verifications.Count);

        var proj = result.Settings.Projects[0];
        Assert.Equal(6, proj.Verifications.Count);
        Assert.Equal(2, proj.ReviewActions.Count);
        Assert.Equal("Backend", proj.ReviewActions[0].Name);
        Assert.Equal("Frontend", proj.ReviewActions[1].Name);
    }

    // ==================== CLI Integration Tests ====================
    // These tests verify the actual CLI commands work end-to-end

    [Fact]
    public void CliIntegration_VerificationAddAndProjectReference()
    {
        var repoPath = CreateRepo("CliTest", "app.csproj");
        WriteConfig($"""
            projects:
            - name: CliTest
              repos:
              - path: {repoPath}
            verifications:
            - name: CheckResult
              prompt: Verify the implementation.
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            """);

        // Use the actual config service (same as CLI commands do)
        var config = LoadConfig();
        Assert.Single(config.Settings.Verifications);

        // Step 1: Add a verification definition (like: tendril verification add DotnetBuild --prompt "...")
        config.Settings.Verifications.Add(new VerificationConfig
        {
            Name = "DotnetBuild",
            Prompt = "Run `dotnet build --warnaserror` and verify success."
        });
        config.SaveSettings();

        // Step 2: Add verification ref to project (like: tendril project add-verification CliTest DotnetBuild --required)
        var config2 = LoadConfig();
        config2.Settings.Projects[0].Verifications.Add(new ProjectVerificationRef { Name = "DotnetBuild", Required = true });
        config2.Settings.Projects[0].Verifications.Add(new ProjectVerificationRef { Name = "CheckResult", Required = true });
        config2.SaveSettings();

        // Step 3: Add review action (like: tendril project add-review-action CliTest App --command "..." --condition "...")
        var config3 = LoadConfig();
        config3.Settings.Projects[0].ReviewActions.Add(new ReviewActionConfig
        {
            Name = "App",
            Command = "dotnet run --project worktrees/CliTest --browse --find-available-port",
            Condition = "Test-Path \"worktrees/CliTest\""
        });
        config3.SaveSettings();

        // Verify final state persisted correctly
        var final = LoadConfig();
        Assert.Equal(2, final.Settings.Verifications.Count);
        Assert.Equal("DotnetBuild", final.Settings.Verifications[1].Name);
        Assert.Contains("dotnet build", final.Settings.Verifications[1].Prompt);

        var proj = final.Settings.Projects[0];
        Assert.Equal(2, proj.Verifications.Count);
        Assert.True(proj.Verifications[0].Required);
        Assert.Single(proj.ReviewActions);
        Assert.Contains("dotnet run", proj.ReviewActions[0].Command);
    }

    [Fact]
    public void CliIntegration_ReviewActionWithCondition()
    {
        var repoPath = CreateRepo("WebApp", "src/WebApp/WebApp.csproj", "src/WebApp.Tests/WebApp.Tests.csproj");
        WriteConfig($"""
            projects:
            - name: WebApp
              repos:
              - path: {repoPath}
            verifications: []
            codingAgent: claude
            codingAgents:
            - name: claude
              profiles:
              - name: deep
                model: opus
                effort: max
            """);

        var config = LoadConfig();
        config.Settings.Projects[0].ReviewActions.Add(new ReviewActionConfig
        {
            Name = "WebApp",
            Condition = "Test-Path \"worktrees/WebApp/src/WebApp\"",
            Command = "dotnet run --project worktrees/WebApp/src/WebApp --browse --find-available-port"
        });
        config.SaveSettings();

        var result = LoadConfig();
        var action = result.Settings.Projects[0].ReviewActions[0];
        Assert.Equal("WebApp", action.Name);
        Assert.Contains("Test-Path", action.Condition);
        Assert.Contains("WebApp", action.Condition);
        Assert.Contains("--browse", action.Command);
    }

    // ==================== Helpers ====================

    private static string CaptureConsoleOutput(Action action)
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            action();
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
