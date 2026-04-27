using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Pages;

public class DashboardPage
{
    private readonly IPage _page;

    public DashboardPage(IPage page) => _page = page;

    public async Task WaitForLoaded(int timeoutMs = 30_000)
    {
        // Wait for the Ivy SPA to render — initial page load is just an HTML shell,
        // content renders after the SignalR/WebSocket connection establishes.
        // Dashboard may show: stats ("Total Plans"), empty state ("No plans yet"),
        // or loading state ("Loading Dashboard Data...").
        // Also match "Drafts" which appears in sidebar when dashboard is rendered.
        await _page.Locator("text=/Total Plans|No plans yet|Create your first plan|Loading Dashboard Data|Drafts/")
            .First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
    }

    public async Task NavigateToDrafts() =>
        await _page.GetByText("Drafts").First.ClickAsync();

    public async Task NavigateToJobs() =>
        await _page.GetByText("Jobs").First.ClickAsync();

    public async Task NavigateToReview() =>
        await _page.GetByText("Review").First.ClickAsync();

    public async Task<string> GetStatValue(string label)
    {
        var statCard = _page.GetByText(label).Locator("..");
        return await statCard.InnerTextAsync();
    }

    public async Task<int> GetTotalPlans()
    {
        var text = await GetStatValue("Total Plans");
        return int.TryParse(ExtractNumber(text), out var n) ? n : 0;
    }

    private static string ExtractNumber(string text)
    {
        return new string(text.Where(char.IsDigit).ToArray());
    }
}
