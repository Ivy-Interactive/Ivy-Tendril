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
    public void ExampleConfig_Should_Seed_CheckResult_Prompt_With_Runtime_Behavior_Step()
    {
        // Arrange: locate example.config.yaml in the project
        var projectDir = Path.GetDirectoryName(System.AppContext.BaseDirectory);
        while (projectDir != null && !File.Exists(Path.Combine(projectDir, "example.config.yaml")))
            projectDir = Path.GetDirectoryName(projectDir);

        var exampleConfigPath = projectDir != null
            ? Path.Combine(projectDir, "example.config.yaml")
            : PathHelper.GetResourcePath("example.config.yaml");

        Assert.True(File.Exists(exampleConfigPath), $"example.config.yaml not found at {exampleConfigPath}");

        // Act: read and parse example.config.yaml
        var configYaml = File.ReadAllText(exampleConfigPath);
        var settings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(configYaml);

        // Assert: find CheckResult verification
        Assert.NotNull(settings);
        Assert.NotNull(settings.Verifications);
        var checkResult = settings.Verifications.FirstOrDefault(v => v.Name == "CheckResult");
        Assert.NotNull(checkResult);

        // Assert the prompt contains the runtime behavior exercise step
        Assert.Contains("exercise the documented behavior", checkResult.Prompt);

        // Assert numbered steps are contiguous from 1 and report step is last
        // YAML > folding joins lines with spaces, so steps like "1. Foo" become "... 1. Foo ..."
        // We need to find patterns like " N. " or line-start "N. " where N is a digit
        var stepNumbers = new List<int>();
        var prompt = checkResult.Prompt;

        // Find all occurrences of " <digit>. " or "^<digit>. " patterns
        for (int i = 0; i < prompt.Length - 2; i++)
        {
            // Check if we're at start of line or after a space, followed by digit(s) and a period
            bool atLineStart = i == 0 || prompt[i - 1] == '\n';
            bool afterSpace = i > 0 && prompt[i - 1] == ' ' && (i == 1 || prompt[i - 2] == '\n' || prompt[i - 2] == ' ');

            if ((atLineStart || afterSpace) && char.IsDigit(prompt[i]) && prompt[i + 1] == '.')
            {
                // Extract the full number
                int j = i;
                while (j < prompt.Length && char.IsDigit(prompt[j]))
                    j++;

                if (j > i && j < prompt.Length && prompt[j] == '.')
                {
                    var numStr = prompt.Substring(i, j - i);
                    if (int.TryParse(numStr, out var num) && num >= 1 && num <= 10)
                    {
                        stepNumbers.Add(num);
                    }
                }
            }
        }

        // Remove duplicates and sort
        stepNumbers = stepNumbers.Distinct().OrderBy(n => n).ToList();

        // Steps should be contiguous starting from 1
        Assert.NotEmpty(stepNumbers);
        Assert.Equal(1, stepNumbers.First());
        for (int i = 0; i < stepNumbers.Count - 1; i++)
        {
            Assert.Equal(stepNumbers[i] + 1, stepNumbers[i + 1]);
        }

        // Last step should be the report-writing step
        var lastStepNum = stepNumbers.Last();
        var lastStepPattern = $" {lastStepNum}. ";
        var lastStepIndex = checkResult.Prompt.LastIndexOf(lastStepPattern);
        Assert.NotEqual(-1, lastStepIndex);
        var textAfterLastStep = checkResult.Prompt.Substring(lastStepIndex);
        Assert.Contains("Write the verification report", textAfterLastStep);
    }
}
