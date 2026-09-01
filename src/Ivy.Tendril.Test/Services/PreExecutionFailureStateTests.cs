using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     Covers the pre-execution failure routing added by plan 00103. A plan whose
///     <c>Verification/PreExecution.md</c> reads <c>result: Fail</c> had its premise checked and
///     rejected, so nothing was implemented: it must land in Failed, not Review, from where one click
///     marks it Completed and it reads as a delivered plan for work that never happened (plan 00041).
///     The negative controls matter as much as the guard: an absent report, a Pass, a Skipped, and
///     config-only plans that legitimately have no commits and no PRs must all be left alone.
/// </summary>
public class PreExecutionFailureStateTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-preexec-test");
    private readonly string _repoDir;

    public PreExecutionFailureStateTests()
    {
        _repoDir = Path.Combine(_tempDir.Path, "SomeRepo");
        Directory.CreateDirectory(_repoDir);
    }

    public void Dispose() => _tempDir.Dispose();

    // ---- fixture helpers -----------------------------------------------------------------

    private string CreatePlanFolder(
        string folderName,
        string state,
        (string Name, VerificationStatus Status)[] verifications,
        string[]? commits = null,
        string[]? prs = null)
    {
        var planFolder = Path.Combine(_tempDir.Path, folderName);
        Directory.CreateDirectory(planFolder);

        var plan = new PlanYaml
        {
            State = state,
            Project = "TestProject",
            Title = "Test Plan",
        };
        plan.Repos.Add(_repoDir);
        foreach (var (name, status) in verifications)
            plan.Verifications.Add(new PlanVerificationEntry { Name = name, Status = status });
        if (commits != null) plan.Commits.AddRange(commits);
        if (prs != null) plan.Prs.AddRange(prs);

        PlanCommandHelpers.WritePlan(planFolder, plan);
        return planFolder;
    }

    /// <summary>Writes a PreExecution report in the frontmatter form step 1.7 emits.</summary>
    private static void WritePreExecutionReport(string planFolder, string result)
    {
        var verificationDir = Path.Combine(planFolder, "Verification");
        Directory.CreateDirectory(verificationDir);
        File.WriteAllText(Path.Combine(verificationDir, "PreExecution.md"),
            $"---\nresult: {result}\ndate: 2026-08-01T20:13:49Z\n---\n# PreExecution\n\n**Blocks Found:** 6\n");
    }

    private PlanReaderService CreateReaderService() =>
        new(new TempDirConfigService(_tempDir.Path), NullLogger<PlanReaderService>.Instance);

    /// <summary>
    ///     Drives the real job-completion decision point against a real plan reader and returns the
    ///     state it left on disk.
    /// </summary>
    private async Task<string> RunEnsurePlanStateTransitioned(string planFolder)
    {
        var reader = CreateReaderService();
        var handler = new JobCompletionHandler(
            configService: null,
            logger: NullLogger.Instance,
            modelPricingService: null,
            planReaderService: reader,
            telemetryService: null,
            planWatcherService: null,
            promptsRoot: _tempDir.Path);

        handler.EnsurePlanStateTransitioned(new JobItem
        {
            Id = "00001",
            TypedArgs = new ExecutePlanArgs(planFolder),
        });

        await reader.FlushPendingWritesAsync();
        return PlanCommandHelpers.ReadPlan(planFolder).State;
    }

    // ---- EnsurePlanStateTransitioned -----------------------------------------------------

    [Fact]
    public async Task EnsurePlanStateTransitioned_PreExecutionFail_AllVerificationsSkipped_SetsFailed()
    {
        // Plan 00041's real shape: pre-execution rejected the plan, and the agent then followed the
        // firmware's old instruction to set every verification to Skipped, which made every row look
        // complete. Before this guard that routed to Review.
        var planFolder = CreatePlanFolder("00041-Stale", nameof(PlanStatus.Executing),
        [
            ("DotnetFormat", VerificationStatus.Skipped),
            ("DotnetBuild", VerificationStatus.Skipped),
            ("DotnetTest", VerificationStatus.Skipped),
            ("WidgetsFrontendCheck", VerificationStatus.Skipped),
            ("WidgetsFrontendTest", VerificationStatus.Skipped),
            ("CheckResult", VerificationStatus.Skipped),
        ]);
        WritePreExecutionReport(planFolder, "Fail");

        Assert.Equal(nameof(PlanStatus.Failed), await RunEnsurePlanStateTransitioned(planFolder));
    }

    [Fact]
    public async Task EnsurePlanStateTransitioned_PreExecutionFail_VerificationsPending_SetsFailed()
    {
        // Plan 00043's shape: gates left Pending, which already routed to Failed. Pins that the new
        // condition does not disturb it.
        var planFolder = CreatePlanFolder("00043-Stale", nameof(PlanStatus.Executing),
        [
            ("DotnetBuild", VerificationStatus.Pending),
            ("CheckResult", VerificationStatus.Pending),
        ]);
        WritePreExecutionReport(planFolder, "Fail");

        Assert.Equal(nameof(PlanStatus.Failed), await RunEnsurePlanStateTransitioned(planFolder));
    }

    [Fact]
    public async Task EnsurePlanStateTransitioned_PreExecutionPass_AllVerificationsPass_SetsReview()
    {
        var planFolder = CreatePlanFolder("00044-Clean", nameof(PlanStatus.Executing),
        [
            ("DotnetBuild", VerificationStatus.Pass),
            ("DotnetTest", VerificationStatus.Pass),
        ], commits: ["abc1234"]);
        WritePreExecutionReport(planFolder, "Pass");

        Assert.Equal(nameof(PlanStatus.Review), await RunEnsurePlanStateTransitioned(planFolder));
    }

    [Fact]
    public async Task EnsurePlanStateTransitioned_NoPreExecutionReport_BehavesAsBefore()
    {
        // Step 1.7 skips validation entirely for a revision with no code blocks, so a missing report
        // is normal. It must fall through to the verification-only decision, both ways.
        var passing = CreatePlanFolder("00045-NoReportPassing", nameof(PlanStatus.Executing),
        [
            ("DotnetBuild", VerificationStatus.Pass),
        ], commits: ["abc1234"]);
        var pending = CreatePlanFolder("00046-NoReportPending", nameof(PlanStatus.Executing),
        [
            ("DotnetBuild", VerificationStatus.Pending),
        ]);

        Assert.Equal(nameof(PlanStatus.Review), await RunEnsurePlanStateTransitioned(passing));
        Assert.Equal(nameof(PlanStatus.Failed), await RunEnsurePlanStateTransitioned(pending));
    }

    [Fact]
    public async Task EnsurePlanStateTransitioned_PreExecutionSkipped_DoesNotForceFailed()
    {
        // Only Fail is a positive statement that the premise was checked and rejected.
        var planFolder = CreatePlanFolder("00047-SkippedReport", nameof(PlanStatus.Executing),
        [
            ("DotnetBuild", VerificationStatus.Pass),
        ], commits: ["abc1234"]);
        WritePreExecutionReport(planFolder, "Skipped");

        Assert.Equal(nameof(PlanStatus.Review), await RunEnsurePlanStateTransitioned(planFolder));
    }

    // ---- ReadPreExecutionResult ----------------------------------------------------------

    [Fact]
    public void ReadPreExecutionResult_ParsesFrontmatterAndLegacyResultLine()
    {
        // Frontmatter form, as written by the ExecutePlan firmware today.
        var frontmatter = CreatePlanFolder("00060-Frontmatter", nameof(PlanStatus.Executing), []);
        WritePreExecutionReport(frontmatter, "Fail");

        // Legacy markdown body form, still on disk for older plans.
        var legacy = CreatePlanFolder("00061-Legacy", nameof(PlanStatus.Executing), []);
        var legacyDir = Path.Combine(legacy, "Verification");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "PreExecution.md"),
            "# PreExecution\n\n- **Result:** Fail\n- **Date:** 2026-08-01T20:13:47Z\n");

        var missing = CreatePlanFolder("00062-Missing", nameof(PlanStatus.Executing), []);

        Assert.Equal(VerificationStatus.Fail, PlanYamlHelper.ReadPreExecutionResult(frontmatter));
        Assert.Equal(VerificationStatus.Fail, PlanYamlHelper.ReadPreExecutionResult(legacy));
        Assert.Null(PlanYamlHelper.ReadPreExecutionResult(missing));
        Assert.Null(PlanYamlHelper.ReadPreExecutionResult(""));
    }

    // ---- TransitionState guard -----------------------------------------------------------

    [Fact]
    public void TransitionState_ToCompleted_PreExecutionFail_NoCommitsNoPrs_IsBlocked()
    {
        var planFolder = CreatePlanFolder("00070-Laundered", nameof(PlanStatus.Review),
        [
            ("DotnetBuild", VerificationStatus.Skipped),
        ]);
        WritePreExecutionReport(planFolder, "Fail");
        var service = CreateReaderService();

        var ex = Assert.Throws<PlanTransitionBlockedException>(
            () => service.TransitionState("00070-Laundered", PlanStatus.Completed));

        Assert.Equal("00070-Laundered", ex.FolderName);
        Assert.Equal(PlanStatus.Completed, ex.RequestedState);
        Assert.Contains("PreExecution.md", ex.Message); // points at the diagnosis
        Assert.Contains(nameof(PlanStatus.Skipped), ex.Message); // names the escape hatch
        // The refused transition leaves plan.yaml alone.
        Assert.Equal(nameof(PlanStatus.Review), PlanCommandHelpers.ReadPlan(planFolder).State);
    }

    [Fact]
    public async Task TransitionState_ToCompleted_PreExecutionFail_WithCommits_IsAllowed()
    {
        // A stale or retried report must not block a plan that did real work.
        var planFolder = CreatePlanFolder("00071-DidWork", nameof(PlanStatus.Review),
        [
            ("DotnetBuild", VerificationStatus.Pass),
        ], commits: ["abc1234"]);
        WritePreExecutionReport(planFolder, "Fail");
        var service = CreateReaderService();

        service.TransitionState("00071-DidWork", PlanStatus.Completed);
        await service.FlushPendingWritesAsync();

        Assert.Equal(nameof(PlanStatus.Completed), PlanCommandHelpers.ReadPlan(planFolder).State);
    }

    [Fact]
    public async Task TransitionState_ToCompleted_ConfigOnlyPlan_PreExecutionPass_NoCommitsNoPrs_IsAllowed()
    {
        // The false-positive control, modelled on plans 00048/00050/00053: config-only plans edit
        // verification prompts in config.yaml, touch no repo files, and are correctly commit-free and
        // PR-free with PreExecution Pass. This is the case a bare "Completed with empty commits and
        // empty prs is invalid" rule would have wrongly flagged.
        var planFolder = CreatePlanFolder("00072-ConfigOnly", nameof(PlanStatus.Review),
        [
            ("DotnetBuild", VerificationStatus.Skipped),
            ("CheckResult", VerificationStatus.Pass),
        ]);
        WritePreExecutionReport(planFolder, "Pass");
        var service = CreateReaderService();

        service.TransitionState("00072-ConfigOnly", PlanStatus.Completed);
        await service.FlushPendingWritesAsync();

        Assert.Equal(nameof(PlanStatus.Completed), PlanCommandHelpers.ReadPlan(planFolder).State);
    }

    [Fact]
    public async Task TransitionState_ToSkipped_PreExecutionFail_IsAllowed()
    {
        // The escape hatch the block message points at has to actually work.
        var planFolder = CreatePlanFolder("00073-Retire", nameof(PlanStatus.Review),
        [
            ("DotnetBuild", VerificationStatus.Skipped),
        ]);
        WritePreExecutionReport(planFolder, "Fail");
        var service = CreateReaderService();

        service.TransitionState("00073-Retire", PlanStatus.Skipped);
        await service.FlushPendingWritesAsync();

        Assert.Equal(nameof(PlanStatus.Skipped), PlanCommandHelpers.ReadPlan(planFolder).State);
    }

    // ---- GetCompletionBlockReason & PlanCompletionAction ---------------------------------

    [Fact]
    public void GetCompletionBlockReason_PreExecutionFail_NoCommitsNoPrs_ReturnsBlockReason()
    {
        var planFolder = CreatePlanFolder("00074-Blocked", nameof(PlanStatus.Review),
        [
            ("DotnetBuild", VerificationStatus.Skipped),
        ]);
        WritePreExecutionReport(planFolder, "Fail");
        var service = CreateReaderService();

        var reason = service.GetCompletionBlockReason("00074-Blocked");

        Assert.NotNull(reason);
        Assert.Contains("Pre-execution validation failed", reason);
        Assert.Contains(nameof(PlanStatus.Skipped), reason);
    }

    [Fact]
    public void GetCompletionBlockReason_PreExecutionPass_ReturnsNull()
    {
        var planFolder = CreatePlanFolder("00075-Pass", nameof(PlanStatus.Review),
        [
            ("DotnetBuild", VerificationStatus.Pass),
        ]);
        WritePreExecutionReport(planFolder, "Pass");
        var service = CreateReaderService();

        Assert.Null(service.GetCompletionBlockReason("00075-Pass"));
    }

    [Fact]
    public void GetCompletionBlockReason_PreExecutionFail_WithCommits_ReturnsNull()
    {
        var planFolder = CreatePlanFolder("00076-WithCommits", nameof(PlanStatus.Review),
        [
            ("DotnetBuild", VerificationStatus.Pass),
        ], commits: ["abc1234"]);
        WritePreExecutionReport(planFolder, "Fail");
        var service = CreateReaderService();

        Assert.Null(service.GetCompletionBlockReason("00076-WithCommits"));
    }

    [Fact]
    public async Task PlanCompletionAction_TryComplete_And_Skip_HandlesPreExecutionFailure()
    {
        var planFolder = CreatePlanFolder("00077-ActionTest", nameof(PlanStatus.Review),
        [
            ("DotnetBuild", VerificationStatus.Skipped),
        ]);
        WritePreExecutionReport(planFolder, "Fail");
        var service = CreateReaderService();
        var plan = service.GetPlanByFolder(planFolder)!;

        var failedVerifications = PlanCompletionAction.TryComplete(service, plan);
        Assert.NotNull(failedVerifications);
        Assert.Empty(failedVerifications);

        PlanCompletionAction.Skip(service, plan);
        await service.FlushPendingWritesAsync();

        Assert.Equal(nameof(PlanStatus.Skipped), PlanCommandHelpers.ReadPlan(planFolder).State);
    }

    private class TempDirConfigService(string planFolder) : StubConfigService, IConfigService
    {
        string IConfigService.PlanFolder => planFolder;
    }
}
