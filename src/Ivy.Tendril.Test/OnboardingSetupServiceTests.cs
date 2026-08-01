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

    [Fact]
    public async Task FinalizeOnboardingAsync_Should_Seed_CheckResult_Prompt_With_Runtime_Behavior_Step()
    {
        // Arrange: create a bootstrapped config.yaml
        var tendrilHome = Path.Combine(_tempDir.Path, "tendril-home");
        Directory.CreateDirectory(tendrilHome);
        var configPath = Path.Combine(tendrilHome, "config.yaml");
        File.WriteAllText(configPath, "codingAgent: claude\nprojects: []\n");

        var configService = new ConfigService(new TendrilSettings());
        configService.SetPendingTendrilHome(tendrilHome);
        configService.SetPendingProject(new ProjectConfig
        {
            Name = "TestProject",
            Repos = new List<RepoRef> { new() { Path = "/tmp/test-repo" } }
        });

        var onboardingService = new OnboardingSetupService(
            configService,
            null!,
            null!,
            NullLogger<OnboardingSetupService>.Instance);

        // Act
        await onboardingService.FinalizeOnboardingAsync();

        // Assert: read the seeded config and find CheckResult verification
        var savedYaml = File.ReadAllText(configPath);
        var savedSettings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(savedYaml);

        Assert.NotNull(savedSettings);
        var checkResult = savedSettings.Verifications.FirstOrDefault(v => v.Name == "CheckResult");
        Assert.NotNull(checkResult);

        // Assert the prompt contains the runtime behavior exercise step
        Assert.Contains("exercise the documented behavior", checkResult.Prompt);

        // Assert numbered steps are contiguous from 1 and report step is last
        var lines = checkResult.Prompt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var stepNumbers = new List<int>();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length > 0 && char.IsDigit(trimmed[0]) && trimmed.Contains('.'))
            {
                var numStr = new string(trimmed.TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(numStr, out var num))
                {
                    stepNumbers.Add(num);
                }
            }
        }

        // Steps should be contiguous starting from 1
        Assert.NotEmpty(stepNumbers);
        Assert.Equal(1, stepNumbers.First());
        for (int i = 0; i < stepNumbers.Count - 1; i++)
        {
            Assert.Equal(stepNumbers[i] + 1, stepNumbers[i + 1]);
        }

        // Last step should be the report-writing step
        var lastStepIndex = checkResult.Prompt.LastIndexOf($"{stepNumbers.Last()}.");
        Assert.NotEqual(-1, lastStepIndex);
        var textAfterLastStep = checkResult.Prompt.Substring(lastStepIndex);
        Assert.Contains("Write the verification report", textAfterLastStep);
    }
}
