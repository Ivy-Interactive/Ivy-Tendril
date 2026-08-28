using System.Collections.Generic;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Xunit;

namespace Ivy.Tendril.Test;

public class VaultSecretSanitizerTests
{
    [Theory]
    [InlineData("API_KEY", "sk-1234567890abcdef1234567890", "${API_KEY}")]
    [InlineData("GITHUB_TOKEN", "ghp_1234567890abcdef1234567890abcdef1234", "${GITHUB_TOKEN}")]
    [InlineData("ANTHROPIC_AUTH_TOKEN", "secret-token-val", "${ANTHROPIC_AUTH_TOKEN}")]
    [InlineData("DATABASE_PASSWORD", "SuperSecretPassword123!", "${DATABASE_PASSWORD}")]
    [InlineData("BASE_URL", "https://api.example.com", "https://api.example.com")]
    [InlineData("PORT", "8080", "8080")]
    public void SanitizeEnvValue_ReplacesSensitiveKeys_And_PreservesSafeValues(string key, string input, string expected)
    {
        var result = VaultSecretSanitizer.SanitizeEnvValue(key, input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeEnvValue_PreservesExistingPlaceholders()
    {
        var result = VaultSecretSanitizer.SanitizeEnvValue("API_KEY", "${CUSTOM_KEY}");
        Assert.Equal("${CUSTOM_KEY}", result);
    }

    [Fact]
    public void SanitizeMcpServers_ReplacesSensitiveEnvironmentAndArguments()
    {
        var servers = new List<ProjectMcpServerRef>
        {
            new()
            {
                Name = "playwright",
                Command = "npx",
                Arguments = new List<string> { "-y", "@modelcontextprotocol/server-playwright", "--api-key", "ghp_1234567890abcdef1234567890abcdef1234" },
                Environment = new Dictionary<string, string>
                {
                    ["API_KEY"] = "sk-1234567890abcdef1234567890",
                    ["PORT"] = "3000"
                }
            }
        };

        var sanitized = VaultSecretSanitizer.SanitizeMcpServers(servers);

        Assert.Single(sanitized);
        var s = sanitized[0];
        Assert.Equal("playwright", s.Name);
        Assert.Equal("${API_KEY}", s.Environment["API_KEY"]);
        Assert.Equal("3000", s.Environment["PORT"]);
        Assert.Contains("${API_KEY}", s.Arguments);
    }
}
