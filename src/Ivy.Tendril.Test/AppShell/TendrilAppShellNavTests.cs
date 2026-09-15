using Ivy.Tendril.AppShell;

namespace Ivy.Tendril.Test.AppShell;

public class TendrilAppShellNavTests
{
    private static readonly MenuItem[] Menu =
    [
        MenuItem.Default("Plans", "plans"),
        MenuItem.Default("Inbox", "inbox"),
        MenuItem.Default("Chat", "chat"),
        MenuItem.Default("Agent", "agent")
    ];

    [Fact]
    public void BuildNavItems_ExcludesAgentAndChat_KeepsInboxByDefault()
    {
        var items = TendrilAppShell.BuildNavItems(Menu, activeAppId: "inbox");

        Assert.Equal(["plans", "inbox"], items.Select(i => i.Id));
        Assert.True(items.Single(i => i.Id == "inbox").IsActive);
    }

    [Fact]
    public void BuildNavItems_ExcludesFooterApps_CaseInsensitively()
    {
        var items = TendrilAppShell.BuildNavItems(Menu, activeAppId: null, footerAppIds: ["INBOX"]);

        Assert.Equal(["plans"], items.Select(i => i.Id));
    }

    [Fact]
    public void ShowInboxInFooter_PairsInboxWithSettings_OutsideShareMode()
    {
        // The footer pair is no longer beta-only: normal runs get it too.
        Assert.True(TendrilAppShell.ShowInboxInFooter(isShareMode: false));
    }

    [Fact]
    public void ShowInboxInFooter_IsSuppressed_InShareMode()
    {
        // Share mode's footer identifies the reviewer instead of exposing settings.
        Assert.False(TendrilAppShell.ShowInboxInFooter(isShareMode: true));
    }
}
