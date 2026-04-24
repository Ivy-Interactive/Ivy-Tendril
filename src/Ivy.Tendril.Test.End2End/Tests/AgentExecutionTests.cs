using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;
using Ivy.Tendril.Test.End2End.Pages;
using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Tests;

/// <summary>
/// Full lifecycle test: CreatePlan → ExecutePlan → CreatePR.
/// The coding agent is determined by E2E__Agent (default: claude).
/// Run the suite 3 times with E2E__Agent=claude/codex/gemini to test all agents.
/// </summary>
[Collection("E2E")]
public class AgentExecutionTests : IAsyncLifetime
{
    private readonly E2ETestFixture _fixture;
    private IBrowserContext? _context;
    private IPage? _page;

    public AgentExecutionTests(E2ETestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        if (!_fixture.OnboardingCompleted)
        {
            var ctx = await _fixture.Playwright.NewContextAsync();
            var pg = await ctx.NewPageAsync();
            await pg.GotoAsync(_fixture.Tendril.TendrilUrl);

            var onboarding = new OnboardingPage(pg);
            var agentDisplayName = _fixture.Settings.Agent switch
            {
                "claude" => "Claude",
                "codex" => "Codex",
                "gemini" => "Gemini",
                _ => _fixture.Settings.Agent,
            };
            await onboarding.CompleteOnboarding(
                agentDisplayName,
                _fixture.Tendril.TendrilHome,
                "E2ETest",
                _fixture.TestRepo.LocalClonePath);
            _fixture.OnboardingCompleted = true;
            await ctx.CloseAsync();
        }

        _context = await _fixture.Playwright.NewContextAsync();
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
            await _context.CloseAsync();
    }

    [Fact]
    public async Task CreatePlan_Execute_CreatePR()
    {
        var agent = _fixture.Settings.Agent;
        var timeout = _fixture.Settings.PlanExecutionTimeoutSeconds;
        var plansDir = _fixture.Tendril.TendrilPlans;
        var dashboard = new DashboardPage(_page!);
        var plans = new PlansPage(_page!);
        var review = new ReviewPage(_page!);

        // --- Step 1: Create Plan ---
        await _page!.GotoAsync(_fixture.Tendril.TendrilUrl);
        await dashboard.WaitForLoaded();
        await dashboard.NavigateToDrafts();
        await Task.Delay(1000);

        var planDescription = "Uppercase all string literals in Program.cs";
        var step1StartLine = _fixture.Tendril.StdoutLines.Count;
        await plans.CreatePlan(planDescription);

        await WaitForPlanWithYaml("Uppercase", timeout,
            $"Step 1 (CreatePlan) failed: agent={agent}");

        var planFolder = FileSystemAssertions.FindPlanFolder(plansDir, "Uppercase")!;
        var planId = FileSystemAssertions.GetPlanId(planFolder)!;
        FileSystemAssertions.AssertPlanExists(plansDir, "Uppercase");

        // Wait for the CreatePlan job to finish (detect via stdout)
        await WaitForJobExit(timeout, step1StartLine);

        // Verify CreatePlan CLI log
        LogAssertions.AssertCliLogHasEntries(planFolder, "CreatePlan");
        LogAssertions.AssertCliLogContainsCommand(planFolder, "CreatePlan", "job status");
        LogAssertions.AssertCliLogContainsCommand(planFolder, "CreatePlan", "plan create");
        LogAssertions.AssertAllCliCallsSucceeded(planFolder, "CreatePlan");

        // --- Step 2: Execute Plan ---
        await _page!.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await dashboard.WaitForLoaded();
        await dashboard.NavigateToDrafts();

        // Wait for the plan to appear in the sidebar (sync may still be settling)
        await WaitForPlanInSidebar(planId, "Drafts");

        await plans.SelectPlanById(planId);
        await Task.Delay(1000);
        var step2StartLine = _fixture.Tendril.StdoutLines.Count;
        await plans.ClickExecute();

        await WaitForPlanState("Uppercase", "ReadyForReview", timeout,
            $"Step 2 (ExecutePlan) failed: plan #{planId} did not reach ReadyForReview, agent={agent}");

        // Wait for the ExecutePlan job to finish (detect via stdout)
        await WaitForJobExit(timeout, step2StartLine);

        // Verify ExecutePlan CLI log
        LogAssertions.AssertCliLogHasEntries(planFolder, "ExecutePlan");
        LogAssertions.AssertCliLogContainsCommand(planFolder, "ExecutePlan", "job status");
        LogAssertions.AssertCliLogContainsCommand(planFolder, "ExecutePlan", "--plan-id");
        LogAssertions.AssertCliLogContainsCommand(planFolder, "ExecutePlan", "--plan-title");
        LogAssertions.AssertAllCliCallsSucceeded(planFolder, "ExecutePlan");

        // --- Step 3: Create PR ---
        await _page!.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await dashboard.WaitForLoaded();
        await dashboard.NavigateToReview();

        // Wait for the plan to appear in Review sidebar
        await WaitForPlanInSidebar(planId, "Review");

        await review.SelectPlanById(planId);
        await Task.Delay(1000);
        await review.ClickCreatePR();

        await WaitForPRCreated(planFolder, timeout,
            $"Step 3 (CreatePR) failed: no PR URL in plan.yaml for #{planId}, agent={agent}");

        // Verify CreatePr CLI log
        LogAssertions.AssertCliLogHasEntries(planFolder, "CreatePr");
        LogAssertions.AssertCliLogContainsCommand(planFolder, "CreatePr", "plan add-pr");
        LogAssertions.AssertAllCliCallsSucceeded(planFolder, "CreatePr");
    }

    private async Task WaitForPlanWithYaml(string titleFragment, int timeoutSeconds, string context)
    {
        using var watcher = new PlanCreationWatcher(_fixture.Tendril.TendrilPlans, titleFragment);
        try
        {
            await watcher.WaitAsync(
                TimeSpan.FromSeconds(timeoutSeconds),
                _fixture.Tendril.StdoutLines);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{context}\n{ex.Message}", ex);
        }
    }

    private async Task WaitForPlanState(string titleFragment, string expectedState, int timeoutSeconds, string context)
    {
        using var watcher = new PlanStateWatcher(_fixture.Tendril.TendrilPlans, titleFragment, expectedState);
        try
        {
            await watcher.WaitAsync(
                TimeSpan.FromSeconds(timeoutSeconds),
                _fixture.Tendril.StdoutLines);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{context}\n{ex.Message}", ex);
        }
    }

    private async Task WaitForPRCreated(string planFolder, int timeoutSeconds, string context)
    {
        using var watcher = new PlanStateWatcher(
            Path.GetDirectoryName(planFolder)!, Path.GetFileName(planFolder), "Completed");
        try
        {
            // PR creation sets state to Completed and writes the PR URL
            await watcher.WaitAsync(
                TimeSpan.FromSeconds(timeoutSeconds),
                _fixture.Tendril.StdoutLines);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{context}\n{ex.Message}", ex);
        }
    }

    private async Task WaitForJobExit(int timeoutSeconds, int fromLine = -1)
    {
        await StdoutMonitor.WaitForJobExit(
            _fixture.Tendril,
            TimeSpan.FromSeconds(timeoutSeconds),
            fromLine);
    }

    private async Task WaitForPlanInSidebar(string planId, string tabName)
    {
        var dashboard = new DashboardPage(_page!);
        string lastError = "";
        int attempt = 0;

        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                attempt++;
                try
                {
                    await _page!.GotoAsync(_fixture.Tendril.TendrilUrl,
                        new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 15_000 });
                    await dashboard.WaitForLoaded();
                    if (tabName == "Drafts")
                        await dashboard.NavigateToDrafts();
                    else if (tabName == "Review")
                        await dashboard.NavigateToReview();
                    await Task.Delay(3000);

                    var visible = await _page!.GetByText($"#{planId}").First.IsVisibleAsync();
                    return visible;
                }
                catch (Exception ex)
                {
                    lastError = $"attempt {attempt}: {ex.GetType().Name}: {ex.Message}";
                    return false;
                }
            },
            TimeSpan.FromSeconds(120),
            pollInterval: TimeSpan.FromSeconds(5),
            failureMessage: $"Plan #{planId} not visible in {tabName} sidebar after 120s (attempts: {attempt}).\n" +
                $"Last error: {lastError}\n" +
                $"Plans dir: {string.Join(", ", Directory.Exists(_fixture.Tendril.TendrilPlans) ? Directory.GetDirectories(_fixture.Tendril.TendrilPlans).Select(Path.GetFileName) : [])}\n" +
                $"Tendril URL: {_fixture.Tendril.TendrilUrl}");
    }
}
