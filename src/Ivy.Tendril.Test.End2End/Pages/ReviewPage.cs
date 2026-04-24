using Ivy.Tendril.Test.End2End.Helpers;
using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Pages;

public class ReviewPage
{
    private readonly IPage _page;

    public ReviewPage(IPage page) => _page = page;

    public async Task WaitForLoaded(int timeoutMs = 10_000) =>
        await _page.Locator("text=/Ready for Review|No plans|review/i")
            .First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });

    public async Task SelectPlanById(string planId)
    {
        var locator = _page.GetByText($"#{planId}").First;
        await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await locator.ClickAsync();
    }

    public async Task ClickCreatePR()
    {
        await _page.GetByRole(AriaRole.Button, new() { Name = "Create PR" }).First.ClickAsync();

        // With PrRule "default", a Custom PR dialog opens. Click the dialog's Create PR to confirm.
        try
        {
            await _page.GetByText("Custom PR").First.WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
            // The dialog's Create PR is the last one on the page
            var buttons = _page.GetByRole(AriaRole.Button, new() { Name = "Create PR" });
            var count = await buttons.CountAsync();
            await buttons.Nth(count - 1).ClickAsync();
        }
        catch (TimeoutException)
        {
            // No dialog — PrRule is "yolo", PR creation started directly
        }
    }

    public async Task WaitForPRCreated(int timeoutMs = 120_000)
    {
        await _page.Locator("text=/Pull request|PR created|pull request/i")
            .First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
    }
}
