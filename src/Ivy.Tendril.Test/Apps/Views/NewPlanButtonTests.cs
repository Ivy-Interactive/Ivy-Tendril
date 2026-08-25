using System.Runtime.InteropServices;
using Ivy.Tendril.Apps.Views;

namespace Ivy.Tendril.Test.Apps.Views;

public class NewPlanButtonTests
{
    [Fact]
    public void GetTooltip_WhenMac_ReturnsMacShortcut()
    {
        var tooltip = NewPlanButton.GetTooltip(isMac: true);
        Assert.Equal("New Plan (⌘+⌥+N)", tooltip);
    }

    [Fact]
    public void GetTooltip_WhenNonMac_ReturnsCtrlShortcut()
    {
        var tooltip = NewPlanButton.GetTooltip(isMac: false);
        Assert.Equal("New Plan (Ctrl+Alt+N)", tooltip);
    }

    [Fact]
    public void GetTooltip_Default_MatchesCurrentPlatform()
    {
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var expected = isMac ? "New Plan (⌘+⌥+N)" : "New Plan (Ctrl+Alt+N)";
        Assert.Equal(expected, NewPlanButton.GetTooltip());
    }
}
