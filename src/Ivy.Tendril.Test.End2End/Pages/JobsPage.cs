using Ivy.Tendril.Test.End2End.Helpers;
using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Pages;

public class JobsPage
{
    private readonly IPage _page;

    public JobsPage(IPage page) => _page = page;

    public async Task WaitForLoaded(int timeoutMs = 10_000)
    {
        await _page.GetByText("Jobs").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
    }

    public async Task<bool> HasActiveJobs()
    {
        try
        {
            var hasRunning = await _page.Locator("text=/Running|Queued|Pending/")
                .First.IsVisibleAsync();
            return hasRunning;
        }
        catch
        {
            return false;
        }
    }

    public async Task WaitForNoActiveJobs(int timeoutSeconds = 300)
    {
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                await _page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
                await Task.Delay(2000);
                return !await HasActiveJobs();
            },
            TimeSpan.FromSeconds(timeoutSeconds),
            pollInterval: TimeSpan.FromSeconds(5),
            failureMessage: $"Jobs still active after {timeoutSeconds}s");
    }
}
