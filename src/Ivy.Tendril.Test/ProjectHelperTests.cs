using Ivy;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;

namespace Ivy.Tendril.Test;

public class ProjectHelperTests
{
    [Theory]
    [InlineData("Framework", new[] { "Framework" })]
    [InlineData("Framework, Tendril", new[] { "Framework", "Tendril" })]
    [InlineData("Framework,Tendril,Agent", new[] { "Framework", "Tendril", "Agent" })]
    [InlineData("  Framework  ,  Tendril  ", new[] { "Framework", "Tendril" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void ParseProjects_HandlesVariousFormats(string? input, string[] expected)
    {
        var result = ProjectHelper.ParseProjects(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Framework", "Framework", true)]
    [InlineData("Framework, Tendril", "Framework", true)]
    [InlineData("Framework, Tendril", "Tendril", true)]
    [InlineData("Framework, Tendril", "Agent", false)]
    [InlineData("Framework", "framework", true)]
    [InlineData("", "Framework", false)]
    [InlineData(null, "Framework", false)]
    public void ContainsProject_ChecksMembership(string? projectValue, string projectToFind, bool expected)
    {
        var result = ProjectHelper.ContainsProject(projectValue, projectToFind);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Framework", "Framework")]
    [InlineData("Framework, Tendril", "Framework, Tendril")]
    [InlineData("  Framework  ,  Tendril  ", "Framework, Tendril")]
    [InlineData("", "Auto")]
    [InlineData(null, "Auto")]
    public void FormatProjectsForDisplay_FormatsCorrectly(string? input, string expected)
    {
        var result = ProjectHelper.FormatProjectsForDisplay(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildBadges_NullOrEmpty_ReturnsEmpty(string? input)
    {
        var config = new StubConfigService();
        var badges = ProjectHelper.BuildBadges(input, config).ToList();
        Assert.Empty(badges);
    }

    [Fact]
    public void BuildBadges_SingleProject_ReturnsOutlineBadgeWithConfiguredColor()
    {
        var config = new StubConfigService(
        [
            new ProjectConfig { Name = "Tendril", Color = "Blue" }
        ]);

        var badges = ProjectHelper.BuildBadges("Tendril", config).ToList();

        var badge = Assert.Single(badges);
        Assert.Equal("Tendril", badge.Title);
        Assert.Equal(BadgeVariant.Outline, badge.Variant);
        Assert.Equal(Colors.Blue, badge.Color);
    }

    [Fact]
    public void BuildBadges_MultipleProjects_ReturnsBadgeForEachProject()
    {
        var config = new StubConfigService(
        [
            new ProjectConfig { Name = "Tendril", Color = "Blue" },
            new ProjectConfig { Name = "Framework", Color = "Amber" }
        ]);

        var badges = ProjectHelper.BuildBadges("Tendril, Framework", config).ToList();

        Assert.Equal(2, badges.Count);

        Assert.Equal("Tendril", badges[0].Title);
        Assert.Equal(BadgeVariant.Outline, badges[0].Variant);
        Assert.Equal(Colors.Blue, badges[0].Color);

        Assert.Equal("Framework", badges[1].Title);
        Assert.Equal(BadgeVariant.Outline, badges[1].Variant);
        Assert.Equal(Colors.Amber, badges[1].Color);
    }
}
