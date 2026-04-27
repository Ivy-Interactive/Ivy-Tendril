using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;
using Ivy.Tendril.Test.End2End.Pages;
using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Tests;

[Collection("E2E")]
public class OnboardingTests : IAsyncLifetime
{
    private readonly E2ETestFixture _fixture;
    private IBrowserContext? _context;
    private IPage? _page;

    public OnboardingTests(E2ETestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Playwright.NewContextAsync();
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
            await _context.CloseAsync();
    }

    [Fact]
    public async Task Onboarding_CompletesSuccessfully()
    {
        if (!_fixture.OnboardingCompleted)
        {
            var settings = _fixture.Settings;
            var onboarding = new OnboardingPage(_page!);

            await _page!.GotoAsync(_fixture.Tendril.TendrilUrl);

            var agentDisplayName = settings.Agent switch
            {
                "claude" => "Claude",
                "codex" => "Codex",
                "gemini" => "Gemini",
                _ => settings.Agent,
            };

            try
            {
                await onboarding.CompleteOnboarding(
                    agent: agentDisplayName,
                    tendrilHome: _fixture.Tendril.TendrilHome,
                    projectName: "E2ETest",
                    repoPath: _fixture.TestRepo.LocalClonePath);
            }
            catch (TimeoutException ex)
            {
                var stdout = string.Join("\n", _fixture.Tendril.StdoutLines.TakeLast(40));
                var stderr = string.Join("\n", _fixture.Tendril.StderrLines.TakeLast(20));
                throw new TimeoutException(
                    $"{ex.Message}\n\n--- Tendril stdout (last 40 lines) ---\n{stdout}\n\n--- Tendril stderr (last 20 lines) ---\n{stderr}",
                    ex);
            }

            _fixture.OnboardingCompleted = true;
        }

        // Verify filesystem structure
        FileSystemAssertions.AssertOnboardingComplete(_fixture.Tendril.TendrilHome);

        // Verify config.yaml contains the project and agent
        FileSystemAssertions.AssertConfigContains(_fixture.Tendril.TendrilHome, "E2ETest");
        FileSystemAssertions.AssertConfigContains(_fixture.Tendril.TendrilHome, _fixture.Settings.Agent);
    }
}
