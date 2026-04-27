using Ivy.Tendril.Test.End2End.Configuration;
using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Fixtures;

public class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser
        ?? throw new InvalidOperationException("Playwright not initialized");

    public async Task InitializeAsync()
    {
        var settings = TestSettingsProvider.Get();

        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"Playwright browser install failed with exit code {exitCode}");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = settings.Headless,
            SlowMo = settings.SlowMo,
        });
    }

    public async Task<IBrowserContext> NewContextAsync()
    {
        return await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser != null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}
