using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Mcp;

/// <summary>
/// Provides optional bearer token authentication for the Tendril MCP server.
/// </summary>
public sealed class McpAuthenticationService
{
    private readonly string? _expectedTokenHash;
    private readonly bool _authenticationEnabled;
    private readonly ILogger<McpAuthenticationService> _logger;

    public McpAuthenticationService(ILogger<McpAuthenticationService> logger)
    {
        _logger = logger;
        var token = GetTokenFromEnvironment();

        if (string.IsNullOrWhiteSpace(token))
        {
            _authenticationEnabled = false;
            _expectedTokenHash = null;
            _logger.LogInformation("Authentication disabled - TENDRIL_MCP_TOKEN not set");
        }
        else
        {
            _authenticationEnabled = true;
            _expectedTokenHash = HashToken(token);
            _logger.LogInformation("Authentication enabled - token validation active");
        }
    }

    /// <summary>
    /// Gets whether authentication is enabled.
    /// </summary>
    public bool IsAuthenticationEnabled => _authenticationEnabled;

    /// <summary>
    /// Validates the provided token against the configured token.
    /// </summary>
    /// <param name="providedToken">The token to validate</param>
    /// <returns>True if authentication passes or is disabled, false if validation fails</returns>
    public bool ValidateToken(string? providedToken)
    {
        // If auth is disabled, all requests are allowed
        if (!_authenticationEnabled)
            return true;

        // If auth is enabled but no token provided, reject
        if (string.IsNullOrWhiteSpace(providedToken))
        {
            _logger.LogWarning("Authentication failed - no token provided");
            return false;
        }

        // Validate token using constant-time comparison via hash comparison
        var providedHash = HashToken(providedToken);
        var isValid = string.Equals(_expectedTokenHash, providedHash, StringComparison.Ordinal);

        if (!isValid)
        {
            _logger.LogWarning("Authentication failed - invalid token");
        }

        return isValid;
    }

    /// <summary>
    /// Validates that the client has the correct environment token set.
    /// Since both server and client run with shared environment, this checks
    /// if TENDRIL_MCP_TOKEN is available in the current process.
    /// </summary>
    public bool ValidateEnvironmentToken()
    {
        if (!_authenticationEnabled)
            return true;

        var token = GetTokenFromEnvironment();
        return ValidateToken(token);
    }

    private static string? GetTokenFromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable("TENDRIL_MCP_TOKEN")?.Trim();

        // Handle quoted values
        if (!string.IsNullOrEmpty(token) && token.StartsWith('"') && token.EndsWith('"'))
            token = token[1..^1];

        return string.IsNullOrEmpty(token) ? null : token;
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
