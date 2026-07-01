using System;
using System.IO;
using System.Linq;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     Covers the CreatePr safety net in <see cref="JobCompletionHandler.ReconcileCreatePrResult" />:
///     when the agent creates a PR but skips the Program.md step-6 closeout, Tendril must still
///     record the PR URL and mark the plan Completed so it surfaces in the Pull Requests app instead
///     of being stranded in Drafts — while ignoring PR URLs that don't belong to this plan's repos.
/// </summary>
public class JobCompletionCreatePrTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _planFolder;
    private readonly string _repoDir;
    private const string PrUrl = "https://github.com/nielsbosma/lots-of-dev-tools/pull/26";

    public JobCompletionCreatePrTests()
    {
        _planFolder = Path.Combine(_tempDir.Path, "00015-AddJWTTesterTool");
        // Folder name must equal the PR URL's repo segment for the safety net to trust the URL.
        _repoDir = Path.Combine(_tempDir.Path, "lots-of-dev-tools");
        Directory.CreateDirectory(_planFolder);
        Directory.CreateDirectory(_repoDir);
    }

    public void Dispose() => _tempDir.Dispose();

    private JobCompletionHandler CreateHandler() => new(
        configService: null,
        logger: NullLogger.Instance,
        modelPricingService: null,
        planReaderService: null,
        telemetryService: null,
        planWatcherService: null,
        promptsRoot: _tempDir.Path);

    private void WritePlan(string state, string[]? prs = null, bool withRepo = true, string[]? commits = null)
    {
        var plan = new PlanYaml
        {
            State = state,
            Project = "lots-of-dev-tools",
            Title = "Add JWT Tester Tool",
        };
        if (withRepo) plan.Repos.Add(_repoDir);
        if (prs != null) plan.Prs.AddRange(prs);
        if (commits != null) plan.Commits.AddRange(commits);
        PlanCommandHelpers.WritePlan(_planFolder, plan);
    }

    private JobItem JobWithOutput(params string[] outputLines)
    {
        var job = new JobItem
        {
            Id = "00145",
            TypedArgs = new CreatePrArgs(_planFolder, Merge: false),
        };
        foreach (var line in outputLines)
            job.OutputLines.Enqueue(line);
        return job;
    }

    [Fact]
    public void RecordsMissingPr_AndSetsCompleted_WhenAgentSkippedCloseout()
    {
        WritePlan(nameof(PlanStatus.Review)); // agent left it in Review with no PR recorded
        var job = JobWithOutput($"Created PR {PrUrl}#issuecomment-4851921040");

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Equal(new[] { PrUrl }, plan.Prs); // trailing #issuecomment stripped, base URL recorded
    }

    [Fact]
    public void SetsCompleted_EvenWhenPlanWasStuckInDraft()
    {
        WritePlan(nameof(PlanStatus.Draft));
        var job = JobWithOutput($"opened {PrUrl}");

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Contains(PrUrl, plan.Prs);
    }

    [Fact]
    public void CompletesWithoutDuplicating_WhenAgentRecordedPrButNotState()
    {
        // Agent recorded the PR with a /files suffix but left the plan in Review.
        WritePlan(nameof(PlanStatus.Review), prs: new[] { PrUrl + "/files" });
        var job = JobWithOutput($"existing {PrUrl}");

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Single(plan.Prs); // canonical dedup: same PR #26 not re-added despite different form
    }

    [Fact]
    public void NoOp_WhenAlreadyRecordedAndCompleted()
    {
        WritePlan(nameof(PlanStatus.Completed), prs: new[] { PrUrl });
        var job = JobWithOutput($"existing {PrUrl}");

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Single(plan.Prs);
    }

    [Fact]
    public void LeavesStateUnchanged_WhenNoPrInOutput()
    {
        WritePlan(nameof(PlanStatus.Review)); // e.g. aborted run
        var job = JobWithOutput("no pull request was created");

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Review), plan.State);
        Assert.Empty(plan.Prs);
    }

    [Fact]
    public void IgnoresForeignRepoPr_NotBelongingToThisPlan()
    {
        WritePlan(nameof(PlanStatus.Review)); // repo is lots-of-dev-tools
        // A PR URL for a different repo merely echoed in the transcript (e.g. plan.SourceUrl or a
        // referenced PR) must not be recorded or force-complete the plan.
        var job = JobWithOutput("see https://github.com/nielsbosma/some-other-repo/pull/99");

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Review), plan.State);
        Assert.Empty(plan.Prs);
    }

    [Fact]
    public void DoesNotScavengePrUrls_WhenPlanHasNoRepos()
    {
        // Direct-to-main plans have no repos and open no PR; a referenced PR URL must be ignored.
        WritePlan(nameof(PlanStatus.Completed), withRepo: false, commits: new[] { "abc1234" });
        var job = JobWithOutput($"related {PrUrl}");

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Empty(plan.Prs);
    }
}
