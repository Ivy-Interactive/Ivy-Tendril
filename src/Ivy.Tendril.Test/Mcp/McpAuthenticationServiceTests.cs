using Ivy.Tendril.Mcp;
using Xunit;

namespace Ivy.Tendril.Test.Mcp;

public class McpAuthenticationServiceTests : IDisposable
{
    private readonly string? _originalToken;

    public McpAuthenticationServiceTests()
    {
        _originalToken = Environment.GetEnvironmentVariable("TENDRIL_MCP_TOKEN");
    }

    public void Dispose()
    {
        // Restore original token state
        if (_originalToken == null)
            Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        else
            Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", _originalToken);
    }

    [Fact]
    public void NoTokenConfigured_AllRequestsAllowed()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        var authService = new McpAuthenticationService();

        // Act & Assert
        Assert.False(authService.IsAuthenticationEnabled);
        Assert.True(authService.ValidateToken(null));
        Assert.True(authService.ValidateToken(""));
        Assert.True(authService.ValidateToken("any-random-token"));
        Assert.True(authService.ValidateEnvironmentToken());
    }

    [Fact]
    public void TokenConfigured_ValidToken_RequestAllowed()
    {
        // Arrange
        var testToken = "test-secure-token-123";
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", testToken);
        var authService = new McpAuthenticationService();

        // Act & Assert
        Assert.True(authService.IsAuthenticationEnabled);
        Assert.True(authService.ValidateToken(testToken));
        Assert.True(authService.ValidateEnvironmentToken());
    }

    [Fact]
    public void TokenConfigured_InvalidToken_RequestRejected()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "correct-token");
        var authService = new McpAuthenticationService();

        // Act & Assert
        Assert.True(authService.IsAuthenticationEnabled);
        Assert.False(authService.ValidateToken("wrong-token"));
        Assert.False(authService.ValidateToken(""));
        Assert.False(authService.ValidateToken("totally-different-token"));
    }

    [Fact]
    public void TokenConfigured_NoTokenProvided_RequestRejected()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "required-token");
        var authService = new McpAuthenticationService();

        // Act & Assert
        Assert.True(authService.IsAuthenticationEnabled);
        Assert.False(authService.ValidateToken(null));
        Assert.False(authService.ValidateToken(""));
        Assert.False(authService.ValidateToken("   "));
    }

    [Fact]
    public void TokenConfigured_QuotedToken_HandledCorrectly()
    {
        // Arrange
        var plainToken = "unquoted-token";
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", $"\"{plainToken}\"");
        var authService = new McpAuthenticationService();

        // Act & Assert - both quoted and unquoted should work since service strips quotes
        Assert.True(authService.IsAuthenticationEnabled);
        Assert.True(authService.ValidateEnvironmentToken());
    }

    [Fact]
    public void TokenConfigured_CaseSensitive()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "CaseSensitiveToken");
        var authService = new McpAuthenticationService();

        // Act & Assert
        Assert.True(authService.ValidateToken("CaseSensitiveToken"));
        Assert.False(authService.ValidateToken("casesensitivetoken"));
        Assert.False(authService.ValidateToken("CASESENSITIVETOKEN"));
    }

    [Fact]
    public void EnvironmentTokenValidation_AfterCreation()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "initial-token");
        var authService = new McpAuthenticationService();

        // Act - Change environment variable after service creation
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "changed-token");

        // Assert - Service should still validate against the original token at construction time
        Assert.True(authService.ValidateToken("initial-token"));
        Assert.False(authService.ValidateToken("changed-token"));
    }
}
