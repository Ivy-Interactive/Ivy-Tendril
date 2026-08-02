using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.OpenCode;

public class OpenCodeHealthCheckTests
{
    private readonly OpenCodeHealthCheck _healthCheck = new();

    [Fact]
    public void ParseAuthList_WithEnvironmentCredential_ReturnsAuthenticated()
    {
        const string output = @"
Credentials ~/.local/share/opencode/auth.json
  0 credentials
Environment
  Amazon Bedrock  AWS_BEARER_TOKEN_BEDROCK
  1 environment variable
";

        var result = OpenCodeHealthCheck.ParseAuthList(output);

        Assert.Equal(AuthStatus.Authenticated, result.Status);
        Assert.Equal("environment", result.AuthMethod);
    }

    [Fact]
    public void ParseAuthList_WithNoCredentials_ReturnsNotAuthenticated()
    {
        const string output = @"
Credentials ~/.local/share/opencode/auth.json
  0 credentials
";

        var result = OpenCodeHealthCheck.ParseAuthList(output);

        Assert.Equal(AuthStatus.NotAuthenticated, result.Status);
    }

    [Fact]
    public void ParseAuthList_WithFileCredentials_ReturnsAuthenticated()
    {
        const string output = @"
Credentials ~/.local/share/opencode/auth.json
  2 credentials
";

        var result = OpenCodeHealthCheck.ParseAuthList(output);

        Assert.Equal(AuthStatus.Authenticated, result.Status);
        Assert.Equal("auth-file", result.AuthMethod);
    }

    [Fact]
    public void ParseAuthList_WithAnsiEscapes_StripsAndParses()
    {
        const string output = "\x1b[90mCredentials\x1b[0m ~/.local/share/opencode/auth.json\n  0 credentials\n\x1b[90mEnvironment\x1b[0m\n  Amazon Bedrock  AWS_BEARER_TOKEN_BEDROCK\n  1 environment variable\n";

        var result = OpenCodeHealthCheck.ParseAuthList(output);

        Assert.Equal(AuthStatus.Authenticated, result.Status);
        Assert.Equal("environment", result.AuthMethod);
    }

    [Fact]
    public void AgentId_IsOpenCode()
    {
        Assert.Equal(AgentId.OpenCode, _healthCheck.AgentId);
    }

    [Fact]
    public void GetOnboardingInfo_ReturnsCompleteInfo()
    {
        var info = _healthCheck.GetOnboardingInfo();

        Assert.Equal("OpenCode", info.DisplayName);
        Assert.NotEmpty(info.InstallCommand);
        Assert.NotNull(info.AuthCommand);
        Assert.NotNull(info.DocsUrl);
    }
}
