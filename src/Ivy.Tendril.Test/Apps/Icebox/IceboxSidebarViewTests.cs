using Ivy;
using Ivy.Core;
using Ivy.Tendril.Apps.Icebox;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;

namespace Ivy.Tendril.Test.Apps.Icebox;

public class IceboxSidebarViewTests
{
    private static PlanFile Plan(string project) => new(
        new PlanMetadata(1, project, "Bug", "Test Plan", PlanStatus.Icebox,
            [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null),
        "", "/plans/00001-TestPlan", "");

    private static object[] Children(PlanFile plan, IConfigService config) =>
        ((IWidget)SidebarView.BuildRowBadges(plan, config).Build()!).Children;

    [Fact]
    public void BuildRowBadges_SingleProject_RendersProjectAndLevelBadge()
    {
        var config = new StubConfigService(
        [
            new ProjectConfig { Name = "Tendril", Color = "Blue" }
        ]);

        var children = Children(Plan("Tendril"), config);

        var badges = children.OfType<Badge>().ToList();
        Assert.Equal(2, badges.Count);

        Assert.Equal("Tendril", badges[0].Title);
        Assert.Equal(BadgeVariant.Outline, badges[0].Variant);
        Assert.Equal(Colors.Blue, badges[0].Color);

        Assert.Equal("Bug", badges[1].Title);
        Assert.Equal(Colors.Gray, badges[1].Color);
    }

    [Fact]
    public void BuildRowBadges_MultipleProjects_RendersOneBadgePerProject()
    {
        var config = new StubConfigService(
        [
            new ProjectConfig { Name = "Tendril", Color = "Blue" },
            new ProjectConfig { Name = "Framework", Color = "Amber" }
        ]);

        var children = Children(Plan("Tendril, Framework"), config);

        var badges = children.OfType<Badge>().ToList();
        Assert.Equal(3, badges.Count);

        Assert.Equal("Tendril", badges[0].Title);
        Assert.Equal(BadgeVariant.Outline, badges[0].Variant);
        Assert.Equal(Colors.Blue, badges[0].Color);

        Assert.Equal("Framework", badges[1].Title);
        Assert.Equal(BadgeVariant.Outline, badges[1].Variant);
        Assert.Equal(Colors.Amber, badges[1].Color);

        Assert.Equal("Bug", badges[2].Title);
        Assert.Equal(Colors.Gray, badges[2].Color);
    }

    [Fact]
    public void BuildRowBadges_EmptyProject_RendersOnlyLevelBadge()
    {
        var config = new StubConfigService();

        var children = Children(Plan(""), config);

        var badges = children.OfType<Badge>().ToList();
        var badge = Assert.Single(badges);
        Assert.Equal("Bug", badge.Title);
    }

    [Fact]
    public void BuildRowBadges_UnknownProject_RendersUncolouredBadge()
    {
        var config = new StubConfigService(
        [
            new ProjectConfig { Name = "Tendril", Color = "Blue" }
        ]);

        var children = Children(Plan("UnknownProject"), config);

        var badges = children.OfType<Badge>().ToList();
        Assert.Equal(2, badges.Count);

        Assert.Equal("UnknownProject", badges[0].Title);
        Assert.Equal(BadgeVariant.Outline, badges[0].Variant);
        Assert.Null(badges[0].Color);
    }

    [Fact]
    public void BuildProjectOptions_MultiProjectPlan_CountsEachProjectSeparately()
    {
        var plans = new[]
        {
            Plan("Tendril, Framework"),
            Plan("Tendril")
        };

        var options = SidebarView.BuildProjectOptions(plans, null);

        Assert.Equal(2, options.Length);

        Assert.Equal("Tendril (2)", options[0].Label);
        Assert.Equal("Tendril", options[0].Value);

        Assert.Equal("Framework (1)", options[1].Label);
        Assert.Equal("Framework", options[1].Value);
    }

    [Fact]
    public void BuildProjectOptions_WithLevelFilter_NarrowsOptionSet()
    {
        var planBug = new PlanFile(
            new PlanMetadata(1, "Tendril", "Bug", "Test Plan", PlanStatus.Icebox,
                [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null),
            "", "/plans/00001-TestPlan", "");

        var planFeature = new PlanFile(
            new PlanMetadata(2, "Framework", "Feature", "Test Plan 2", PlanStatus.Icebox,
                [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null),
            "", "/plans/00002-TestPlan2", "");

        var plans = new[] { planBug, planFeature };

        var options = SidebarView.BuildProjectOptions(plans, "Bug");

        var option = Assert.Single(options);
        Assert.Equal("Tendril (1)", option.Label);
        Assert.Equal("Tendril", option.Value);
    }
}
