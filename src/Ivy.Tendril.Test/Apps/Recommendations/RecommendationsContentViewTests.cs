using System;
using System.Collections.Generic;
using System.Linq;
using Ivy;
using Ivy.Tendril.Apps.Recommendations;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Recommendations;

public class RecommendationsContentViewTests
{
    private class FakeConfigService : IConfigService
    {
        public FakeConfigService(string tendrilHome = "D:/.tendril")
        {
            TendrilHome = tendrilHome;
        }

        public TendrilSettings Settings => new();
        public string TendrilHome { get; }
        public string ConfigPath => "";
        public string PlanFolder => "";
        public List<ProjectConfig> Projects => [];
        public List<LevelConfig> Levels => [];
        public string[] LevelNames => [];
        public EditorConfig Editor => new() { Command = "code", Label = "VS Code" };
        public bool NeedsOnboarding => false;
        public ConfigParseError? ParseError => null;

        public ProjectConfig? GetProject(string name) => null;
        public Colors? GetLevelColor(string level) => null;
        public Colors? GetProjectColor(string projectName) => null;
        public void SaveSettings() { }
        public void MutateAndSave(Action<TendrilSettings> mutate) => mutate(Settings);
        public void ReloadSettings() { }
        public bool TryAutoHeal() => false;
        public void ResetToDefaults() { }
        public void RetryLoadConfig() { }
#pragma warning disable CS0067
        public event EventHandler? SettingsReloaded;
#pragma warning restore CS0067
        public void SetPendingCodingAgent(string name) { }
        public string? GetPendingCodingAgent() => null;
        public void SetPendingTendrilHome(string path) { }
        public string? GetPendingTendrilHome() => null;
        public void SetPendingProject(ProjectConfig project) { }
        public ProjectConfig? GetPendingProject() => null;
        public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions) { }
        public List<VerificationConfig>? GetPendingVerificationDefinitions() => null;
        public void CompleteOnboarding(string tendrilHome) { }
        public void OpenInEditor(string path) { }
        public string PolishMarkdown(string content) => content;
        public void Dispose() { }
    }

    [Fact]
    public void BuildControlsLayout_WithProject_RendersProjectBadge()
    {
        var config = new FakeConfigService();
        var onDeclineCalled = false;
        var onAcceptCalled = false;

        var layout = ContentView.BuildControlsLayout(
            "ivy-tendril",
            0,
            5,
            () => onDeclineCalled = true,
            () => onAcceptCalled = true,
            config,
            isMobile: false);

        Assert.NotNull(layout);

        // Verify that the layout contains a Badge widget
        var badges = FindWidgetsOfType<Badge>(layout);
        Assert.NotEmpty(badges);
        var projectBadge = badges.FirstOrDefault(b => b.Label == "ivy-tendril");
        Assert.NotNull(projectBadge);
        Assert.Equal(BadgeVariant.Outline, projectBadge.Variant);
    }

    [Fact]
    public void BuildControlsLayout_WithNullOrEmptyProject_OmitsBadge()
    {
        var config = new FakeConfigService();

        // Test with null project
        var layoutWithNull = ContentView.BuildControlsLayout(
            null,
            0,
            5,
            () => { },
            () => { },
            config,
            isMobile: false);

        var badgesWithNull = FindWidgetsOfType<Badge>(layoutWithNull);
        Assert.Empty(badgesWithNull);

        // Test with empty string project
        var layoutWithEmpty = ContentView.BuildControlsLayout(
            "",
            0,
            5,
            () => { },
            () => { },
            config,
            isMobile: false);

        var badgesWithEmpty = FindWidgetsOfType<Badge>(layoutWithEmpty);
        Assert.Empty(badgesWithEmpty);
    }

    [Fact]
    public void BuildControlsLayout_RendersActionButtonsAndCounter()
    {
        var config = new FakeConfigService();
        var onDeclineCalled = false;
        var onAcceptCalled = false;

        var layout = ContentView.BuildControlsLayout(
            "ivy-tendril",
            2,
            10,
            () => onDeclineCalled = true,
            () => onAcceptCalled = true,
            config,
            isMobile: false);

        Assert.NotNull(layout);

        // Verify counter text is present
        var textBlocks = FindWidgetsOfType<Text>(layout);
        Assert.NotEmpty(textBlocks);

        // Verify Decline and Accept buttons are present
        var buttons = FindWidgetsOfType<Button>(layout);
        Assert.Contains(buttons, b => b.Label == "Decline");
        Assert.Contains(buttons, b => b.Label == "Accept");

        var declineButton = buttons.First(b => b.Label == "Decline");
        Assert.Equal(ButtonVariant.Outline, declineButton.Variant);

        var acceptButton = buttons.First(b => b.Label == "Accept");
        Assert.Equal(ButtonVariant.Primary, acceptButton.Variant);
    }

    private static List<T> FindWidgetsOfType<T>(object? root) where T : class
    {
        var results = new List<T>();
        if (root == null) return results;

        if (root is T match)
            results.Add(match);

        if (root is IWidget widget)
        {
            foreach (var child in widget.Children)
            {
                results.AddRange(FindWidgetsOfType<T>(child));
            }
        }

        return results;
    }
}
