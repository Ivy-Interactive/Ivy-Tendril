using System;
using System.IO;
using System.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
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
///     of being stranded in Drafts, while ignoring PR URLs that don't belong to this plan. Issue #2336
///     is the second half: a URL the agent merely cited in the PR body it was writing got recorded as
///     the plan's own PR, 12 times over, because the scan read every eventwire entry as if it were
///     command output.
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

    private void WritePlan(
        string state,
        string[]? prs = null,
        bool withRepo = true,
        string[]? commits = null,
        string[]? extraRepos = null)
    {
        var plan = new PlanYaml
        {
            State = state,
            Project = "lots-of-dev-tools",
            Title = "Add JWT Tester Tool",
        };
        if (withRepo) plan.Repos.Add(_repoDir);
        if (extraRepos != null)
            foreach (var repo in extraRepos)
            {
                var path = Path.Combine(_tempDir.Path, repo);
                Directory.CreateDirectory(path);
                plan.Repos.Add(path);
            }
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

    /// <summary>
    ///     A job whose OutputLines hold real serialized eventwire entries, which is the only thing
    ///     production ever puts there: <c>JobItem.EnqueueOutput</c> serializes every parsed event, and
    ///     even <c>EnqueueSystemOutput</c> wraps its message in a TextEvent. Every case below goes
    ///     through here, because a fixture of plain prose lines cannot tell a passing filter from an
    ///     absent one.
    /// </summary>
    private JobItem JobWithEvents(params AgentEvent[] events)
    {
        var serializer = new JsonEventSerializer();
        return JobWithOutput(events.Select(serializer.Serialize).ToArray());
    }

    private static ToolResultEvent ToolResult(string output) => new()
    {
        Kind = AgentEventKind.ToolResult,
        ToolUseId = "toolu_01",
        ToolName = "Bash",
        Output = output,
    };

    private static ToolCallEvent ToolCall(string command) => new()
    {
        Kind = AgentEventKind.ToolCall,
        ToolUseId = "toolu_01",
        ToolName = "Bash",
        Description = "Prepare PR body in temp file",
        InputJson = $"{{\"command\":{System.Text.Json.JsonSerializer.Serialize(command)}}}",
    };

    [Fact]
    public void RecordsMissingPr_AndSetsCompleted_WhenAgentSkippedCloseout()
    {
        WritePlan(nameof(PlanStatus.Review)); // agent left it in Review with no PR recorded
        var job = JobWithEvents(ToolResult($"{PrUrl}#issuecomment-4851921040"));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Equal(new[] { PrUrl }, plan.Prs); // trailing #issuecomment stripped, base URL recorded
    }

    [Fact]
    public void SetsCompleted_EvenWhenPlanWasStuckInDraft()
    {
        WritePlan(nameof(PlanStatus.Draft));
        var job = JobWithEvents(ToolResult(PrUrl));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Contains(PrUrl, plan.Prs);
    }

    [Fact]
    public void RecordsPr_WhenBareUrlOnItsOwnLineInToolResult()
    {
        // The exact shape of job 00467's successful `gh pr create`: a progress line, then the URL
        // alone on the next one.
        WritePlan(nameof(PlanStatus.Review));
        var job = JobWithEvents(ToolResult($"Attempt 1: Creating PR...\n{PrUrl}\n"));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Equal(new[] { PrUrl }, plan.Prs);
    }

    [Fact]
    public void IgnoresPrUrl_CitedInsideAToolCallInput()
    {
        // Issue #2336 itself. The agent cites a sibling plan's PR in the body it is composing, so the
        // URL reaches OutputLines as part of a tool_call input, never as command output.
        const string ownPr = "https://github.com/nielsbosma/lots-of-dev-tools/pull/19";
        WritePlan(nameof(PlanStatus.Review), prs: new[] { ownPr });
        var job = JobWithEvents(ToolCall(
            $"cat > /tmp/body.md <<'EOF'\nBuilds on [Plan 00061]({PrUrl}).\nEOF"));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(new[] { ownPr }, plan.Prs);
    }

    [Fact]
    public void IgnoresPrUrl_CitedInProseInsideAToolResult()
    {
        // 9 of the 12 corruptions in issue #2336 had the citation echoed back by a tool result (the
        // heredoc write, `cat` of the body file, `gh pr view` of a sibling), so a tool_result-only
        // filter is not enough on its own: the URL still has to own its line.
        const string ownPr = "https://github.com/nielsbosma/lots-of-dev-tools/pull/19";
        WritePlan(nameof(PlanStatus.Review), prs: new[] { ownPr });
        var job = JobWithEvents(ToolResult($"Related work: [Plan 00061]({PrUrl}) landed first."));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(new[] { ownPr }, plan.Prs);
    }

    [Fact]
    public void IgnoresSecondPrForARepoThatAlreadyHasOne()
    {
        // The per-repo guard, with a bare URL in a tool result: this one gets past the line filter, so
        // it fails if the guard is missing.
        const string ownPr = "https://github.com/nielsbosma/lots-of-dev-tools/pull/19";
        WritePlan(nameof(PlanStatus.Review), prs: new[] { ownPr });
        var job = JobWithEvents(ToolResult(PrUrl));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(new[] { ownPr }, plan.Prs);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State); // the plan has a PR, so still completed
    }

    [Fact]
    public void StillRecords_ForASecondRepoOnAMultiRepoPlan()
    {
        // The guard is per repo, not per plan: a two-repo plan whose agent recorded only the first
        // PR is exactly the case the safety net exists for.
        const string firstRepoPr = "https://github.com/nielsbosma/lots-of-dev-tools/pull/19";
        const string secondRepoPr = "https://github.com/nielsbosma/other-tools/pull/4";
        WritePlan(nameof(PlanStatus.Review), prs: new[] { firstRepoPr }, extraRepos: new[] { "other-tools" });
        var job = JobWithEvents(ToolResult(secondRepoPr));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(new[] { firstRepoPr, secondRepoPr }, plan.Prs);
    }

    [Fact]
    public void CompletesWithoutDuplicating_WhenAgentRecordedPrButNotState()
    {
        // Agent recorded the PR with a /files suffix but left the plan in Review. The suffixed form
        // still marks the repo as having a PR, so the bare URL in the output is not re-added.
        WritePlan(nameof(PlanStatus.Review), prs: new[] { PrUrl + "/files" });
        var job = JobWithEvents(ToolResult(PrUrl));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Single(plan.Prs);
    }

    [Fact]
    public void RecordsPrOnce_WhenTheSameBareUrlIsPrintedTwice()
    {
        // Keeps the canonical owner/repo#number dedup honest: with no PR recorded the per-repo guard
        // cannot fire, so the second occurrence is only rejected by the dedup.
        WritePlan(nameof(PlanStatus.Review));
        var job = JobWithEvents(
            ToolResult(PrUrl),
            ToolResult($"{PrUrl}/"));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(new[] { PrUrl }, plan.Prs);
    }

    [Fact]
    public void NoOp_WhenAlreadyRecordedAndCompleted()
    {
        WritePlan(nameof(PlanStatus.Completed), prs: new[] { PrUrl });
        var job = JobWithEvents(ToolResult(PrUrl));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Completed), plan.State);
        Assert.Single(plan.Prs);
    }

    [Fact]
    public void LeavesStateUnchanged_WhenNoPrInOutput()
    {
        WritePlan(nameof(PlanStatus.Review)); // e.g. aborted run
        var job = JobWithEvents(ToolResult("no pull request was created"));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Equal(nameof(PlanStatus.Review), plan.State);
        Assert.Empty(plan.Prs);
    }

    [Fact]
    public void IgnoresForeignRepoPr_NotBelongingToThisPlan()
    {
        WritePlan(nameof(PlanStatus.Review)); // repo is lots-of-dev-tools
        // A PR URL for a different repo, printed exactly the way `gh pr create` prints one, so the
        // repo filter is the only thing that can reject it.
        var job = JobWithEvents(ToolResult("https://github.com/nielsbosma/some-other-repo/pull/99"));

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
        var job = JobWithEvents(ToolResult(PrUrl));

        CreateHandler().ReconcileCreatePrResult(job);

        var plan = PlanCommandHelpers.ReadPlan(_planFolder);
        Assert.Empty(plan.Prs);
    }
}
