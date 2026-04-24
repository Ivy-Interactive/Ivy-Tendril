using Ivy.Tendril.Test.End2End.Helpers;
using Microsoft.Playwright;

namespace Ivy.Tendril.Test.End2End.Pages;

public class PlansPage
{
    private readonly IPage _page;

    public PlansPage(IPage page) => _page = page;

    public async Task ClickNewPlan() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "New Plan" }).First.ClickAsync();

    public async Task CreatePlan(string description, string project = "Auto", string priority = "Normal")
    {
        await ClickNewPlan();

        // Wait for the Create New Plan dialog
        await _page.GetByText("Create New Plan").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        // Select project if not Auto
        if (project != "Auto")
            await _page.GetByText(project).ClickAsync();

        // Select priority if not Normal
        if (priority != "Normal")
            await _page.GetByText(priority).ClickAsync();

        // Fill in the task description
        var textarea = _page.GetByPlaceholder("Enter task description...");
        await textarea.FillAsync(description);

        // Click Create
        await _page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
    }

    public async Task<bool> PlanExistsInList(string titleFragment)
    {
        try
        {
            await _page.GetByText(titleFragment).WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task SelectPlan(string titleFragment) =>
        await _page.GetByText(titleFragment).First.ClickAsync();

    public async Task SelectPlanById(string planId)
    {
        var locator = _page.GetByText($"#{planId}").First;
        await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await locator.ClickAsync();
    }

    public async Task ClickExecute() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Execute" }).ClickAsync();

    public async Task ClickUpdate() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Update" }).ClickAsync();

    public async Task ClickSplit() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Split" }).ClickAsync();

    public async Task ClickExpand() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Expand" }).ClickAsync();

    public async Task ClickDelete() =>
        await _page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

    public async Task WaitForPlanState(string plansDir, string titleFragment, string expectedState, int timeoutSeconds = 300)
    {
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                var folder = FileSystemAssertions.FindPlanFolder(plansDir, titleFragment);
                if (folder == null) return false;

                var yamlPath = Path.Combine(folder, "plan.yaml");
                if (!File.Exists(yamlPath)) return false;

                var content = await File.ReadAllTextAsync(yamlPath);
                return content.Contains($"state: {expectedState}", StringComparison.OrdinalIgnoreCase);
            },
            TimeSpan.FromSeconds(timeoutSeconds),
            pollInterval: TimeSpan.FromSeconds(2),
            failureMessage: $"Plan '{titleFragment}' did not reach state '{expectedState}' within {timeoutSeconds}s");
    }
}
