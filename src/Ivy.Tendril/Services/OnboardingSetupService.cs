using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class OnboardingSetupService(IConfigService config, IServiceProvider services, ILogger<OnboardingSetupService> logger) : IOnboardingSetupService
{
    private readonly ILogger<OnboardingSetupService> _logger = logger;

    public async Task CompleteSetupAsync(string tendrilHome)
    {
        _logger.LogInformation("Creating Tendril directory structure at {Path}", tendrilHome);

        // Create directory structure
        Directory.CreateDirectory(tendrilHome);
        Directory.CreateDirectory(Path.Combine(tendrilHome, "Inbox"));
        var planFolder = Environment.GetEnvironmentVariable("TENDRIL_PLANS")?.Trim() is { Length: > 0 } plans
            ? plans
            : Path.Combine(tendrilHome, "Plans");
        Directory.CreateDirectory(planFolder);
        await FileHelper.WriteAllTextAsync(Path.Combine(planFolder, ".counter"), "1");
        Directory.CreateDirectory(Path.Combine(tendrilHome, "Trash"));
        Directory.CreateDirectory(Path.Combine(tendrilHome, "Promptwares"));
        if (PromptwareDeployer.IsEmbeddedAvailable())
            PromptwareDeployer.Deploy(Path.Combine(tendrilHome, "Promptwares"));
        Directory.CreateDirectory(Path.Combine(tendrilHome, "Hooks"));

        _logger.LogInformation("Directory structure created");

        // Copy template or create basic config
        _logger.LogInformation("Writing config file to {Path}", Path.Combine(tendrilHome, "config.yaml"));
        var projectDir = Path.GetDirectoryName(System.AppContext.BaseDirectory);
        while (projectDir != null && !File.Exists(Path.Combine(projectDir, "example.config.yaml")))
            projectDir = Path.GetDirectoryName(projectDir);

        var exampleConfigPath = projectDir != null
            ? Path.Combine(projectDir, "example.config.yaml")
            : Path.Combine(System.AppContext.BaseDirectory, "example.config.yaml");

        var configPath = Path.Combine(tendrilHome, "config.yaml");

        if (File.Exists(exampleConfigPath))
        {
            var exampleContent = await FileHelper.ReadAllTextAsync(exampleConfigPath);
            await FileHelper.WriteAllTextAsync(configPath, exampleContent);
        }
        else if (!File.Exists(configPath))
        {
            var basicConfig = "codingAgent: claude\n" +
                              "jobTimeout: 30\n" +
                              "staleOutputTimeout: 10\n" +
                              "codingAgents:\n" +
                              "- name: ClaudeCode\n" +
                              "  profiles:\n" +
                              "  - name: deep\n" +
                              "    model: claude-opus-4-6\n" +
                              "    effort: max\n" +
                              "  - name: balanced\n" +
                              "    model: claude-sonnet-4-6\n" +
                              "    effort: high\n" +
                              "  - name: quick\n" +
                              "    model: claude-haiku-4-5\n" +
                              "    effort: low\n" +
                              "- name: Codex\n" +
                              "  profiles:\n" +
                              "  - name: deep\n" +
                              "    model: gpt-5.4\n" +
                              "    effort: high\n" +
                              "  - name: balanced\n" +
                              "    model: gpt-5.4-mini\n" +
                              "    effort: medium\n" +
                              "  - name: quick\n" +
                              "    model: gpt-5.3-codex\n" +
                              "    effort: low\n" +
                              "- name: Gemini\n" +
                              "  profiles:\n" +
                              "  - name: deep\n" +
                              "    model: gemini-3-flash-preview\n" +
                              "  - name: balanced\n" +
                              "    model: gemini-2.5-flash\n" +
                              "  - name: quick\n" +
                              "    model: gemini-2.5-flash-lite\n" +
                              "projects: []\n" +
                              "verifications: []\n";
            await FileHelper.WriteAllTextAsync(configPath, basicConfig);
        }

        _logger.LogInformation("Config file written");

        // Set environment variable for current session
        _logger.LogInformation("Setting environment variable TENDRIL_HOME={Value}", tendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_HOME", tendrilHome);

        // Persist environment variable across restarts
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Environment.SetEnvironmentVariable("TENDRIL_HOME", tendrilHome, EnvironmentVariableTarget.User);
            }
            else
            {
                // Determine shell rc file
                var shell = Environment.GetEnvironmentVariable("SHELL") ?? "";
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var rcFile = shell.EndsWith("/zsh") ? Path.Combine(home, ".zshrc")
                           : shell.EndsWith("/bash") ? Path.Combine(home, ".bashrc")
                           : Path.Combine(home, ".profile");

                _logger.LogInformation("Persisting TENDRIL_HOME to {RcFile} (shell={Shell})", rcFile, shell);

                var exportLine = $"export TENDRIL_HOME=\"{tendrilHome}\"";

                var content = File.Exists(rcFile) ? await FileHelper.ReadAllTextAsync(rcFile) : "";
                if (!content.Contains(exportLine))
                    await File.AppendAllLinesAsync(rcFile, new[] { "", "# Tendril Home", exportLine });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist TENDRIL_HOME to shell rc file (shell={Shell})",
                Environment.GetEnvironmentVariable("SHELL"));
        }

        _logger.LogInformation("Environment variable persisted (Windows={IsWin})", OperatingSystem.IsWindows());

        // Mark onboarding complete (this reloads config from the file we just wrote)
        _logger.LogInformation("Marking onboarding complete");
        config.CompleteOnboarding(tendrilHome);

        // Add pending verification definitions to global config
        var pendingDefinitions = config.GetPendingVerificationDefinitions();
        if (pendingDefinitions != null)
            foreach (var def in pendingDefinitions)
                if (!config.Settings.Verifications.Any(v => v.Name == def.Name))
                    config.Settings.Verifications.Add(def);

        // Add pending project if one was configured
        var pendingProject = config.GetPendingProject();
        if (pendingProject != null)
        {
            config.Settings.Projects.Add(pendingProject);
            config.SaveSettings();
        }

        _logger.LogInformation("Configuration saved");
    }

    public void StartBackgroundServices()
    {
        _logger.LogInformation("Starting background services (deferred)");
        BackgroundServiceActivator.Start(services, _logger);
    }
}
