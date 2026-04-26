using Ivy.Tendril.Mcp;
using Ivy.Tendril.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Mcp;

public class AuthenticatedToolBaseTests : IDisposable
{
    private readonly string? _originalToken;

    public AuthenticatedToolBaseTests()
    {
        _originalToken = Environment.GetEnvironmentVariable("TENDRIL_MCP_TOKEN");
    }

    public void Dispose()
    {
        if (_originalToken == null)
            Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        else
            Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", _originalToken);
    }

    private class TestAuthenticatedTool : AuthenticatedToolBase
    {
        public TestAuthenticatedTool(McpAuthenticationService authService) : base(authService)
        {
        }

        public string TestMethod() => ExecuteAuthenticated(() => "Success");

        public string TestMethodWithException() => ExecuteAuthenticated(() =>
        {
            throw new InvalidOperationException("Test error");
        });
    }

    [Fact]
    public void ExecuteAuthenticated_NoAuthConfigured_AllowsAccess()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        var authService = new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance);
        var tool = new TestAuthenticatedTool(authService);

        // Act
        var result = tool.TestMethod();

        // Assert
        Assert.Equal("Success", result);
    }

    [Fact]
    public void ExecuteAuthenticated_AuthConfigured_ValidToken_AllowsAccess()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "test-token");
        var authService = new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance);
        var tool = new TestAuthenticatedTool(authService);

        // Act
        var result = tool.TestMethod();

        // Assert
        Assert.Equal("Success", result);
    }

    [Fact]
    public void ExecuteAuthenticated_AuthConfigured_InvalidToken_ReturnsError()
    {
        // Arrange - create service with token, then clear environment token
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "valid-token");
        var authService = new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        var tool = new TestAuthenticatedTool(authService);

        // Act
        var result = tool.TestMethod();

        // Assert
        Assert.Equal("Error: Authentication failed. Access denied.", result);
    }

    [Fact]
    public void ExecuteAuthenticated_AuthFails_DoesNotExecuteAction()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "valid-token");
        var authService = new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        var tool = new TestAuthenticatedTool(authService);

        // Act - method throws exception, but auth should fail first
        var result = tool.TestMethodWithException();

        // Assert - should get auth error, not exception
        Assert.Equal("Error: Authentication failed. Access denied.", result);
    }

    [Fact]
    public void ExecuteAuthenticated_ErrorMessageConsistent()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", "token");
        var authService = new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);
        var tool = new TestAuthenticatedTool(authService);

        // Act
        var result = tool.TestMethod();

        // Assert - verify exact error message (regression test for consistency)
        Assert.Equal("Error: Authentication failed. Access denied.", result);
    }
}
