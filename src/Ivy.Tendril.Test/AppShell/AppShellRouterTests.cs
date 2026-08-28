using System.Collections.Immutable;
using Ivy.Core.Apps;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Apps.Review;
using static Ivy.Tendril.AppShell.TendrilAppShell;

namespace Ivy.Tendril.Test.AppShell;

public class AppShellRouterTests
{
    private static AppDescriptor Descriptor(string id, bool allowDuplicateTabs) => new()
    {
        Id = id,
        Title = id,
        Group = [],
        IsVisible = true,
        AllowDuplicateTabs = allowDuplicateTabs
    };

    [Fact]
    public void RouteForPages_WithAppId_ReturnsOpenPage()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs("plans"),
            AppShellNavigation.Pages,
            "default",
            ImmutableArray<TabState>.Empty,
            null);

        Assert.Equal(AppShellRouter.RouteAction.OpenPage, result.Action);
        Assert.Equal("plans", result.EffectiveAppId);
    }

    [Fact]
    public void RouteForPages_WithoutAppId_UsesDefault()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs(null),
            AppShellNavigation.Pages,
            "default",
            ImmutableArray<TabState>.Empty,
            null);

        Assert.Equal(AppShellRouter.RouteAction.OpenPage, result.Action);
        Assert.Equal("default", result.EffectiveAppId);
    }

    [Fact]
    public void RouteHybrid_ExistingTabId_ReturnsSwitchToExistingTab()
    {
        var tabs = ImmutableArray.Create(
            new TabState("tab1", "agent", "Claude Code", null!, null, "key1"));
        var router = new AppShellRouter();

        var result = router.Route(
            new NavigateArgs(null, null, "tab1"),
            AppShellNavigation.Tabs,
            null,
            tabs,
            null);

        Assert.Equal(AppShellRouter.RouteAction.SwitchToExistingTab, result.Action);
        Assert.Equal(0, result.TabIndex);
        Assert.Equal("tab1", result.TabId);
    }

    [Fact]
    public void RouteHybrid_MissingTabIdWithPopOp_ReturnsError()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs(null, null, "nonexistent", HistoryOp.Pop),
            AppShellNavigation.Tabs,
            null,
            ImmutableArray<TabState>.Empty,
            null);

        Assert.Equal(AppShellRouter.RouteAction.Error, result.Action);
        Assert.Equal("Tab no longer exists.", result.ErrorMessage);
    }

    [Fact]
    public void RouteHybrid_RegularApp_ReturnsOpenPage()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs("review", new ReviewAppArgs("00010-A")),
            AppShellNavigation.Tabs,
            null,
            ImmutableArray<TabState>.Empty,
            Descriptor("review", allowDuplicateTabs: false));

        Assert.Equal(AppShellRouter.RouteAction.OpenPage, result.Action);
        Assert.Equal("review", result.EffectiveAppId);
    }

    [Fact]
    public void RouteHybrid_UnknownDescriptor_ReturnsOpenPage()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs("review"),
            AppShellNavigation.Tabs,
            null,
            ImmutableArray<TabState>.Empty,
            null);

        Assert.Equal(AppShellRouter.RouteAction.OpenPage, result.Action);
        Assert.Equal("review", result.EffectiveAppId);
    }

    [Fact]
    public void RouteHybrid_SessionApp_ReturnsCreateNewTab()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs("agent"),
            AppShellNavigation.Tabs,
            null,
            ImmutableArray<TabState>.Empty,
            Descriptor("agent", allowDuplicateTabs: true));

        Assert.Equal(AppShellRouter.RouteAction.CreateNewTab, result.Action);
        Assert.Equal("agent", result.EffectiveAppId);
    }

    [Fact]
    public void RouteHybrid_SessionApp_ExistingTabForSameApp_StillCreatesNewTab()
    {
        var tabs = ImmutableArray.Create(
            new TabState("tab1", "agent", "Claude Code", null!, null, "key1"));
        var router = new AppShellRouter();

        var result = router.Route(
            new NavigateArgs("agent"),
            AppShellNavigation.Tabs,
            null,
            tabs,
            Descriptor("agent", allowDuplicateTabs: true));

        Assert.Equal(AppShellRouter.RouteAction.CreateNewTab, result.Action);
    }

    [Fact]
    public void RouteHybrid_NullAppId_ReturnsNoop()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs(null),
            AppShellNavigation.Tabs,
            "default",
            ImmutableArray<TabState>.Empty,
            null);

        Assert.Equal(AppShellRouter.RouteAction.Noop, result.Action);
    }
}
