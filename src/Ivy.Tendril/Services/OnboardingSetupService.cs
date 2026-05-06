using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class OnboardingSetupService(IConfigService config, IServiceProvider services, ILogger<OnboardingSetupService> logger) : IOnboardingSetupService
{
    private readonly ILogger<OnboardingSetupService> _logger = logger;

    public async Task BootstrapTendrilHomeAsync(string tendrilHome)
    {
        _logger.LogInformation("Creating Tendril directory structure at {Path}", tendrilHome);

        Directory.CreateDirectory(tendrilHome);
        Directory.CreateDirectory(Path.Combine(tendrilHome, "Inbox"));
        var planFolder = Environment.GetEnvironmentVariable("TENDRIL_PLANS")?.Trim() is { Length: > 0 } plans
            ? plans
            : Path.Combine(tendrilHome, "Plans");
        Directory.CreateDirectory(planFolder);
        var counterPath = Path.Combine(planFolder, ".counter");
        if (!File.Exists(counterPath))
            await FileHelper.WriteAllTextAsync(counterPath, "1");
        Directory.CreateDirectory(Path.Combine(tendrilHome, "Trash"));
        Directory.CreateDirectory(Path.Combine(tendrilHome, "Promptwares"));
        if (PromptwareDeployer.IsEmbeddedAvailable())
            PromptwareDeployer.Deploy(Path.Combine(tendrilHome, "Promptwares"));
        Directory.CreateDirectory(Path.Combine(tendrilHome, "Hooks"));

        _logger.LogInformation("Directory structure created");

        _logger.LogInformation("Writing config file to {Path}", Path.Combine(tendrilHome, "config.yaml"));
        var projectDir = Path.GetDirectoryName(System.AppContext.BaseDirectory);
        while (projectDir != null && !File.Exists(Path.Combine(projectDir, "example.config.yaml")))
            projectDir = Path.GetDirectoryName(projectDir);

        var exampleConfigPath = projectDir != null
            ? Path.Combine(projectDir, "example.config.yaml")
            : Path.Combine(System.AppContext.BaseDirectory, "example.config.yaml");

        var configPath = Path.Combine(tendrilHome, "config.yaml");

        if (!File.Exists(configPath))
        {
            if (File.Exists(exampleConfigPath))
            {
                var exampleContent = await FileHelper.ReadAllTextAsync(exampleConfigPath);
                await FileHelper.WriteAllTextAsync(configPath, exampleContent);
            }
            else
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
                                  "    model: gemini-3-flash-preview\n" +
                                  "  - name: quick\n" +
                                  "    model: gemini-3-flash-preview\n" +
                                  "- name: Copilot\n" +
                                  "  profiles:\n" +
                                  "  - name: deep\n" +
                                  "    model: gpt-5.2\n" +
                                  "    effort: high\n" +
                                  "  - name: balanced\n" +
                                  "    model: gpt-5.2\n" +
                                  "    effort: medium\n" +
                                  "  - name: quick\n" +
                                  "    model: gpt-5.2\n" +
                                  "    effort: low\n" +
                                  "projects: []\n" +
                                  "verifications: []\n";
                await FileHelper.WriteAllTextAsync(configPath, basicConfig);
            }
        }

        _logger.LogInformation("Config file written");

        config.ReloadSettings();
        var pendingAgent = config.GetPendingCodingAgent();
        if (!string.IsNullOrEmpty(pendingAgent))
            config.Settings.CodingAgent = pendingAgent;

        _logger.LogInformation("Setting environment variable TENDRIL_HOME={Value}", tendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_HOME", tendrilHome);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Environment.SetEnvironmentVariable("TENDRIL_HOME", tendrilHome, EnvironmentVariableTarget.User);
            }
            else
            {
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
    }

    public Task CommitPendingProjectAsync()
    {
        var pendingDefinitions = config.GetPendingVerificationDefinitions();
        if (pendingDefinitions != null)
            foreach (var def in pendingDefinitions)
                if (!config.Settings.Verifications.Any(v => v.Name == def.Name))
                    config.Settings.Verifications.Add(def);

        var pendingProject = config.GetPendingProject();
        if (pendingProject != null
            && !config.Settings.Projects.Any(p => p.Name.Equals(pendingProject.Name, StringComparison.OrdinalIgnoreCase)))
        {
            config.Settings.Projects.Add(pendingProject);
            config.SaveSettings();
            _logger.LogInformation("Pending project '{Name}' committed", pendingProject.Name);
        }

        return Task.CompletedTask;
    }

    public async Task FinalizeOnboardingAsync()
    {
        var tendrilHome = config.GetPendingTendrilHome();
        if (string.IsNullOrEmpty(tendrilHome))
            throw new InvalidOperationException("Tendril home path not set; cannot finalize onboarding.");

        await CommitPendingProjectAsync();

        _logger.LogInformation("Marking onboarding complete");
        config.CompleteOnboarding(tendrilHome);

        _logger.LogInformation("Configuration saved");
    }

    public Task RemoveProjectVerificationAsync(string projectName, string verificationName)
    {
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
        if (project == null)
            return Task.CompletedTask;

        var removed = project.Verifications.RemoveAll(v =>
            v.Name.Equals(verificationName, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            config.SaveSettings();
            _logger.LogInformation("Removed verification '{Verification}' from project '{Project}'",
                verificationName, projectName);
        }

        return Task.CompletedTask;
    }

    public async Task StartBackgroundServicesAsync()
    {
        _logger.LogInformation("Starting background services (deferred)");
        await BackgroundServiceActivator.StartAsync(services, _logger);
    }
}
