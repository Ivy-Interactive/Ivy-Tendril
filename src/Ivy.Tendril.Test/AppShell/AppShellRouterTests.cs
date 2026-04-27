using System.Collections.Immutable;
using Ivy.Core.Apps;
using Ivy.Tendril.AppShell;
using static Ivy.Tendril.AppShell.TendrilAppShell;

namespace Ivy.Tendril.Test.AppShell;

public class AppShellRouterTests
{
    [Fact]
    public void RouteForPages_WithAppId_ReturnsOpenPage()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs("plans"),
            AppShellNavigation.Pages,
            "default",
            ImmutableArray<TabState>.Empty,
            null,
            false);

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
            null,
            false);

        Assert.Equal(AppShellRouter.RouteAction.OpenPage, result.Action);
        Assert.Equal("default", result.EffectiveAppId);
    }

    [Fact]
    public void RouteForTabs_ExistingTabId_ReturnsSwitchToExistingTab()
    {
        var tabs = ImmutableArray.Create(
            new TabState("tab1", "plans", "Plans", null!, null, "key1"));
        var router = new AppShellRouter();

        var result = router.Route(
            new NavigateArgs(null, null, "tab1"),
            AppShellNavigation.Tabs,
            null,
            tabs,
            null,
            false);

        Assert.Equal(AppShellRouter.RouteAction.SwitchToExistingTab, result.Action);
        Assert.Equal(0, result.TabIndex);
        Assert.Equal("tab1", result.TabId);
    }

    [Fact]
    public void RouteForTabs_MissingTabIdWithPopOp_ReturnsError()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs(null, null, "nonexistent", HistoryOp.Pop),
            AppShellNavigation.Tabs,
            null,
            ImmutableArray<TabState>.Empty,
            null,
            false);

        Assert.Equal(AppShellRouter.RouteAction.Error, result.Action);
        Assert.Equal("Tab no longer exists.", result.ErrorMessage);
    }

    [Fact]
    public void RouteForTabs_DuplicateAppId_ReturnsSwitchToExistingTab()
    {
        var tabs = ImmutableArray.Create(
            new TabState("tab1", "plans", "Plans", null!, null, "key1"));
        var appDescriptor = new AppDescriptor
        {
            Id = "plans",
            Title = "Plans",
            Group = [],
            IsVisible = true,
            AllowDuplicateTabs = false
        };
        var router = new AppShellRouter();

        var result = router.Route(
            new NavigateArgs("plans"),
            AppShellNavigation.Tabs,
            null,
            tabs,
            appDescriptor,
            true);

        Assert.Equal(AppShellRouter.RouteAction.SwitchToExistingTab, result.Action);
        Assert.Equal(0, result.TabIndex);
    }

    [Fact]
    public void RouteForTabs_NewAppId_ReturnsCreateNewTab()
    {
        var router = new AppShellRouter();
        var result = router.Route(
            new NavigateArgs("review"),
            AppShellNavigation.Tabs,
            null,
            ImmutableArray<TabState>.Empty,
            null,
            false);

        Assert.Equal(AppShellRouter.RouteAction.CreateNewTab, result.Action);
        Assert.Equal("review", result.EffectiveAppId);
    }
}
