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
}
