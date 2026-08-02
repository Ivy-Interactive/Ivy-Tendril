using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiHealthCheckTests
{
    private readonly GeminiHealthCheck _healthCheck = new();

    [Fact]
    public async Task CheckAuth_WithApiKeyEnvVar_ReturnsAuthenticated()
    {
        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");

            var result = await _healthCheck.CheckAuthAsync();

            Assert.Equal(AuthStatus.Authenticated, result.Status);
            Assert.Equal("api-key", result.AuthMethod);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
        }
    }

    [Fact]
    public void GetCredentialCandidates_ProbesMigratedConfigDirectory()
    {
        const string home = "/home/testuser";

        var candidates = GeminiHealthCheck.GetCredentialCandidates(home);

        Assert.Contains("/home/testuser/.gemini/oauth_creds.json", candidates);
        Assert.Contains("/home/testuser/.gemini/config/oauth_creds.json", candidates);
        Assert.Contains("/home/testuser/.gemini/google_accounts.json", candidates);
        Assert.Contains("/home/testuser/.gemini/config/google_accounts.json", candidates);
        Assert.Contains("/home/testuser/.gemini/settings.json", candidates);
        Assert.Contains("/home/testuser/.gemini/config/settings.json", candidates);
        Assert.Contains("/home/testuser/.gemini/config/config.json", candidates);
    }

    [Fact]
    public void AgentId_IsGemini()
    {
        Assert.Equal(AgentId.Gemini, _healthCheck.AgentId);
    }

    [Fact]
    public void GetOnboardingInfo_ReturnsCompleteInfo()
    {
        var info = _healthCheck.GetOnboardingInfo();

        Assert.Equal("Gemini", info.DisplayName);
        Assert.NotEmpty(info.InstallCommand);
        Assert.NotNull(info.AuthCommand);
        Assert.NotNull(info.DocsUrl);
    }
}
