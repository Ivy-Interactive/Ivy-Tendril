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

        var planDescription = $"Create a file called {agent}-lifecycle.txt containing 'Hello from {agent}'";
        await plans.CreatePlan(planDescription);

        await WaitForPlanWithYaml($"{agent}-lifecycle", timeout,
            $"Step 1 (CreatePlan) failed: agent={agent}");

        var planFolder = FileSystemAssertions.FindPlanFolder(plansDir, $"{agent}-lifecycle")!;
        var planId = FileSystemAssertions.GetPlanId(planFolder)!;
        FileSystemAssertions.AssertPlanExists(plansDir, $"{agent}-lifecycle");

        // Wait for the CreatePlan job to fully complete (Jobs badge → 0)
        await WaitForNoActiveJobs(timeout);

        // --- Step 2: Execute Plan ---
        await _page!.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await dashboard.WaitForLoaded();
        await dashboard.NavigateToDrafts();

        // Wait for the plan to appear in the sidebar (sync may still be settling)
        await WaitForPlanInSidebar(planId, "Drafts");

        await plans.SelectPlanById(planId);
        await Task.Delay(1000);
        await plans.ClickExecute();

        await WaitForPlanState($"{agent}-lifecycle", "ReadyForReview", timeout,
            $"Step 2 (ExecutePlan) failed: plan #{planId} did not reach ReadyForReview, agent={agent}");

        // Wait for the ExecutePlan job to complete
        await WaitForNoActiveJobs(timeout);

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
    }

    private async Task WaitForPlanWithYaml(string titleFragment, int timeoutSeconds, string context)
    {
        var plansDir = _fixture.Tendril.TendrilPlans;

        await RetryHelper.WaitUntilAsync(
            () =>
            {
                var folder = FileSystemAssertions.FindPlanFolder(plansDir, titleFragment);
                if (folder == null) return Task.FromResult(false);
                return Task.FromResult(File.Exists(Path.Combine(folder, "plan.yaml")));
            },
            TimeSpan.FromSeconds(timeoutSeconds),
            pollInterval: TimeSpan.FromSeconds(3),
            failureMessage: $"{context}\n" +
                $"Plan '{titleFragment}' with plan.yaml not found within {timeoutSeconds}s.\n" +
                $"Plans dir: {string.Join(", ", Directory.Exists(plansDir) ? Directory.GetFileSystemEntries(plansDir).Select(Path.GetFileName) : [])}\n" +
                $"Tendril stdout (last 30):\n{string.Join("\n", _fixture.Tendril.StdoutLines.TakeLast(30))}");
    }

    private async Task WaitForPlanState(string titleFragment, string expectedState, int timeoutSeconds, string context)
    {
        var plansDir = _fixture.Tendril.TendrilPlans;

        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                var folder = FileSystemAssertions.FindPlanFolder(plansDir, titleFragment);
                if (folder == null) return false;

                var yamlPath = Path.Combine(folder, "plan.yaml");
                if (!File.Exists(yamlPath)) return false;

                var content = await File.ReadAllTextAsync(yamlPath);
                return content.Contains($"state: {expectedState}", StringComparison.OrdinalIgnoreCase);
            },
            TimeSpan.FromSeconds(timeoutSeconds),
            pollInterval: TimeSpan.FromSeconds(3),
            failureMessage: $"{context}\n" +
                $"Tendril stdout (last 30):\n{string.Join("\n", _fixture.Tendril.StdoutLines.TakeLast(30))}");
    }

    private async Task WaitForPRCreated(string planFolder, int timeoutSeconds, string context)
    {
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                var yamlPath = Path.Combine(planFolder, "plan.yaml");
                if (!File.Exists(yamlPath)) return false;

                var content = await File.ReadAllTextAsync(yamlPath);
                return content.Contains("github.com", StringComparison.OrdinalIgnoreCase) &&
                       content.Contains("pull", StringComparison.OrdinalIgnoreCase);
            },
            TimeSpan.FromSeconds(timeoutSeconds),
            pollInterval: TimeSpan.FromSeconds(3),
            failureMessage: $"{context}\n" +
                $"Tendril stdout (last 30):\n{string.Join("\n", _fixture.Tendril.StdoutLines.TakeLast(30))}");
    }

    private async Task WaitForNoActiveJobs(int timeoutSeconds)
    {
        var dashboard = new DashboardPage(_page!);

        await _page!.GotoAsync(_fixture.Tendril.TendrilUrl);
        await dashboard.WaitForLoaded();
        await dashboard.NavigateToJobs();
        await Task.Delay(2000);

        var jobs = new JobsPage(_page!);
        await jobs.WaitForNoActiveJobs(timeoutSeconds);
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
                    await Task.Delay(5000);

                    // Take a screenshot on first and last attempts for debugging
                    if (attempt == 1 || attempt % 5 == 0)
                    {
                        var screenshotPath = Path.Combine(Path.GetTempPath(),
                            $"tendril-e2e-sidebar-{tabName}-attempt{attempt}.png");
                        await _page!.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
                    }

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
            pollInterval: TimeSpan.FromSeconds(10),
            failureMessage: $"Plan #{planId} not visible in {tabName} sidebar after 120s (attempts: {attempt}).\n" +
                $"Last error: {lastError}\n" +
                $"Plans dir: {string.Join(", ", Directory.Exists(_fixture.Tendril.TendrilPlans) ? Directory.GetDirectories(_fixture.Tendril.TendrilPlans).Select(Path.GetFileName) : [])}\n" +
                $"Tendril URL: {_fixture.Tendril.TendrilUrl}");
    }
}
