using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class ProjectPortCommandTests : IDisposable
{
    private readonly string _originalTendrilHome;
    private readonly TempDirectoryFixture _tempDir = new("tendril-project-port-test");

    public ProjectPortCommandTests()
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

    private void SeedProject(params (string Name, int DefaultPort)[] ports)
    {
        var config = CreateConfig();
        var project = new ProjectConfig { Name = "Tendril" };
        foreach (var (name, defaultPort) in ports)
            project.Ports[name] = new ProjectPortConfig { DefaultPort = defaultPort };
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
                project.AddBranch("port", port =>
                {
                    port.AddCommand<ProjectListPortsCommand>("list");
                    port.AddCommand<ProjectAddPortCommand>("add");
                    port.AddCommand<ProjectRemovePortCommand>("remove");
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
    public void AddPort_PersistsToConfig()
    {
        SeedProject();

        var exitCode = BuildApp().Run(["project", "port", "add", "Tendril", "backend", "3001",
            "--description", "ASP.NET API"]);

        Assert.Equal(0, exitCode);
        var port = Assert.Single(CreateConfig().Settings.Projects[0].Ports);
        Assert.Equal("backend", port.Key);
        Assert.Equal(3001, port.Value.DefaultPort);
        Assert.Equal("ASP.NET API", port.Value.Description);
    }

    [Fact]
    public void AddPort_WithoutDescription_StoresEmptyDescription()
    {
        SeedProject();

        BuildApp().Run(["project", "port", "add", "Tendril", "backend", "3001"]);

        Assert.Equal("", CreateConfig().Settings.Projects[0].Ports["backend"].Description);
    }

    [Fact]
    public void AddPort_ExistingName_UpdatesInPlace()
    {
        SeedProject(("backend", 3001));

        var output = CaptureConsoleOut(() =>
            BuildApp().Run(["project", "port", "add", "Tendril", "backend", "4001"]));

        Assert.Contains("Updated port: backend -> 4001", output);
        var port = Assert.Single(CreateConfig().Settings.Projects[0].Ports);
        Assert.Equal(4001, port.Value.DefaultPort);
    }

    [Fact]
    public void AddPort_NewName_ReportsAdded()
    {
        SeedProject();

        var output = CaptureConsoleOut(() =>
            BuildApp().Run(["project", "port", "add", "Tendril", "frontend", "3000"]));

        Assert.Contains("Added port: frontend -> 3000", output);
    }

    [Fact]
    public void AddPort_UnknownProject_ListsAvailable()
    {
        SeedProject();

        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildApp().Run(["project", "port", "add", "Missing", "backend", "3001"]));

        Assert.Contains("Available: Tendril", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("70000")]
    public void AddPort_OutOfRangePort_FailsValidation(string port)
    {
        var settings = new ProjectAddPortSettings
        {
            ProjectName = "Tendril",
            Name = "backend",
            DefaultPort = int.Parse(port)
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("default-port must be between 1 and 65535", result.Message);
    }

    [Fact]
    public void AddPortSettings_Validate_ValidPort_Succeeds()
    {
        var settings = new ProjectAddPortSettings
        {
            ProjectName = "Tendril",
            Name = "backend",
            DefaultPort = 3001
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void ListPorts_PlainMode_WritesTabSeparatedRows()
    {
        SeedProject();
        BuildApp().Run(["project", "port", "add", "Tendril", "backend", "3001", "--description", "API"]);
        CliOutput.PlainOverride = true;

        var output = CaptureConsoleOut(() => BuildApp().Run(["project", "port", "list", "Tendril"]));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("Name\tDefault Port\tDescription", lines[0]);
        Assert.Equal("backend\t3001\tAPI", lines[1]);
    }

    [Fact]
    public void ListPorts_NoPortsConfigured_Succeeds()
    {
        SeedProject();

        Assert.Equal(0, BuildApp().Run(["project", "port", "list", "Tendril"]));
    }

    [Fact]
    public void RemovePort_DeletesFromConfig()
    {
        SeedProject(("backend", 3001), ("frontend", 3000));

        var exitCode = BuildApp().Run(["project", "port", "remove", "Tendril", "backend"]);

        Assert.Equal(0, exitCode);
        var remaining = Assert.Single(CreateConfig().Settings.Projects[0].Ports);
        Assert.Equal("frontend", remaining.Key);
    }

    [Fact]
    public void RemovePort_UnknownName_Throws()
    {
        SeedProject(("backend", 3001));

        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildApp().Run(["project", "port", "remove", "Tendril", "missing"]));

        Assert.Contains("Port not found: missing", ex.Message);
        // The failed mutation must not have dropped the port that does exist.
        Assert.Single(CreateConfig().Settings.Projects[0].Ports);
    }
}
