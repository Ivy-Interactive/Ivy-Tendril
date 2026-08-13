using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.ReviewAction;
using static Ivy.Tendril.AppShell.TendrilAppShell;

namespace Ivy.Tendril.Test.AppShell;

public class TabTitleResolverTests
{
    [Fact]
    public void ResolveArgsTabTitle_ReviewActionArgs_FormatsPlanIdAndActionName()
    {
        var title = ResolveArgsTabTitle(
            new ReviewActionAppArgs("00074-OpenReviewActionsInAPTYTerminalTab", "Tendril"));

        Assert.Equal("#74 Tendril", title);
    }

    [Fact]
    public void ResolveArgsTabTitle_NoNumericPrefix_FallsBackToRawFolderName()
    {
        var title = ResolveArgsTabTitle(new ReviewActionAppArgs("NoNumericPrefix", "Tendril"));

        Assert.Equal("#NoNumericPrefix Tendril", title);
    }

    [Theory]
    [InlineData(null, "Tendril")]
    [InlineData("00074-OpenReviewActionsInAPTYTerminalTab", null)]
    [InlineData("", "Tendril")]
    [InlineData("00074-OpenReviewActionsInAPTYTerminalTab", "")]
    public void ResolveArgsTabTitle_MissingPlanIdOrActionName_ReturnsNull(string? planId, string? actionName)
    {
        var title = ResolveArgsTabTitle(new ReviewActionAppArgs(planId, actionName));

        Assert.Null(title);
    }

    [Fact]
    public void ResolveArgsTabTitle_NonReviewActionArgs_ReturnsNull()
    {
        var title = ResolveArgsTabTitle(new AgentAppArgs("Do something"));

        Assert.Null(title);
    }

    [Fact]
    public void ResolveArgsTabTitle_AgentArgs_ReturnsTitleVerbatim()
    {
        var title = ResolveArgsTabTitle(new AgentAppArgs("prompt", "#85"));

        Assert.Equal("#85", title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveArgsTabTitle_AgentArgs_NullOrEmptyTitle_ReturnsNull(string? title)
    {
        var result = ResolveArgsTabTitle(new AgentAppArgs("prompt", title));

        Assert.Null(result);
    }

    [Fact]
    public void ResolveArgsTabTitle_NullArgs_ReturnsNull()
    {
        Assert.Null(ResolveArgsTabTitle(null));
    }
}
