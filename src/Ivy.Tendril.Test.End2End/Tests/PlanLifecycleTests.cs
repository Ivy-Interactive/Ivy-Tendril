using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;
using Ivy.Tendril.Test.End2End.Pages;
using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Tests;

[Collection("E2E")]
public class PlanLifecycleTests : IAsyncLifetime
{
    private readonly E2ETestFixture _fixture;
    private IBrowserContext? _context;
    private IPage? _page;

    public PlanLifecycleTests(E2ETestFixture fixture) => _fixture = fixture;

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
    public async Task CreatePlan_ViaUI_CreatesPlanFolder()
    {
        var dashboard = new DashboardPage(_page!);
        var plans = new PlansPage(_page!);
        var timeout = _fixture.Settings.PlanExecutionTimeoutSeconds;

        await _page!.GotoAsync(_fixture.Tendril.TendrilUrl);
        await dashboard.WaitForLoaded();
        await dashboard.NavigateToDrafts();

        await plans.CreatePlan("Add a README.md file with project description");

        await WaitForPlanWithYaml("README", timeout);

        FileSystemAssertions.AssertPlanExists(_fixture.Tendril.TendrilPlans, "README");
    }

    [Fact]
    public async Task PlanList_ShowsCreatedPlans()
    {
        var dashboard = new DashboardPage(_page!);
        var plans = new PlansPage(_page!);
        var timeout = _fixture.Settings.PlanExecutionTimeoutSeconds;

        await _page!.GotoAsync(_fixture.Tendril.TendrilUrl);
        await dashboard.WaitForLoaded();
        await dashboard.NavigateToDrafts();

        await plans.CreatePlan("First test plan for listing");
        await WaitForPlanWithYaml("First", timeout);

        var firstFolder = FileSystemAssertions.FindPlanFolder(_fixture.Tendril.TendrilPlans, "First")!;
        var firstId = FileSystemAssertions.GetPlanId(firstFolder)!;

        await plans.CreatePlan("Second test plan for listing");
        await WaitForPlanWithYaml("Second", timeout);

        var secondFolder = FileSystemAssertions.FindPlanFolder(_fixture.Tendril.TendrilPlans, "Second")!;
        var secondId = FileSystemAssertions.GetPlanId(secondFolder)!;

        // Reload to pick up both plans in the sidebar
        await _page!.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await dashboard.WaitForLoaded();
        await dashboard.NavigateToDrafts();
        await Task.Delay(2000);

        Assert.True(await plans.PlanExistsInList($"#{firstId}"));
        Assert.True(await plans.PlanExistsInList($"#{secondId}"));
    }

    private async Task WaitForPlanWithYaml(string titleFragment, int timeoutSeconds)
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
            pollInterval: TimeSpan.FromSeconds(2),
            failureMessage: $"Plan folder containing '{titleFragment}' with plan.yaml was not created within {timeoutSeconds}s.\n" +
                $"Plans dir contents: {string.Join(", ", Directory.Exists(plansDir) ? Directory.GetFileSystemEntries(plansDir).Select(Path.GetFileName) : [])}\n" +
                $"Tendril stdout (last 20 lines):\n{string.Join("\n", _fixture.Tendril.StdoutLines.TakeLast(20))}");
    }
}
