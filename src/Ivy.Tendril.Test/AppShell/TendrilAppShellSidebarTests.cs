using static Ivy.Tendril.AppShell.TendrilAppShell;

namespace Ivy.Tendril.Test.AppShell;

public class TendrilAppShellSidebarTests
{
    [Theory]
    [InlineData("plans", true)]
    [InlineData("drafts", true)]
    [InlineData("review", true)]
    [InlineData("recommendations", true)]
    [InlineData("PLANS", true)]
    [InlineData("Review", true)]
    [InlineData("RECOMMENDATIONS", true)]
    [InlineData("settings", false)]
    [InlineData("jobs", false)]
    [InlineData("icebox", false)]
    [InlineData("agent", false)]
    [InlineData("$setup", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasSidebarSection_ReturnsExpectedValue(string? appId, bool expected)
    {
        var result = HasSidebarSection(appId);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("review", "review", true)]
    [InlineData("review", "REVIEW", true)]
    [InlineData("review", "plans", true)]
    [InlineData("plans", "review", true)]
    [InlineData("recommendations", "drafts", true)]
    [InlineData("review", "jobs", false)]
    [InlineData("review", "settings", false)]
    [InlineData("review", null, false)]
    [InlineData(null, "review", false)]
    [InlineData(null, null, false)]
    public void UsesSidebarList_ReturnsExpectedValue(string? listAppId, string? currentAppId, bool expected)
    {
        var result = UsesSidebarList(listAppId, currentAppId);
        Assert.Equal(expected, result);
    }
}
