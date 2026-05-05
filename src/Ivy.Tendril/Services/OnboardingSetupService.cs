using System.Diagnostics;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class OnboardingSetupService(IConfigService config, IServiceProvider services, ILogger<OnboardingSetupService> logger) : IOnboardingSetupService
{
    private const string GitignoreMarkerFileName = ".gitignore-configured";

    private static readonly string[] OsMetadataPatterns =
    [
        ".DS_Store",
        "._*",
        "Thumbs.db",
        "desktop.ini"
    ];

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

        _logger.LogInformation("Config file written");

        // Configure global gitignore for OS metadata files
        await EnsureGlobalGitignoreAsync(tendrilHome);

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

    public async Task StartBackgroundServicesAsync()
    {
        _logger.LogInformation("Starting background services (deferred)");
        await BackgroundServiceActivator.StartAsync(services, _logger);
    }

    /// <summary>
    ///     Ensures a global gitignore is configured with OS metadata patterns (.DS_Store, Thumbs.db, etc.).
    ///     Called during onboarding and as a one-time migration for existing installations.
    /// </summary>
    internal async Task EnsureGlobalGitignoreAsync(string tendrilHome)
    {
        try
        {
            var gitignorePath = await ResolveGlobalGitignorePathAsync();
            var dir = Path.GetDirectoryName(gitignorePath);
            if (dir != null)
                Directory.CreateDirectory(dir);

            var existingContent = File.Exists(gitignorePath)
                ? await File.ReadAllTextAsync(gitignorePath)
                : "";

            var missingPatterns = OsMetadataPatterns
                .Where(p => !ContainsPattern(existingContent, p))
                .ToList();

            if (missingPatterns.Count == 0)
            {
                _logger.LogInformation("Global gitignore at {Path} already contains all OS metadata patterns", gitignorePath);
            }
            else
            {
                var block = "\n# OS metadata (added by Tendril)\n" + string.Join("\n", missingPatterns) + "\n";
                await File.AppendAllTextAsync(gitignorePath, block);
                _logger.LogInformation("Appended {Count} OS metadata pattern(s) to global gitignore at {Path}",
                    missingPatterns.Count, gitignorePath);
            }

            // Write marker file so startup migration doesn't re-run
            var markerPath = Path.Combine(tendrilHome, GitignoreMarkerFileName);
            if (!File.Exists(markerPath))
                await File.WriteAllTextAsync(markerPath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure global gitignore");
        }
    }

    /// <summary>
    ///     One-time startup migration: ensures global gitignore is configured for existing installations
    ///     that completed onboarding before this feature was added.
    /// </summary>
    public async Task EnsureGlobalGitignoreOnStartupAsync(string tendrilHome)
    {
        var markerPath = Path.Combine(tendrilHome, GitignoreMarkerFileName);
        if (File.Exists(markerPath))
            return;

        _logger.LogInformation("Running one-time global gitignore migration for existing installation");
        await EnsureGlobalGitignoreAsync(tendrilHome);
    }

    private static async Task<string> ResolveGlobalGitignorePathAsync()
    {
        // Check if the user has a custom core.excludesFile configured
        var customPath = await GetGitConfigValueAsync("core.excludesFile");
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            // Expand ~ to home directory
            if (customPath.StartsWith('~'))
                customPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    customPath[2..]);
            return customPath;
        }

        // Fall back to XDG default: ~/.config/git/ignore
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "git", "ignore");
    }

    private static async Task<string?> GetGitConfigValueAsync(string key)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"config --global {key}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsPattern(string content, string pattern)
    {
        // Check each line for an exact match (ignoring comments and whitespace)
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed == pattern)
                return true;
        }
        return false;
    }
}
