namespace Ivy.Tendril.Mcp.Tools;

public abstract class AuthenticatedToolBase
{
    private readonly McpAuthenticationService _authService;

    protected AuthenticatedToolBase(McpAuthenticationService authService)
    {
        _authService = authService;
    }

    protected string ExecuteAuthenticated(Func<string> action)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
