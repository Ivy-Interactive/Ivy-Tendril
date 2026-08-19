using Ivy.Tendril.AppShell;

namespace Ivy.Tendril.Test.AppShell;

public class TendrilAppShellHelpMenuTests
{
    [Fact]
    public void BuildHelpMenuItems_BetaDisabled_ExcludesAboutMenuItem()
    {
        var items = TendrilAppShell.BuildHelpMenuItems(isBeta: false, client: null, navigator: null);

        Assert.Equal(3, items.Length);
        Assert.DoesNotContain(items, item => item.Label == "About");
        Assert.Equal(["Documentation", "Discord", "Report Issue"], items.Select(i => i.Label));
    }

    [Fact]
    public void BuildHelpMenuItems_BetaEnabled_IncludesAboutMenuItem()
    {
        var items = TendrilAppShell.BuildHelpMenuItems(isBeta: true, client: null, navigator: null);

        Assert.Equal(4, items.Length);
        Assert.Contains(items, item => item.Label == "About");
        Assert.Equal(["Documentation", "Discord", "Report Issue", "About"], items.Select(i => i.Label));
    }
}
