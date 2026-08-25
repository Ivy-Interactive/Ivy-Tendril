using static Ivy.Tendril.AppShell.TendrilAppShell;

namespace Ivy.Tendril.Test.AppShell;

public class AppShellNotificationTests
{
    [Fact]
    public void ShouldShowInAppToast_DesktopWithNativeNotificationsEnabled_ReturnsFalse()
    {
        // Native notification covers it, so the in-app toast would be a duplicate.
        Assert.False(ShouldShowInAppToast(isDesktop: true, desktopNotificationsEnabled: true));
    }

    [Fact]
    public void ShouldShowInAppToast_DesktopWithNativeNotificationsDisabled_ReturnsTrue()
    {
        // Native path is suppressed by the setting, so the toast is the only notification left.
        Assert.True(ShouldShowInAppToast(isDesktop: true, desktopNotificationsEnabled: false));
    }

    [Fact]
    public void ShouldShowInAppToast_WebWithNativeNotificationsEnabled_ReturnsTrue()
    {
        // Web mode has no native notification path, so it must always toast.
        Assert.True(ShouldShowInAppToast(isDesktop: false, desktopNotificationsEnabled: true));
    }

    [Fact]
    public void ShouldShowInAppToast_WebWithNativeNotificationsDisabled_ReturnsTrue()
    {
        // The setting only governs the native path; it must not disable the web toast.
        Assert.True(ShouldShowInAppToast(isDesktop: false, desktopNotificationsEnabled: false));
    }
}
