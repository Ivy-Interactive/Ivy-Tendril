namespace Ivy.Tendril.Mcp.Tools;

/// <summary>
/// Base class for MCP tool classes requiring authentication.
/// Provides centralized authentication checking via ExecuteAuthenticated.
/// </summary>
public abstract class AuthenticatedToolBase
{
    private readonly McpAuthenticationService _authService;

    protected AuthenticatedToolBase(McpAuthenticationService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Executes an action after validating authentication.
    /// Returns an error message if authentication fails.
    /// </summary>
    protected string ExecuteAuthenticated(Func<string> action)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        return action();
    }
}
