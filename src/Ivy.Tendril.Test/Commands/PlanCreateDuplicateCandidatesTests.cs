using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Infrastructure;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

/// <summary>
///     The two CLI surfaces that surface <c>DuplicateCandidates</c>: <c>plan create</c> (stdout,
///     after the existing blocks) and <c>plan write-revision</c> (stderr, exit code unchanged).
/// </summary>
[Collection("TendrilHome")]
public class PlanCreateDuplicateCandidatesTests : IDisposable
{
    private const string Project = "Rusty-Framework";
    private const string Title00063 = "Add the Missing Cargo Fmt Pre-Commit Guard From Plan 00042";
    private const string Title00065 = "Deliver the Blocked Cargo Fmt Pre-Commit Hook and Correct Plan 00042's Motivation";

    private readonly TempDirectoryFixture _tempDir = new("tendril-create-duplicates");
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;
    private readonly string _plansDir;

    public PlanCreateDuplicateCandidatesTests()
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

    private CommandApp BuildApp()
    {
        var repoDir = Path.Combine(_tempDir.Path, "repos", Project);
        Directory.CreateDirectory(repoDir);

        var services = new ServiceCollection();
        services.AddSingleton<IPlanWatcherService, NullPlanWatcherService>();
        var configService = new TestPlanConfigService(repoDir, Project);
        services.AddSingleton<IConfigService>(configService);
        services.AddSingleton<IGithubService>(new GithubService(configService, NullLogger<GithubService>.Instance));

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("plan", plan =>
            {
                plan.AddCommand<PlanCreateCommand>("create");
                plan.AddCommand<PlanWriteRevisionCommand>("write-revision");
            });
        });
        return app;
    }

    private string SeedPlan(string folderName, string title, string project = Project, string state = "Completed")
    {
        var planDir = Path.Combine(_plansDir, folderName);
        Directory.CreateDirectory(planDir);

        var plan = new PlanYaml
        {
            State = state,
            Project = project,
            Title = title,
            Level = "Bug",
            Created = new DateTime(2026, 8, 1, 20, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 8, 1, 20, 0, 0, DateTimeKind.Utc)
        };

        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), YamlHelper.Serializer.Serialize(plan));
        return planDir;
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

    private static string CaptureStderr(Action action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }

    [Fact]
    public void PlanCreate_MatchingSibling_PrintsBlockAfterVerifications()
    {
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063);
        var app = BuildApp();

        var exit = 0;
        var stdout = CaptureStdout(() => exit = app.Run(["plan", "create", Title00065, Project]));

        Assert.Equal(0, exit);
        var lines = stdout.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();

        var headerIndex = Array.IndexOf(lines, "DuplicateCandidates:");
        var verificationsIndex = Array.IndexOf(lines, "Verifications:");
        Assert.True(headerIndex > verificationsIndex, "the block must come after the Verifications block");
        Assert.Equal(
            $"00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042|{Title00063}|Completed",
            lines[headerIndex + 1]);
    }

    [Fact]
    public void PlanCreate_NoCandidates_OmitsTheHeaderEntirely()
    {
        SeedPlan("00200-SomethingElseCompletely", "Something Else Completely");
        var app = BuildApp();

        var stdout = CaptureStdout(() => app.Run(["plan", "create", Title00065, Project]));

        Assert.DoesNotContain("DuplicateCandidates", stdout);
    }

    [Fact]
    public void PlanCreate_ExistingBlocks_AreUnchangedByTheAddition()
    {
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063);
        var app = BuildApp();

        var stdout = CaptureStdout(() => app.Run(["plan", "create", Title00065, Project]));

        var lines = stdout.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var planFolder = Directory.GetDirectories(_plansDir, "*-Deliver*").Single();
        var planId = PathHelper.GetFileNameCrossPlatform(planFolder).Split('-')[0];

        // The three blocks every agent parses, in the same order and shape as before.
        Assert.Equal($"PlanId: {planId}", lines[0]);
        Assert.Equal($"Directory: {planFolder}", lines[1]);
        Assert.Equal("Verifications:", lines[2]);
    }

    [Fact]
    public void PlanCreate_NoDuplicateCheck_SuppressesTheBlock()
    {
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063);
        var app = BuildApp();

        var exit = 0;
        var stdout = CaptureStdout(() =>
            exit = app.Run(["plan", "create", Title00065, Project, "--no-duplicate-check"]));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("DuplicateCandidates", stdout);
    }

    [Fact]
    public void PlanCreate_OtherProjectSibling_IsNotReported()
    {
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063, "Ivy-Tendril");
        var app = BuildApp();

        var stdout = CaptureStdout(() => app.Run(["plan", "create", Title00065, Project]));

        Assert.DoesNotContain("DuplicateCandidates", stdout);
    }

    [Fact]
    public void PlanWriteRevision_WithCandidates_WarnsOnStderrAndStillWritesTheRevision()
    {
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063);
        var ownFolder = SeedPlan("00065-DeliverTheBlockedCargoFmtPreCommitHook", Title00065, Project, "Draft");
        var app = BuildApp();

        var exit = 0;
        var stderr = CaptureStderr(() => CaptureStdout(() =>
            exit = app.Run(["plan", "write-revision", "00065", "--file", WriteRevisionFile()])));

        // The revision must be written regardless: a false positive must not block plan creation.
        Assert.Equal(0, exit);
        var revision = Directory.GetFiles(Path.Combine(ownFolder, "Revisions")).Single();
        Assert.Contains("# Revision body", File.ReadAllText(revision));

        Assert.Contains("warning: 1 possible duplicate plan(s) found", stderr);
        Assert.Contains("DuplicateCandidates:", stderr);
        Assert.Contains($"00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042|{Title00063}|Completed", stderr);

        // The plan must never match itself.
        Assert.DoesNotContain("00065-DeliverTheBlockedCargoFmtPreCommitHook", stderr);
    }

    [Fact]
    public void PlanWriteRevision_NoCandidates_WritesNoWarning()
    {
        SeedPlan("00200-SomethingElseCompletely", "Something Else Completely");
        SeedPlan("00065-DeliverTheBlockedCargoFmtPreCommitHook", Title00065, Project, "Draft");
        var app = BuildApp();

        var exit = 0;
        var stderr = CaptureStderr(() => CaptureStdout(() =>
            exit = app.Run(["plan", "write-revision", "00065", "--file", WriteRevisionFile()])));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("DuplicateCandidates", stderr);
        Assert.DoesNotContain("warning:", stderr);
    }

    [Fact]
    public void PlanWriteRevision_NoDuplicateCheck_SuppressesTheWarning()
    {
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063);
        SeedPlan("00065-DeliverTheBlockedCargoFmtPreCommitHook", Title00065, Project, "Draft");
        var app = BuildApp();

        var exit = 0;
        var stderr = CaptureStderr(() => CaptureStdout(() => exit = app.Run(
            ["plan", "write-revision", "00065", "--file", WriteRevisionFile(), "--no-duplicate-check"])));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("DuplicateCandidates", stderr);
    }

    [Fact]
    public void PlanWriteRevision_StdoutIsStillOnlyTheRevisionPath()
    {
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063);
        var ownFolder = SeedPlan("00065-DeliverTheBlockedCargoFmtPreCommitHook", Title00065, Project, "Draft");
        var app = BuildApp();

        string stdout = "";
        CaptureStderr(() => stdout = CaptureStdout(() =>
            app.Run(["plan", "write-revision", "00065", "--file", WriteRevisionFile()])));

        // Agents parse this stdout as a path. The warning goes to stderr and must not pollute it.
        var expected = Path.Combine(ownFolder, "Revisions", "001.md");
        Assert.Equal(expected, stdout);
    }

    private string WriteRevisionFile()
    {
        var file = Path.Combine(_tempDir.Path, $"revision-{Guid.NewGuid():N}.md");
        File.WriteAllText(file, "# Revision body");
        return file;
    }
}
