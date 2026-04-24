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

        await plans.CreatePlan("Uppercase all string literals in Program.cs");

        await WaitForPlanWithYaml("Uppercase", timeout);

        FileSystemAssertions.AssertPlanExists(_fixture.Tendril.TendrilPlans, "Uppercase");
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

        await plans.CreatePlan("Uppercase all string literals in GlobalUsings.cs");
        await WaitForPlanWithYaml("GlobalUsings", timeout);

        var firstFolder = FileSystemAssertions.FindPlanFolder(_fixture.Tendril.TendrilPlans, "GlobalUsings")!;
        var firstId = FileSystemAssertions.GetPlanId(firstFolder)!;

        await plans.CreatePlan("Add XML documentation comments to Program.cs");
        await WaitForPlanWithYaml("Documentation", timeout);

        var secondFolder = FileSystemAssertions.FindPlanFolder(_fixture.Tendril.TendrilPlans, "Documentation")!;
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
        using var watcher = new PlanCreationWatcher(_fixture.Tendril.TendrilPlans, titleFragment);
        await watcher.WaitAsync(
            TimeSpan.FromSeconds(timeoutSeconds),
            _fixture.Tendril.StdoutLines);
    }
}
