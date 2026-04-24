using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;
using Ivy.Tendril.Test.End2End.Pages;
using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Tests;

[Collection("E2E")]
public class VerificationTests : IAsyncLifetime
{
    private readonly E2ETestFixture _fixture;
    private IBrowserContext? _context;
    private IPage? _page;

    public VerificationTests(E2ETestFixture fixture) => _fixture = fixture;

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
    public void OnboardingCreatesValidDirectoryStructure()
    {
        FileSystemAssertions.AssertOnboardingComplete(_fixture.Tendril.TendrilHome);

        var counterPath = Path.Combine(_fixture.Tendril.TendrilPlans, ".counter");
        Assert.True(File.Exists(counterPath), ".counter file missing in Plans/");
        var counterValue = File.ReadAllText(counterPath).Trim();
        Assert.True(int.TryParse(counterValue, out var n) && n >= 1,
            $".counter should be >= 1, got '{counterValue}'");
    }

    [Fact]
    public void ConfigYaml_ContainsExpectedSettings()
    {
        var configPath = Path.Combine(_fixture.Tendril.TendrilHome, "config.yaml");
        Assert.True(File.Exists(configPath));

        var content = File.ReadAllText(configPath);
        Assert.Contains("codingAgent:", content);
        Assert.Contains("projects:", content);
        FileSystemAssertions.AssertConfigContains(_fixture.Tendril.TendrilHome, _fixture.Settings.Agent);
    }

    [Fact]
    public void Database_ExistsAfterOnboarding()
    {
        var dbPath = Path.Combine(_fixture.Tendril.TendrilHome, "tendril.db");
        Assert.True(File.Exists(dbPath), "tendril.db should exist after onboarding");
        Assert.True(new FileInfo(dbPath).Length > 0, "tendril.db should not be empty");
    }

    [Fact]
    public async Task DashboardLoadsAfterOnboarding()
    {
        var dashboard = new DashboardPage(_page!);

        await _page!.GotoAsync(_fixture.Tendril.TendrilUrl);
        await dashboard.WaitForLoaded();
    }
}
