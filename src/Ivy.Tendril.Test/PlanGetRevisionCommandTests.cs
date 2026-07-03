using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class PlanGetRevisionCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;
    private readonly string _plansDir;

    public PlanGetRevisionCommandTests()
    {
        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(_plansDir);

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

    private string CreatePlanFolder(string id, string title)
    {
        var folderName = $"{id}-{title}";
        var planDir = Path.Combine(_plansDir, folderName);
        Directory.CreateDirectory(planDir);

        var plan = new PlanYaml
        {
            State = "Draft",
            Project = "TestProject",
            Title = title,
            Repos = [_tempDir.Path],
            Created = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
        };
        var yaml = YamlHelper.Serializer.Serialize(plan);
        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), yaml);
        return planDir;
    }

    private static CommandApp BuildApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("plan", plan => plan.AddCommand<PlanGetRevisionCommand>("get-revision"));
        });
        return app;
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

    [Fact]
    public void GetRevision_DefaultInvocation_PrintsLatestRevisionContent()
    {
        var planDir = CreatePlanFolder("40001", "LatestTest");
        var revisionsDir = Path.Combine(planDir, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "# First revision");
        File.WriteAllText(Path.Combine(revisionsDir, "002.md"), "# Second revision");

        var app = BuildApp();
        var output = CaptureStdout(() =>
        {
            var exit = app.Run(["plan", "get-revision", "40001"]);
            Assert.Equal(0, exit);
        });

        Assert.Equal("# Second revision", output);
    }

    [Fact]
    public void GetRevision_ExplicitNumber_PrintsThatRevisionContent()
    {
        var planDir = CreatePlanFolder("40002", "NumberTest");
        var revisionsDir = Path.Combine(planDir, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "# First revision");
        File.WriteAllText(Path.Combine(revisionsDir, "002.md"), "# Second revision");

        var app = BuildApp();
        var output = CaptureStdout(() =>
        {
            var exit = app.Run(["plan", "get-revision", "40002", "--number=1"]);
            Assert.Equal(0, exit);
        });

        Assert.Equal("# First revision", output);
    }

    [Fact]
    public void GetRevision_NoRevisionsExist_Throws()
    {
        CreatePlanFolder("40003", "NoRevisionsTest");

        var app = BuildApp();
        Assert.Throws<FileNotFoundException>(() =>
            app.Run(["plan", "get-revision", "40003"]));
    }

    [Fact]
    public void GetRevision_NonExistentNumber_Throws()
    {
        var planDir = CreatePlanFolder("40004", "MissingNumberTest");
        var revisionsDir = Path.Combine(planDir, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "# First revision");

        var app = BuildApp();
        Assert.Throws<FileNotFoundException>(() =>
            app.Run(["plan", "get-revision", "40004", "--number=9"]));
    }
}
