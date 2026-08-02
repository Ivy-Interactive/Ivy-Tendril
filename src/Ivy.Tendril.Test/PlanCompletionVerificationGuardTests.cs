using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Infrastructure;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console.Cli;
using Spectre.Console.Testing;

namespace Ivy.Tendril.Test;

/// <summary>
///     Plan 00090: a plan may not reach Completed while one of its verifications is in the Fail state.
///     The fixtures use plan 00042's real verification list, because 00042 is the incident these tests
///     exist for: it reached Completed with a merged PR while its own CheckResult said the deliverable
///     was missing, and four later plans re-derived the same work because it read as done.
/// </summary>
[Collection("TendrilHome")]
public class PlanCompletionVerificationGuardTests : IDisposable
{
    private const string FolderName = "00042-RemoveDeadDotnetFormatStagedGlob";

    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _plansDir;
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;

    public PlanCompletionVerificationGuardTests()
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

    // Plan 00042's verifications exactly as recorded: four Skipped, three Pass, and the CheckResult
    // that rejected the work. Keeping the Skipped entries matters, since Skipped must not block.
    private static List<PlanVerificationEntry> Plan00042Verifications(
        VerificationStatus checkResult = VerificationStatus.Fail) =>
    [
        new() { Name = "RustFmt", Status = VerificationStatus.Skipped },
        new() { Name = "RustClippy", Status = VerificationStatus.Skipped },
        new() { Name = "RustyFrontendLint", Status = VerificationStatus.Pass },
        new() { Name = "RustBuild", Status = VerificationStatus.Skipped },
        new() { Name = "RustyFrontendBuild", Status = VerificationStatus.Pass },
        new() { Name = "RustTest", Status = VerificationStatus.Skipped },
        new() { Name = "RustyFrontendTest", Status = VerificationStatus.Pass },
        new() { Name = "CheckResult", Status = checkResult }
    ];

    private string SeedPlan(List<PlanVerificationEntry> verifications, string state = "Review")
    {
        var planFolder = Path.Combine(_plansDir, FolderName);
        Directory.CreateDirectory(planFolder);

        var plan = new PlanYaml
        {
            State = state,
            Project = "Rusty",
            Title = "Remove Dead Dotnet Format Staged Glob",
            Repos = [_tempDir.Path],
            Prs = ["https://github.com/org/repo/pull/31"],
            Verifications = verifications,
            Created = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), YamlHelper.Serializer.Serialize(plan));
        return planFolder;
    }

    private PlanYaml ReadSeededPlan() =>
        PlanCommandHelpers.ReadPlan(Path.Combine(_plansDir, FolderName));

    private PlanReaderService CreateReaderService() =>
        new(
            new TestPlanConfigService(_tempDir.Path, "Rusty", tendrilHome: _tempDir.Path),
            new NullLogger<PlanReaderService>());

    // Builds a real `plan set` app the way Program.cs registers it, minus PropagateExceptions so that
    // a blocked transition surfaces as the non-zero exit code the CLI actually returns rather than as
    // an exception. The TestConsole keeps Spectre's error rendering out of the test output.
    private static CommandApp BuildPlanSetApp(out TestConsole console)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPlanWatcherService, NullPlanWatcherService>();

        var testConsole = new TestConsole();
        console = testConsole;

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(config =>
        {
            config.Settings.Console = testConsole;
            config.AddBranch("plan", plan => plan.AddCommand<PlanSetCommand>("set"));
        });
        return app;
    }

    // 1. The incident itself: 00042's own verification list must not be allowed to reach Completed.
    [Fact]
    public async Task TransitionState_ToCompleted_WithFailedVerification_Throws()
    {
        SeedPlan(Plan00042Verifications());
        var service = CreateReaderService();

        var ex = Assert.Throws<PlanTransitionBlockedException>(
            () => service.TransitionState(FolderName, PlanStatus.Completed));

        Assert.Equal(["CheckResult"], ex.FailedVerifications);
        Assert.Contains("--allow-failed-verifications", ex.Message);

        // The block must happen before anything is written, not after.
        await service.FlushPendingWritesAsync();
        var onDisk = ReadSeededPlan();
        Assert.Equal("Review", onDisk.State);
        Assert.False(onDisk.PartialDelivery);
    }

    // 2. Negative control, including Skipped: only Fail blocks.
    [Fact]
    public async Task TransitionState_ToCompleted_WithAllPassOrSkipped_Succeeds()
    {
        SeedPlan(Plan00042Verifications(VerificationStatus.Pass));
        var service = CreateReaderService();

        service.TransitionState(FolderName, PlanStatus.Completed);
        await service.FlushPendingWritesAsync();

        var onDisk = ReadSeededPlan();
        Assert.Equal("Completed", onDisk.State);
        Assert.False(onDisk.PartialDelivery);
    }

    // 3. Pending must not block here: JobCompletionHandler.EnsurePlanStateTransitioned already routes
    //    a plan with Pending gates to Failed, so blocking again would just break manual completion.
    [Fact]
    public async Task TransitionState_ToCompleted_WithPendingOnly_Succeeds()
    {
        SeedPlan(Plan00042Verifications(VerificationStatus.Pending));
        var service = CreateReaderService();

        service.TransitionState(FolderName, PlanStatus.Completed);
        await service.FlushPendingWritesAsync();

        Assert.Equal("Completed", ReadSeededPlan().State);
    }

    // 4. Only Completed is guarded: every other transition stays available for a failed plan.
    [Theory]
    [InlineData(PlanStatus.Failed)]
    [InlineData(PlanStatus.Review)]
    [InlineData(PlanStatus.Draft)]
    [InlineData(PlanStatus.Skipped)]
    public async Task TransitionState_ToNonCompletedState_WithFailedVerification_Succeeds(PlanStatus target)
    {
        SeedPlan(Plan00042Verifications(), state: "Executing");
        var service = CreateReaderService();

        service.TransitionState(FolderName, target);
        await service.FlushPendingWritesAsync();

        Assert.Equal(target.ToString(), ReadSeededPlan().State);
    }

    // 5. The path 00042 actually took: `tendril plan set 00042 state Completed`.
    [Fact]
    public void PlanSet_StateCompleted_WithFailedVerification_ReturnsNonZero()
    {
        SeedPlan(Plan00042Verifications());

        var exit = BuildPlanSetApp(out var console).Run(["plan", "set", "00042", "state", "Completed"]);

        Assert.NotEqual(0, exit);
        Assert.Contains("CheckResult", console.Output);
        var onDisk = ReadSeededPlan();
        Assert.Equal("Review", onDisk.State);
        Assert.False(onDisk.PartialDelivery);
    }

    // 6. The escape hatch records the partial delivery rather than bypassing the gate silently.
    [Fact]
    public void PlanSet_StateCompleted_WithAllowFailedVerifications_SucceedsAndSetsPartialDelivery()
    {
        SeedPlan(Plan00042Verifications());

        var exit = BuildPlanSetApp(out _).Run(
            ["plan", "set", "00042", "state", "Completed", "--allow-failed-verifications"]);

        Assert.Equal(0, exit);
        var onDisk = ReadSeededPlan();
        Assert.Equal("Completed", onDisk.State);
        Assert.True(onDisk.PartialDelivery);
    }

    // 7. partialDelivery is additive: absent means false, and a plan that never set it must not gain
    //    the field on the next write. That is what keeps this off the schema-migration path.
    [Fact]
    public void PlanYaml_PartialDeliveryAbsent_DeserializesFalse_AndRoundTripsWithoutEmittingField()
    {
        var legacyYaml = """
                         state: Completed
                         project: Rusty
                         title: Legacy Plan
                         repos:
                         - /dummy/repo
                         commits: []
                         prs: []
                         verifications: []
                         """;

        var parsed = YamlHelper.Deserializer.Deserialize<PlanYaml>(legacyYaml);

        Assert.False(parsed.PartialDelivery);

        var reserialized = YamlHelper.Serializer.Serialize(parsed);
        Assert.DoesNotContain("partialDelivery", reserialized);

        parsed.PartialDelivery = true;
        Assert.Contains("partialDelivery: true", YamlHelper.Serializer.Serialize(parsed));
    }
}
