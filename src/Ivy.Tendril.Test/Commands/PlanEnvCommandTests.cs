using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class PlanEnvCommandTests : IDisposable
{
    private readonly string _originalPlans;
    private readonly string _originalTendrilHome;
    private readonly string _plansDir;
    private readonly string _repoPath;
    private readonly TempDirectoryFixture _tempDir = new("tendril-plan-env-test");

    public PlanEnvCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS") ?? "";

        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(_plansDir);
        _repoPath = Path.Combine(_tempDir.Path, "repos", "acme", "widgets");
        Directory.CreateDirectory(_repoPath);

        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _plansDir);
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), "projects: []\nverifications: []\n");
    }

    public void Dispose()
    {
        CliOutput.PlainOverride = null;
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _originalPlans);
        _tempDir.Dispose();
    }

    private void SeedProject(
        Dictionary<string, ProjectPortConfig>? ports = null,
        List<ProjectEnvFileConfig>? envFiles = null)
    {
        var config = new ConfigService();
        config.Settings.Projects.Add(new ProjectConfig
        {
            Name = "Widgets",
            Ports = ports ?? new Dictionary<string, ProjectPortConfig>(),
            EnvFiles = envFiles ?? new List<ProjectEnvFileConfig>()
        });
        config.SaveSettings();
    }

    private string CreatePlan(string id = "21001", Dictionary<string, int>? allocatedPorts = null)
    {
        var planFolder = Path.Combine(_plansDir, $"{id}-PlanEnv");
        Directory.CreateDirectory(planFolder);

        var plan = new PlanYaml
        {
            State = "Executing",
            Project = "Widgets",
            Title = "PlanEnv",
            Repos = [_repoPath],
            AllocatedPorts = allocatedPorts
        };

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), YamlHelper.Serializer.Serialize(plan));
        return planFolder;
    }

    /// <summary>Creates the worktree directory the commands expect for the plan's single repo.</summary>
    private static string CreateWorktree(string planFolder, string repoPath)
    {
        var worktree = WorktreePathHelper.GetWorktreePath(planFolder, repoPath);
        Directory.CreateDirectory(worktree);
        return worktree;
    }

    private static CommandApp BuildApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("plan", plan =>
            {
                plan.AddBranch("env", env =>
                {
                    env.AddCommand<PlanEnvMaterializeCommand>("materialize");
                    env.AddCommand<PlanEnvGetCommand>("get");
                });
            });
        });
        return app;
    }

    private static string CaptureConsoleOut(Action action)
    {
        lock (TestLocks.ConsoleLock)
        {
            var original = Console.Out;
            var writer = new StringWriter();
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

    // Mirrors CliOutputTests: AnsiConsole caches its writer, so it has to be swapped separately.
    private static string CaptureAnsiConsoleOutput(Action action)
    {
        var original = AnsiConsole.Console;
        var writer = new StringWriter();
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        try
        {
            action();
        }
        finally
        {
            AnsiConsole.Console = original;
        }

        return writer.ToString();
    }

    // --- materialize ---

    [Fact]
    public void Materialize_WritesEnvFileIntoWorktree()
    {
        SeedProject(
            new Dictionary<string, ProjectPortConfig> { ["backend"] = new() { DefaultPort = 3001 } },
            [
                new ProjectEnvFileConfig
                {
                    Path = ".env",
                    Overrides = new Dictionary<string, string> { ["PORT"] = "${ports.backend}" }
                }
            ]);
        var planFolder = CreatePlan();
        var worktree = CreateWorktree(planFolder, _repoPath);

        var exitCode = BuildApp().Run(["plan", "env", "materialize", "21001"]);

        Assert.Equal(0, exitCode);
        var allocated = PlanCommandHelpers.ReadPlan(planFolder).AllocatedPorts!["backend"];
        Assert.Equal($"PORT={allocated}\n", File.ReadAllText(Path.Combine(worktree, ".env")));
    }

    [Fact]
    public void Materialize_RecordsAllocatedPortsOnThePlan()
    {
        SeedProject(new Dictionary<string, ProjectPortConfig> { ["backend"] = new() { DefaultPort = 3001 } });
        var planFolder = CreatePlan();
        CreateWorktree(planFolder, _repoPath);

        BuildApp().Run(["plan", "env", "materialize", "21001"]);

        Assert.Single(PlanCommandHelpers.ReadPlan(planFolder).AllocatedPorts!);
    }

    [Fact]
    public void Materialize_NoWorktree_ReturnsOne()
    {
        SeedProject();
        CreatePlan();

        var output = CaptureAnsiConsoleOutput(() =>
        {
            var exitCode = BuildApp().Run(["plan", "env", "materialize", "21001"]);
            Assert.Equal(1, exitCode);
        });

        Assert.Contains("No worktrees found for this plan", output);
    }

    [Fact]
    public void Materialize_RepoFilterMatchesRepoName_WritesTheFile()
    {
        SeedProject(envFiles:
        [
            new ProjectEnvFileConfig
            {
                Path = ".env",
                Overrides = new Dictionary<string, string> { ["A"] = "1" }
            }
        ]);
        var planFolder = CreatePlan();
        var worktree = CreateWorktree(planFolder, _repoPath);

        var exitCode = BuildApp().Run(["plan", "env", "materialize", "21001", "--repo", "widgets"]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(worktree, ".env")));
    }

    [Fact]
    public void Materialize_RepoFilterMatchesNothing_ReturnsOne()
    {
        SeedProject();
        var planFolder = CreatePlan();
        CreateWorktree(planFolder, _repoPath);

        var output = CaptureAnsiConsoleOutput(() =>
        {
            var exitCode = BuildApp().Run(["plan", "env", "materialize", "21001", "--repo", "gadgets"]);
            Assert.Equal(1, exitCode);
        });

        Assert.Contains("No worktree found for repo gadgets", output);
    }

    [Fact]
    public void Materialize_NoEnvFilesConfigured_SucceedsWithoutWriting()
    {
        SeedProject(new Dictionary<string, ProjectPortConfig> { ["backend"] = new() { DefaultPort = 3001 } });
        var planFolder = CreatePlan();
        var worktree = CreateWorktree(planFolder, _repoPath);

        var exitCode = BuildApp().Run(["plan", "env", "materialize", "21001"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(Directory.GetFiles(worktree));
    }

    [Fact]
    public void Materialize_UnknownProject_ListsAvailable()
    {
        SeedProject();
        var planFolder = Path.Combine(_plansDir, "21002-Orphan");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), YamlHelper.Serializer.Serialize(new PlanYaml
        {
            State = "Executing",
            Project = "Gone",
            Title = "Orphan",
            Repos = [_repoPath]
        }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildApp().Run(["plan", "env", "materialize", "21002"]));

        Assert.Contains("Available: Widgets", ex.Message);
    }

    [Fact]
    public void Materialize_UnknownPlan_Throws()
    {
        SeedProject();

        Assert.Throws<DirectoryNotFoundException>(() => BuildApp().Run(["plan", "env", "materialize", "29999"]));
    }

    [Fact]
    public void MaterializeSettings_Validate_BlankPlanId_Fails()
    {
        Assert.False(new PlanEnvMaterializeSettings { PlanId = "" }.Validate().Successful);
    }

    // --- get ---

    [Fact]
    public void Get_PlainMode_ListsAllocatedPorts()
    {
        SeedProject(new Dictionary<string, ProjectPortConfig> { ["backend"] = new() { DefaultPort = 3001 } });
        CreatePlan(allocatedPorts: new Dictionary<string, int> { ["backend"] = 31801 });
        CliOutput.PlainOverride = true;

        var output = CaptureConsoleOut(() => BuildApp().Run(["plan", "env", "get", "21001"]));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("Port\tValue", lines[0]);
        Assert.Equal("backend\t31801", lines[1]);
    }

    [Fact]
    public void Get_PrintsResolvedEnvValues()
    {
        SeedProject(
            new Dictionary<string, ProjectPortConfig> { ["backend"] = new() { DefaultPort = 3001 } },
            [
                new ProjectEnvFileConfig
                {
                    Path = ".env",
                    Overrides = new Dictionary<string, string> { ["PORT"] = "${ports.backend}" }
                }
            ]);
        CreatePlan(allocatedPorts: new Dictionary<string, int> { ["backend"] = 31802 });
        CliOutput.PlainOverride = true;

        var output = CaptureConsoleOut(() => BuildApp().Run(["plan", "env", "get", "21001"]));

        Assert.Contains("PORT=31802", output);
    }

    [Fact]
    public void Get_DoesNotAllocateOrWrite()
    {
        SeedProject(
            new Dictionary<string, ProjectPortConfig> { ["backend"] = new() { DefaultPort = 3001 } },
            [new ProjectEnvFileConfig { Path = ".env", Overrides = new Dictionary<string, string> { ["A"] = "1" } }]);
        var planFolder = CreatePlan();
        var worktree = CreateWorktree(planFolder, _repoPath);

        var exitCode = BuildApp().Run(["plan", "env", "get", "21001"]);

        Assert.Equal(0, exitCode);
        Assert.Null(PlanCommandHelpers.ReadPlan(planFolder).AllocatedPorts);
        Assert.Empty(Directory.GetFiles(worktree));
    }

    [Fact]
    public void Get_NoWorktree_StillResolvesOverrides()
    {
        SeedProject(envFiles:
            [new ProjectEnvFileConfig { Path = ".env", Overrides = new Dictionary<string, string> { ["A"] = "1" } }]);
        CreatePlan();
        CliOutput.PlainOverride = true;

        var output = CaptureConsoleOut(() => BuildApp().Run(["plan", "env", "get", "21001"]));

        Assert.Contains("A=1", output);
    }
}
