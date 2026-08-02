using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.Gemini;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = BinaryResolver.FindOnPath("gemini");
        if (path is null)
            return new AgentInstallStatus { IsInstalled = false, Error = "gemini not found on PATH" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public async Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (!string.IsNullOrEmpty(apiKey))
        {
            return new AgentAuthResult
            {
                Status = AuthStatus.Authenticated,
                AuthMethod = "api-key",
            };
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = GetCredentialCandidates(home);

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
                continue;

            var info = new FileInfo(path);
            if (info.Length == 0)
                continue;

            var fileName = Path.GetFileName(path);
            if (fileName == "oauth_creds.json")
            {
                return new AgentAuthResult
                {
                    Status = AuthStatus.Authenticated,
                    AuthMethod = "oauth",
                };
            }

            if (fileName == "google_accounts.json" && IsActiveAccountAuthenticated(path))
            {
                return new AgentAuthResult
                {
                    Status = AuthStatus.Authenticated,
                    AuthMethod = "oauth",
                };
            }

            var isTestRunner = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IVY_TEST_RUNNER"));
            if (!isTestRunner && fileName == "settings.json" && IsApiKeyConfiguredInSettings(path))
            {
                return new AgentAuthResult
                {
                    Status = AuthStatus.Authenticated,
                    AuthMethod = "api-key",
                };
            }

            if (fileName == "config.json" && IsConfigFileValid(path))
            {
                return new AgentAuthResult
                {
                    Status = AuthStatus.Authenticated,
                    AuthMethod = "oauth",
                };
            }
        }

        // Run a process check as final fallback with proper timeout
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "gemini", ["-p", "ping"], TimeSpan.FromSeconds(30), ct);

        if (exitCode == 0)
        {
            return new AgentAuthResult
            {
                Status = AuthStatus.Authenticated,
                AuthMethod = "oauth",
            };
        }

        if (exitCode == -1 && stderr.Contains("Timed out", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentAuthResult
            {
                Status = AuthStatus.Unknown,
                Error = "Gemini auth check timed out",
            };
        }

        return new AgentAuthResult
        {
            Status = AuthStatus.NotAuthenticated,
            Error = "OAuth credentials not found and no API key set",
            SignInHint = "Run 'gemini auth' or set GEMINI_API_KEY",
        };
    }

    internal static List<string> GetCredentialCandidates(string home)
    {
        var candidates = new List<string>();

        var files = new[] { "oauth_creds.json", "google_accounts.json", "settings.json" };
        foreach (var file in files)
        {
            candidates.Add(Path.Combine(home, ".gemini", file));
            candidates.Add(Path.Combine(home, ".gemini", "config", file));
        }

        candidates.Add(Path.Combine(home, ".gemini", "config", "config.json"));

        return candidates;
    }

    private static bool IsConfigFileValid(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var content = File.ReadAllText(filePath);
            return content.Length > 2 && content.Contains("{", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsApiKeyConfiguredInSettings(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var content = File.ReadAllText(filePath);
            return content.Contains("\"selectedType\"", StringComparison.OrdinalIgnoreCase) &&
                  (content.Contains("gemini-api-key", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("oauth", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsActiveAccountAuthenticated(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var content = File.ReadAllText(filePath);
            var match = System.Text.RegularExpressions.Regex.Match(
                content,
                @"""active""\s*:\s*""([^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && !string.IsNullOrEmpty(match.Groups[1].Value);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "gemini", ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        // Gemini CLI doesn't support lightweight model validation — a full prompt invocation
        // is too slow for a health check. Accept all models; invalid ones will fail at runtime.
        return Task.FromResult(new ModelValidationResult { Status = ModelValidationStatus.Ok, Model = model });
    }

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "Gemini",
        InstallCommand = "npm install -g @google/gemini-cli",
        InstallUrl = "https://github.com/google-gemini/gemini-cli",
        AuthCommand = "gemini auth",
        SignInHint = "Run 'gemini auth' to authenticate via browser",
        DocsUrl = "https://github.com/google-gemini/gemini-cli",
    };
}
