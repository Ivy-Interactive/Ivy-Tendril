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

        var mockConfigService = new MockConfigService { PlansFolder = plansRoot };
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
        public string PlansFolder { get; set; } = string.Empty;
        public List<Models.ProjectConfig> Projects { get; } = new();

        public string ConfigPath => string.Empty;
        public string TendrilHome => string.Empty;
        public string? DefaultExecutionProfile => null;
        public List<Models.Level> Levels => new();
        public List<Models.VerificationConfig> Verifications => new();
        public Dictionary<string, string> ReviewActions => new();
        public Models.LLMConfig LLM => new();
        public string? AppsUri => null;
        public List<string> PinnedTools => new();
        public bool McpEnabled => false;
        public List<Models.McpServerConfig> McpServers => new();

        public string? GetAppUri(string appName) => null;
        public Models.ProjectConfig? GetProject(string projectName) => null;
        public Models.VerificationConfig? GetVerification(string verificationName) => null;
        public void Reload() { }
    }
}
