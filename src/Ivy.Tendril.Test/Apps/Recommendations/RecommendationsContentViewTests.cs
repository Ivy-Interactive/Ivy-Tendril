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
    public void BuildControlsLayout_WithProject_RendersLayout()
    {
        var config = new FakeConfigService();

        var layout = ContentView.BuildControlsLayout(
            "ivy-tendril",
            0,
            5,
            () => { },
            () => { },
            config,
            isMobile: false);

        // Verify that the method returns a non-null layout
        // Actual rendering is verified through integration tests
        Assert.NotNull(layout);
    }

    [Fact]
    public void BuildControlsLayout_WithNullOrEmptyProject_RendersLayout()
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

        Assert.NotNull(layoutWithNull);

        // Test with empty string project
        var layoutWithEmpty = ContentView.BuildControlsLayout(
            "",
            0,
            5,
            () => { },
            () => { },
            config,
            isMobile: false);

        Assert.NotNull(layoutWithEmpty);
    }

    [Fact]
    public void BuildControlsLayout_RendersWithCorrectParameters()
    {
        var config = new FakeConfigService();

        var layout = ContentView.BuildControlsLayout(
            "ivy-tendril",
            2,
            10,
            () => { },
            () => { },
            config,
            isMobile: false);

        // Verify layout is created
        Assert.NotNull(layout);

        // Note: Testing that buttons invoke callbacks requires simulating button clicks,
        // which is better suited for integration tests. This unit test verifies the method
        // signature and basic execution path.
    }
}
