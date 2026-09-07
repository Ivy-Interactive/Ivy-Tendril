using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class ProjectEnvFileCommandTests : IDisposable
{
    private readonly string _originalTendrilHome;
    private readonly TempDirectoryFixture _tempDir = new("tendril-project-envfile-test");

    public ProjectEnvFileCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), "projects: []\nverifications: []\n");
    }

    public void Dispose()
    {
        CliOutput.PlainOverride = null;
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private ConfigService CreateConfig() => new();

    private void SeedProject(params ProjectEnvFileConfig[] envFiles)
    {
        var config = CreateConfig();
        var project = new ProjectConfig { Name = "Tendril" };
        project.EnvFiles.AddRange(envFiles);
        config.Settings.Projects.Add(project);
        config.SaveSettings();
    }

    private static CommandApp BuildApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("project", project =>
            {
                project.AddBranch("env-file", envFile =>
                {
                    envFile.AddCommand<ProjectListEnvFilesCommand>("list");
                    envFile.AddCommand<ProjectAddEnvFileCommand>("add");
                    envFile.AddCommand<ProjectRemoveEnvFileCommand>("remove");
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

    [Fact]
    public void AddEnvFile_PersistsPathTemplateAndOverrides()
    {
        SeedProject();

        var exitCode = BuildApp().Run(["project", "env-file", "add", "Tendril", "apps/web/.env",
            "--template", ".env.example",
            "--override", "PORT=${ports.backend}",
            "--override", "LOG_LEVEL=debug"]);

        Assert.Equal(0, exitCode);
        var envFile = Assert.Single(CreateConfig().Settings.Projects[0].EnvFiles);
        Assert.Equal("apps/web/.env", envFile.Path);
        Assert.Equal(".env.example", envFile.Template);
        Assert.Equal("${ports.backend}", envFile.Overrides["PORT"]);
        Assert.Equal("debug", envFile.Overrides["LOG_LEVEL"]);
    }

    [Fact]
    public void AddEnvFile_WithoutTemplate_LeavesTemplateNull()
    {
        SeedProject();

        BuildApp().Run(["project", "env-file", "add", "Tendril", ".env"]);

        Assert.Null(CreateConfig().Settings.Projects[0].EnvFiles[0].Template);
    }

    [Fact]
    public void AddEnvFile_ValueContainingEquals_KeepsTheWholeValue()
    {
        SeedProject();

        BuildApp().Run(["project", "env-file", "add", "Tendril", ".env",
            "--override", "CONNECTION=Host=localhost;Port=5432"]);

        Assert.Equal(
            "Host=localhost;Port=5432",
            CreateConfig().Settings.Projects[0].EnvFiles[0].Overrides["CONNECTION"]);
    }

    [Fact]
    public void AddEnvFile_SamePath_ReplacesTheEntry()
    {
        SeedProject(new ProjectEnvFileConfig
        {
            Path = ".env",
            Overrides = new Dictionary<string, string> { ["OLD"] = "1" }
        });

        var output = CaptureConsoleOut(() =>
            BuildApp().Run(["project", "env-file", "add", "Tendril", ".env", "--override", "NEW=2"]));

        Assert.Contains("Updated environment file: .env", output);
        var envFile = Assert.Single(CreateConfig().Settings.Projects[0].EnvFiles);
        Assert.Equal(new[] { "NEW" }, envFile.Overrides.Keys);
    }

    [Fact]
    public void AddEnvFile_NewPath_ReportsAdded()
    {
        SeedProject();

        var output = CaptureConsoleOut(() =>
            BuildApp().Run(["project", "env-file", "add", "Tendril", ".env"]));

        Assert.Contains("Added environment file: .env", output);
    }

    [Fact]
    public void AddEnvFile_UnknownProject_ListsAvailable()
    {
        SeedProject();

        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildApp().Run(["project", "env-file", "add", "Missing", ".env"]));

        Assert.Contains("Available: Tendril", ex.Message);
    }

    [Fact]
    public void AddEnvFileSettings_Validate_OverrideWithoutEquals_Fails()
    {
        var settings = new ProjectAddEnvFileSettings
        {
            ProjectName = "Tendril",
            Path = ".env",
            Overrides = ["PORT"]
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--override must be KEY=VALUE", result.Message);
    }

    [Fact]
    public void AddEnvFileSettings_Validate_WellFormedOverride_Succeeds()
    {
        var settings = new ProjectAddEnvFileSettings
        {
            ProjectName = "Tendril",
            Path = ".env",
            Overrides = ["PORT=3001"]
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void ListEnvFiles_PlainMode_WritesTabSeparatedRows()
    {
        SeedProject(new ProjectEnvFileConfig
        {
            Path = ".env",
            Template = ".env.example",
            Overrides = new Dictionary<string, string> { ["PORT"] = "3001" }
        });
        CliOutput.PlainOverride = true;

        var output = CaptureConsoleOut(() => BuildApp().Run(["project", "env-file", "list", "Tendril"]));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("Path\tTemplate\tOverrides", lines[0]);
        Assert.Equal(".env\t.env.example\tPORT", lines[1]);
    }

    [Fact]
    public void ListEnvFiles_NoneConfigured_Succeeds()
    {
        SeedProject();

        Assert.Equal(0, BuildApp().Run(["project", "env-file", "list", "Tendril"]));
    }

    [Fact]
    public void RemoveEnvFile_DeletesFromConfig()
    {
        SeedProject(
            new ProjectEnvFileConfig { Path = ".env" },
            new ProjectEnvFileConfig { Path = "apps/web/.env" });

        var exitCode = BuildApp().Run(["project", "env-file", "remove", "Tendril", ".env"]);

        Assert.Equal(0, exitCode);
        var remaining = Assert.Single(CreateConfig().Settings.Projects[0].EnvFiles);
        Assert.Equal("apps/web/.env", remaining.Path);
    }

    [Fact]
    public void RemoveEnvFile_UnknownPath_Throws()
    {
        SeedProject(new ProjectEnvFileConfig { Path = ".env" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildApp().Run(["project", "env-file", "remove", "Tendril", "missing/.env"]));

        Assert.Contains("Environment file not found: missing/.env", ex.Message);
        Assert.Single(CreateConfig().Settings.Projects[0].EnvFiles);
    }
}
