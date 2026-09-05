using Ivy.Tendril.Commands.DoctorChecks;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test;

public class PathBudgetCheckTests
{
    [Fact]
    public void Classify_ReturnsOkAtMaxBudget()
    {
        var result = PathBudgetCheck.Classify(95);

        Assert.Equal(StatusKind.Ok, result);
    }

    [Fact]
    public void Classify_ReturnsWarnAt96()
    {
        var result = PathBudgetCheck.Classify(96);

        Assert.Equal(StatusKind.Warn, result);
    }

    [Fact]
    public void Classify_ReturnsWarnAt125()
    {
        var result = PathBudgetCheck.Classify(125);

        Assert.Equal(StatusKind.Warn, result);
    }

    [Fact]
    public void Classify_ReturnsErrorAt126()
    {
        var result = PathBudgetCheck.Classify(126);

        Assert.Equal(StatusKind.Error, result);
    }

    [Fact]
    public async Task RunAsync_WithLegacyFolders_ReportsWarnButNoError()
    {
        using var fixture = new TempDirectoryFixture();
        var plansRoot = fixture.TempDirectory;

        var legacyFolderName = "00001-" + new string('A', 60);
        Directory.CreateDirectory(Path.Combine(plansRoot, legacyFolderName));

        var mockConfigService = new MockConfigService { PlanFolder = plansRoot };
        var check = new PathBudgetCheck(mockConfigService);

        var result = await check.RunAsync();

        Assert.False(result.HasErrors, "Legacy folders should not cause HasErrors to be true");
        var legacyStatus = result.Statuses.FirstOrDefault(s => s.Label == "Legacy plan folders over budget");
        Assert.NotNull(legacyStatus);
        Assert.Equal(StatusKind.Warn, legacyStatus.Kind);
        Assert.Contains("1 folder", legacyStatus.Value);
    }

    private class MockConfigService : Services.IConfigService
    {
        public string PlanFolder { get; set; } = string.Empty;
        public List<Services.ProjectConfig> Projects { get; } = new();

        public Services.TendrilSettings Settings => new();
        public string ConfigPath => string.Empty;
        public string TendrilHome => string.Empty;
        public List<Services.LevelConfig> Levels => new();
        public string[] LevelNames => Array.Empty<string>();
        public Services.EditorConfig Editor => new();
        public bool NeedsOnboarding => false;
        public Services.ConfigParseError? ParseError => null;

        public Services.ProjectConfig? GetProject(string name) => null;
        public bool TryAutoHeal() => false;
        public void ResetToDefaults() { }
        public void RetryLoadConfig() { }
        public Ivy.Colors? GetLevelColor(string level) => null;
        public Ivy.Colors? GetProjectColor(string projectName) => null;
        public void SaveSettings() { }
        public void MutateAndSave(Action<Services.TendrilSettings> mutate) { }
        public void ReloadSettings() { }
        public event EventHandler? SettingsReloaded;
        public void SetPendingTendrilHome(string path) { }
        public string? GetPendingTendrilHome() => null;
        public void SetPendingProject(Services.ProjectConfig project) { }
        public Services.ProjectConfig? GetPendingProject() => null;
        public void SetPendingCodingAgent(string name) { }
        public string? GetPendingCodingAgent() => null;
        public void SetPendingVerificationDefinitions(List<Services.VerificationConfig> definitions) { }
        public List<Services.VerificationConfig>? GetPendingVerificationDefinitions() => null;
        public void CompleteOnboarding(string tendrilHome) { }
        public void OpenInEditor(string path) { }
        public string PolishMarkdown(string content) => content;
        public void Dispose() { }
    }
}
