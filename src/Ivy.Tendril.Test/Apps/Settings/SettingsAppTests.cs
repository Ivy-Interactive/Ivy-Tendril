using Ivy.Tendril.Apps.Settings;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Settings;

public class SettingsAppTests
{
    [Fact]
    public void BuildSections_WhenBetaFalse_DoesNotIncludeIssueTrackersOrVault()
    {
        var sections = SettingsApp.BuildSections(isBeta: false);

        Assert.DoesNotContain(sections, s => s.Tag == SettingsApp.TagIssueTrackers);
        Assert.DoesNotContain(sections, s => s.Tag == SettingsApp.TagVault);
    }

    [Fact]
    public void BuildSections_WhenBetaTrue_IncludesIssueTrackersAndVault()
    {
        var sections = SettingsApp.BuildSections(isBeta: true);

        Assert.Contains(sections, s => s.Tag == SettingsApp.TagIssueTrackers && s.Label == "Issue Trackers");
        Assert.Contains(sections, s => s.Tag == SettingsApp.TagVault && s.Label == "Team Vault");
    }

    [Fact]
    public void ResolveTagContent_WhenBetaFalse_IssueTrackersFallsBackToDefault()
    {
        var view = SettingsApp.ResolveTagContent(SettingsApp.TagIssueTrackers, isBeta: false);

        Assert.IsType<CodingAgentSetupView>(view);
    }

    [Fact]
    public void ResolveTagContent_WhenBetaTrue_IssueTrackersReturnsIssueTrackersSetupView()
    {
        var view = SettingsApp.ResolveTagContent(SettingsApp.TagIssueTrackers, isBeta: true);

        Assert.IsType<IssueTrackersSetupView>(view);
    }

    [Fact]
    public void ResolveTagContent_WhenBetaFalse_VaultFallsBackToDefault()
    {
        var view = SettingsApp.ResolveTagContent(SettingsApp.TagVault, isBeta: false);

        Assert.IsType<CodingAgentSetupView>(view);
    }

    [Fact]
    public void ResolveTagContent_WhenBetaTrue_VaultReturnsVaultSetupView()
    {
        var view = SettingsApp.ResolveTagContent(SettingsApp.TagVault, isBeta: true);

        Assert.IsType<VaultSetupView>(view);
    }
}
