using System.Collections.Immutable;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Widgets;
using static Ivy.Tendril.AppShell.TendrilAppShell;

namespace Ivy.Tendril.Test.AppShell;

/// <summary>
///     The bottom strip must always offer a way back to the page behind the session panes;
///     without it, running a review action strands the reviewer on its terminal (issue #2245).
/// </summary>
public class StripTabsTests
{
    private static TabState Tab(string id, string appId) => new(id, appId, id, null!, null, "key");

    [Fact]
    public void BuildStripTabs_LeadsWithNonClosablePageTab()
    {
        var strip = BuildStripTabs("Review", "ThumbsUp", []);

        var pageTab = Assert.Single(strip);
        Assert.Equal(PageTabId, pageTab.Id);
        Assert.Equal("Review", pageTab.Title);
        Assert.Equal("ThumbsUp", pageTab.Icon);
        Assert.False(pageTab.Closable);
    }

    [Fact]
    public void BuildStripTabs_KeepsSessionTabsAfterThePageTab()
    {
        var strip = BuildStripTabs("Review", "ThumbsUp", [("tab1", "#74 Run"), ("tab2", "#74 Build")]);

        Assert.Equal([PageTabId, "tab1", "tab2"], strip.Select(t => t.Id));
        Assert.All(strip.Skip(1), t => Assert.True(t.Closable));
    }

    [Fact]
    public void BuildStripTabs_WallpaperPage_CarriesNoIcon()
    {
        // No page open: the tab is "Home" with no app icon, and the frontend picks its own glyph.
        var pageTab = BuildStripTabs("Home", null, [("tab1", "#74 Run")])[0];

        Assert.Equal("Home", pageTab.Title);
        Assert.Null(pageTab.Icon);
        Assert.False(pageTab.Closable);
    }

    private static ShellSidebarListState List(string appId, string? selectedId, params (string Id, string Title)[] rows)
        => new(appId, "Plans", rows.Select(r => new ShellSectionItemDto(r.Id, r.Title)).ToList(),
            selectedId, _ => null);

    [Fact]
    public void PageTabTitle_SelectedRow_NamesTheRowNotTheApp()
    {
        // "#74 Draft | #74 Run Tests" reads better than "Plans | #74 Run Tests".
        var list = List("plans", "p74", ("p73", "#73 Older"), ("p74", "#74 Draft"));

        Assert.Equal("#74 Draft", PageTabTitle("Plans", list));
    }

    [Fact]
    public void PageTabTitle_NoSidebarList_UsesTheAppTitle()
    {
        Assert.Equal("Configuration", PageTabTitle("Configuration", null));
    }

    [Fact]
    public void PageTabTitle_NoRowSelected_UsesTheAppTitle()
    {
        var list = List("plans", null, ("p74", "#74 Draft"));

        Assert.Equal("Plans", PageTabTitle("Plans", list));
    }

    [Fact]
    public void PageTabTitle_SelectionMissingFromRows_UsesTheAppTitle()
    {
        // The published list can lag its selection for a render; never show an empty tab.
        var list = List("plans", "p99", ("p74", "#74 Draft"));

        Assert.Equal("Plans", PageTabTitle("Plans", list));
    }

    [Fact]
    public void PageTabTitle_BlankRowTitle_UsesTheAppTitle()
    {
        var list = List("plans", "p74", ("p74", ""));

        Assert.Equal("Plans", PageTabTitle("Plans", list));
    }

    [Fact]
    public void SelectedStripTabId_NoSessionSelected_SelectsThePageTab()
    {
        var tabs = ImmutableArray.Create(Tab("tab1", "review-action"));

        Assert.Equal(PageTabId, SelectedStripTabId(tabs, null));
    }

    [Fact]
    public void SelectedStripTabId_ReviewActionSelected_SelectsThatTab()
    {
        var tabs = ImmutableArray.Create(Tab("tab1", "review-action"), Tab("tab2", "review-action"));

        Assert.Equal("tab2", SelectedStripTabId(tabs, 1));
    }

    [Fact]
    public void SelectedStripTabId_TerminalPaneActive_SelectsNothing()
    {
        // Terminal panes live in the Chats list, not the strip, so the page tab must not
        // look current while one is showing.
        var tabs = ImmutableArray.Create(Tab("tab1", "review-action"), Tab("session1", "agent"));

        Assert.Null(SelectedStripTabId(tabs, 1));
    }

    [Fact]
    public void SelectedStripTabId_IndexOutOfRange_FallsBackToThePageTab()
    {
        var tabs = ImmutableArray.Create(Tab("tab1", "review-action"));

        Assert.Equal(PageTabId, SelectedStripTabId(tabs, 5));
    }
}
