using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class OnboardingSetupServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("ivy-onboarding-test");

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public async Task FinalizeOnboardingAsync_Should_Persist_Project_To_Config_File()
    {
        // Arrange: create a bootstrapped config.yaml at the tendril home path (no projects)
        var tendrilHome = Path.Combine(_tempDir.Path, "tendril-home");
        Directory.CreateDirectory(tendrilHome);
        var configPath = Path.Combine(tendrilHome, "config.yaml");
        File.WriteAllText(configPath, "codingAgent: claude\nprojects: []\n");

        // Create a ConfigService in onboarding mode (no TENDRIL_HOME set)
        var configService = new ConfigService(new TendrilSettings());
        configService.SetPendingTendrilHome(tendrilHome);
        configService.SetPendingProject(new ProjectConfig
        {
            Name = "TestProject",
            Repos = new List<RepoRef> { new() { Path = "/tmp/test-repo" } }
        });

        // Create the OnboardingSetupService
        var onboardingService = new OnboardingSetupService(
            configService,
            null!,
            null!,
            NullLogger<OnboardingSetupService>.Instance);

        // Act
        await onboardingService.FinalizeOnboardingAsync();

        // Assert: re-read the config file at the tendril home path
        var savedYaml = File.ReadAllText(configPath);
        var savedSettings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(savedYaml);

        Assert.NotNull(savedSettings);
        Assert.Single(savedSettings.Projects);
        Assert.Equal("TestProject", savedSettings.Projects[0].Name);
        Assert.Equal("/tmp/test-repo", savedSettings.Projects[0].Repos[0].Path);
    }
}
