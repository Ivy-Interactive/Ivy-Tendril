using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
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
    public async Task BootstrapTendrilHomeAsync_Should_Skip_Shell_And_Pointer_When_TendrilE2E_Set()
    {
        var originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var originalE2E = Environment.GetEnvironmentVariable("TENDRIL_E2E");
        var tendrilHome = Path.Combine(_tempDir.Path, "bootstrap-home-e2e");
        var logger = new TestLogger<OnboardingSetupService>();
        var configService = new ConfigService(new TendrilSettings());
        var agentRunner = TestAgentRunner.Create();
        var service = new OnboardingSetupService(configService, agentRunner, null!, logger);

        try
        {
            Environment.SetEnvironmentVariable("TENDRIL_E2E", "1");
            await service.BootstrapTendrilHomeAsync(tendrilHome);

            Assert.Contains("Skipping shell configuration, pointer file, and user environment persistence in test mode", logger.GetOutput());

            var pointerFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril_location");
            if (File.Exists(pointerFile))
            {
                var content = await File.ReadAllTextAsync(pointerFile);
                Assert.DoesNotContain(tendrilHome, content);
            }

            var shell = Environment.GetEnvironmentVariable("SHELL") ?? "";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var rcFile = shell.EndsWith("/zsh") ? Path.Combine(home, ".zshrc")
                       : shell.EndsWith("/bash") ? Path.Combine(home, ".bashrc")
                       : Path.Combine(home, ".profile");
            if (File.Exists(rcFile))
            {
                var content = await File.ReadAllTextAsync(rcFile);
                Assert.DoesNotContain(tendrilHome, content);
            }

            if (OperatingSystem.IsWindows())
            {
                var userVar = Environment.GetEnvironmentVariable("TENDRIL_HOME", EnvironmentVariableTarget.User);
                Assert.NotEqual(tendrilHome, userVar);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", originalTendrilHome);
            Environment.SetEnvironmentVariable("TENDRIL_E2E", originalE2E);
        }
    }

    [Theory]
    [InlineData("TENDRIL_TEST")]
    [InlineData("TENDRIL_NO_PERSIST_SHELL")]
    public async Task BootstrapTendrilHomeAsync_Should_Skip_Shell_And_Pointer_When_Test_Env_Vars_Set(string envVarName)
    {
        var originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var originalVal = Environment.GetEnvironmentVariable(envVarName);
        var tendrilHome = Path.Combine(_tempDir.Path, $"bootstrap-home-{envVarName.ToLowerInvariant()}");
        var logger = new TestLogger<OnboardingSetupService>();
        var configService = new ConfigService(new TendrilSettings());
        var agentRunner = TestAgentRunner.Create();
        var service = new OnboardingSetupService(configService, agentRunner, null!, logger);

        try
        {
            Environment.SetEnvironmentVariable(envVarName, "1");
            await service.BootstrapTendrilHomeAsync(tendrilHome);

            Assert.Contains("Skipping shell configuration, pointer file, and user environment persistence in test mode", logger.GetOutput());

            var pointerFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril_location");
            if (File.Exists(pointerFile))
            {
                var content = await File.ReadAllTextAsync(pointerFile);
                Assert.DoesNotContain(tendrilHome, content);
            }

            var shell = Environment.GetEnvironmentVariable("SHELL") ?? "";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var rcFile = shell.EndsWith("/zsh") ? Path.Combine(home, ".zshrc")
                       : shell.EndsWith("/bash") ? Path.Combine(home, ".bashrc")
                       : Path.Combine(home, ".profile");
            if (File.Exists(rcFile))
            {
                var content = await File.ReadAllTextAsync(rcFile);
                Assert.DoesNotContain(tendrilHome, content);
            }

            if (OperatingSystem.IsWindows())
            {
                var userVar = Environment.GetEnvironmentVariable("TENDRIL_HOME", EnvironmentVariableTarget.User);
                Assert.NotEqual(tendrilHome, userVar);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", originalTendrilHome);
            Environment.SetEnvironmentVariable(envVarName, originalVal);
        }
    }

    private class TestLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public string GetOutput() => string.Join(Environment.NewLine, _messages);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
