using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Pages;

public class OnboardingPage
{
    private readonly IPage _page;

    public OnboardingPage(IPage page) => _page = page;

    // Step 0: Coding Agent — rendered as clickable cards
    public async Task SelectAgentCard(string agentName)
    {
        // The agent picker renders cards with the agent label as text
        var card = _page.Locator($"text='{agentName}'").First;
        await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await card.ClickAsync();
    }

    public async Task WaitForAgentSetupComplete(int timeoutMs = 120_000)
    {
        // After clicking an agent card, the step auto-advances to step 1 (Data Storage)
        // once software checks + auth complete. Wait for the "Data Storage" heading or "Next" button.
        await _page.GetByRole(AriaRole.Heading, new() { Name = "Where should we store your data?" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
    }

    // Step 1: Data Storage (Tendril Home) — pre-filled from TENDRIL_HOME env var
    public async Task SetTendrilHome(string path)
    {
        var input = _page.GetByRole(AriaRole.Textbox);
        await input.First.ClearAsync();
        await input.First.FillAsync(path);
    }

    public async Task ClickNext() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

    public async Task ClickBack() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();

    // Step 2: Your First Project — skip for fast onboarding
    public async Task ClickSkip() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Skip" }).ClickAsync();

    // Step 3: Complete
    public async Task ClickFinish() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Finish" }).ClickAsync();

    public async Task WaitForDashboard(string tendrilHome, int timeoutMs = 60_000)
    {
        // After "Finish", the server finalizes onboarding and triggers a page reload.
        // Wait for the config file to exist, then check if the dashboard appears.

        var configPath = Path.Combine(tendrilHome, "config.yaml");
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(configPath))
            {
                var content = await File.ReadAllTextAsync(configPath);
                if (!content.StartsWith("#"))
                    break;
            }
            await Task.Delay(500);
        }

        // Give the server a moment to finish the redirect
        await Task.Delay(2000);

        // Check if we're still on the complete step
        var isComplete = await _page.GetByRole(AriaRole.Heading, new() { Name = "Ready to Go!" })
            .IsVisibleAsync();
        if (isComplete)
        {
            await _page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        }

        // Wait for dashboard content
        try
        {
            await _page.Locator("text=/Loading Dashboard Data|No plans yet|Create your first plan|Dashboard/")
                .First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            var screenshotPath = Path.Combine(Path.GetTempPath(), "tendril-e2e-complete-timeout.png");
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
            throw new TimeoutException(
                $"Dashboard did not appear after setup completed. Screenshot: {screenshotPath}");
        }
    }

    // Full onboarding flow (skips project setup for speed)
    public async Task CompleteOnboarding(string agent, string tendrilHome, string projectName, string repoPath)
    {
        // Step 0: Coding Agent — click the agent card
        await SelectAgentCard(agent);
        await WaitForAgentSetupComplete();

        // Step 1: Data Storage (Tendril Home is pre-filled from env var, just click Next)
        await ClickNext();

        // Step 2: Your First Project — skip to get to Complete faster
        await _page.GetByRole(AriaRole.Heading, new() { Name = "Setup your first project" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await ClickSkip();

        // Step 3: Complete
        await _page.GetByRole(AriaRole.Heading, new() { Name = "Ready to Go!" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await ClickFinish();
        await WaitForDashboard(tendrilHome);
    }
}
