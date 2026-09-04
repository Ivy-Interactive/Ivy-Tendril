using Ivy.Tendril.AppShell;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;
using Xunit;

namespace Ivy.Tendril.Test.Apps;

public class SidebarListActionsTests
{
    private static PlanFile CreatePlan(int id, PlanStatus status, List<string>? commits = null, string? sourceUrl = null)
    {
        var metadata = new PlanMetadata(
            id, "Ivy", "Bug", $"Plan {id}", status,
            [], commits ?? [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, sourceUrl);
        return new PlanFile(metadata, "", $"/tmp/plans/{id:00000}-Plan", "");
    }

    [Fact]
    public void PlansSidebarList_OffersExecuteAction()
    {
        var plans = new List<PlanFile> { CreatePlan(1, PlanStatus.Draft) };

        var list = PlansApp.BuildSidebarList(plans, null);

        var action = Assert.Single(list.Items[0].Actions!);
        Assert.Equal(ShellSidebarActions.Execute, action.Id);
        Assert.Equal("Execute", action.Label);
        Assert.True(action.Primary);
    }

    [Fact]
    public void PlansSidebarList_HidesActionsWhenNotAllowed()
    {
        var plans = new List<PlanFile> { CreatePlan(1, PlanStatus.Draft) };

        var list = PlansApp.BuildSidebarList(plans, null, allowActions: false);

        Assert.Null(list.Items[0].Actions);
    }

    [Fact]
    public void PlansSidebarList_ForwardsItemActionHandler()
    {
        string? receivedItem = null;
        string? receivedAction = null;
        var plans = new List<PlanFile> { CreatePlan(1, PlanStatus.Draft) };

        var list = PlansApp.BuildSidebarList(plans, null, (item, action) =>
        {
            receivedItem = item;
            receivedAction = action;
        });
        list.OnItemAction!("00001-Plan", ShellSidebarActions.Execute);

        Assert.Equal("00001-Plan", receivedItem);
        Assert.Equal(ShellSidebarActions.Execute, receivedAction);
    }

    [Fact]
    public void ReviewSidebarList_OffersCreatePrOnlyForPlansWithCommits()
    {
        var plans = new List<PlanFile>
        {
            CreatePlan(1, PlanStatus.Review, commits: ["abc1234"]),
            CreatePlan(2, PlanStatus.Review),
        };

        var list = ReviewApp.BuildSidebarList(plans, null);

        var action = Assert.Single(list.Items[0].Actions!);
        Assert.Equal(ShellSidebarActions.CreatePr, action.Id);
        Assert.Equal("Create PR", action.Label);
        Assert.Null(list.Items[1].Actions);
    }

    [Fact]
    public void ReviewSidebarList_LabelsUpdatePrForPullRequestSourcedPlans()
    {
        var plans = new List<PlanFile>
        {
            CreatePlan(1, PlanStatus.Review, commits: ["abc1234"],
                sourceUrl: "https://github.com/Ivy-Interactive/Ivy-Tendril/pull/42"),
        };

        var list = ReviewApp.BuildSidebarList(plans, null);

        Assert.Equal("Update PR", Assert.Single(list.Items[0].Actions!).Label);
    }

    [Fact]
    public void ReviewSidebarList_HidesActionsWhenNotAllowed()
    {
        var plans = new List<PlanFile> { CreatePlan(1, PlanStatus.Review, commits: ["abc1234"]) };

        var list = ReviewApp.BuildSidebarList(plans, null, allowActions: false);

        Assert.Null(list.Items[0].Actions);
    }
}
