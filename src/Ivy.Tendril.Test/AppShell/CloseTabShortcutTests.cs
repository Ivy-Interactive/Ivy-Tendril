using Ivy.Tendril.AppShell;
using static Ivy.Tendril.AppShell.TendrilAppShell;

namespace Ivy.Tendril.Test.AppShell;

public class CloseTabShortcutTests
{
    [Fact]
    public void ShouldEnableCloseTabShortcut_DesktopTabsNavigationWithValidSelection_ReturnsTrue()
    {
        // Desktop shell with Tabs navigation, valid selected index.
        Assert.True(ShouldEnableCloseTabShortcut(
            isDesktop: true,
            navigation: AppShellNavigation.Tabs,
            tabCount: 3,
            selectedIndex: 1));
    }

    [Fact]
    public void ShouldEnableCloseTabShortcut_WebMode_ReturnsFalse()
    {
        // Web mode: the chord belongs to the browser and cannot be cancelled.
        Assert.False(ShouldEnableCloseTabShortcut(
            isDesktop: false,
            navigation: AppShellNavigation.Tabs,
            tabCount: 3,
            selectedIndex: 1));
    }

    [Fact]
    public void ShouldEnableCloseTabShortcut_PagesNavigation_ReturnsFalse()
    {
        // Pages navigation has no tab strip.
        Assert.False(ShouldEnableCloseTabShortcut(
            isDesktop: true,
            navigation: AppShellNavigation.Pages,
            tabCount: 3,
            selectedIndex: 1));
    }

    [Fact]
    public void ShouldEnableCloseTabShortcut_NullSelectedIndex_ReturnsFalse()
    {
        // Nothing selected (wallpaper showing).
        Assert.False(ShouldEnableCloseTabShortcut(
            isDesktop: true,
            navigation: AppShellNavigation.Tabs,
            tabCount: 3,
            selectedIndex: null));
    }

    [Fact]
    public void ShouldEnableCloseTabShortcut_SelectedIndexOutOfRange_ReturnsFalse()
    {
        // Selected index equals tab count (out of range).
        Assert.False(ShouldEnableCloseTabShortcut(
            isDesktop: true,
            navigation: AppShellNavigation.Tabs,
            tabCount: 2,
            selectedIndex: 2));
    }

    [Fact]
    public void ShouldEnableCloseTabShortcut_NegativeSelectedIndex_ReturnsFalse()
    {
        // Negative selected index (invalid).
        Assert.False(ShouldEnableCloseTabShortcut(
            isDesktop: true,
            navigation: AppShellNavigation.Tabs,
            tabCount: 3,
            selectedIndex: -1));
    }
}
