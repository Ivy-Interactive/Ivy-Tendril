using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Pages;

public class OnboardingPage
{
    private readonly IPage _page;

    public OnboardingPage(IPage page) => _page = page;

    // Step 0: Welcome
    public async Task ClickGetStarted() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Get Started" }).ClickAsync();

    // Step 1: Software Check
    public async Task ClickCheckSoftware() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Check Software" }).ClickAsync();

    public async Task WaitForCheckComplete(int timeoutMs = 60_000) =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Continue" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });

    public async Task ClickContinue() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

    // Step 2: Coding Agent — rendered as radio buttons
    public async Task SelectAgent(string agentName) =>
        await _page.GetByRole(AriaRole.Radio, new() { Name = agentName }).ClickAsync();

    // Step 3: Tendril Home — pre-filled from TENDRIL_HOME env var
    public async Task SetTendrilHome(string path)
    {
        var input = _page.GetByPlaceholder("Select Tendril data folder...");
        await input.ClearAsync();
        await input.FillAsync(path);
    }

    public async Task ClickNext() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

    // Step 4: Project Setup — Project Name has no a11y label
    public async Task SetProjectName(string name)
    {
        // Wait for the Project Setup heading to appear
        await _page.GetByRole(AriaRole.Heading, new() { Name = "Project Setup" }).WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Fill the first single-line textbox (not textarea, not placeholder-textbox)
        // The project name input is the first input[type=text] that isn't the repo path
        var inputs = _page.Locator("input[type='text']:not([placeholder])");
        await inputs.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await inputs.First.FillAsync(name);
    }

    public async Task SetRepoPath(string path)
    {
        var input = _page.GetByPlaceholder("Your repository folder");
        await input.First.ClearAsync();
        await input.First.FillAsync(path);
    }

    // Step 5: Complete
    public async Task ClickCompleteSetup() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Complete Setup" }).ClickAsync();

    public async Task WaitForDashboard(string tendrilHome, int timeoutMs = 60_000)
    {
        // After "Complete Setup", the server runs setup + starts background services,
        // then triggers a full page reload via client.Redirect("/", true).
        // The Ivy framework's redirect may not always work in headless mode,
        // so we also poll the filesystem and force a page reload if needed.

        // First, wait for the server to finish setup by checking the filesystem
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

        // Check if we're still on the onboarding page
        var isOnboarding = await _page.GetByRole(AriaRole.Heading, new() { Name = "Ready to Go!" })
            .IsVisibleAsync();
        if (isOnboarding)
        {
            // Server completed but browser didn't navigate — force a reload
            await _page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        }

        // Now wait for dashboard content
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

    // Full onboarding flow
    public async Task CompleteOnboarding(string agent, string tendrilHome, string projectName, string repoPath)
    {
        // Step 0: Welcome
        await ClickGetStarted();

        // Step 1: Software Check
        await ClickCheckSoftware();
        await WaitForCheckComplete();
        await ClickContinue();

        // Step 2: Coding Agent
        await SelectAgent(agent);
        await ClickContinue();

        // Step 3: Tendril Home (already pre-filled from env var, just click Next)
        await ClickNext();

        // Step 4: Project Setup
        await SetProjectName(projectName);
        await SetRepoPath(repoPath);
        await ClickNext();

        // Step 5: Complete
        await ClickCompleteSetup();
        await WaitForDashboard(tendrilHome);
    }
}
