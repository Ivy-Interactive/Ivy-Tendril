using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Claude;

[Collection("Claude")]
public class ClaudeHealthCheckTests
{
    private readonly ClaudeHealthCheck _healthCheck = new();

    [Fact]
    public async Task CheckInstall_FindsClaudeBinary()
    {
        var status = await _healthCheck.CheckInstallAsync();

        Assert.True(status.IsInstalled, "Claude CLI should be installed and on PATH");
        Assert.NotNull(status.BinaryPath);
        Assert.Null(status.Error);
    }

    [Fact]
    public async Task GetVersion_ReturnsNonEmptyString()
    {
        var version = await _healthCheck.GetVersionAsync();

        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }

    [Fact]
    public async Task CheckAuth_ReturnsAuthenticated()
    {
        var result = await _healthCheck.CheckAuthAsync();

        Assert.Equal(AuthStatus.Authenticated, result.Status);
        Assert.Null(result.Error);
    }

    [Fact]
    public void AgentId_IsClaude()
    {
        Assert.Equal(AgentId.Claude, _healthCheck.AgentId);
    }

    [Fact]
    public void BinaryResolver_FindsClaude()
    {
        var path = BinaryResolver.FindOnPath("claude");
        Assert.NotNull(path);
    }

    [Fact]
    public async Task ValidateModel_ValidModel_ReturnsOkOrKnownStatus()
    {
        var result = await _healthCheck.ValidateModelAsync("sonnet");

        // Model validation may succeed or fail depending on plan/quota,
        // but should never return InvalidModel for a real model name
        Assert.NotEqual(ModelValidationStatus.InvalidModel, result.Status);
        Assert.Equal("sonnet", result.Model);
    }

    [Fact]
    public void GetOnboardingInfo_ReturnsCompleteInfo()
    {
        var info = _healthCheck.GetOnboardingInfo();

        Assert.Equal("Claude Code", info.DisplayName);
        Assert.NotEmpty(info.InstallCommand);
        Assert.NotNull(info.AuthCommand);
        Assert.NotNull(info.DocsUrl);
    }

    [Fact]
    public async Task RunAuthFlow_ReturnsFalse_NonInteractive()
    {
        var callbacks = new AuthFlowCallbacks
        {
            OnUrl = _ => Task.CompletedTask,
        };

        var result = await _healthCheck.RunAuthFlowAsync(callbacks);
        Assert.False(result);
    }
}
