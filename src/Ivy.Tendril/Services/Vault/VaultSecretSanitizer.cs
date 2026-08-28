using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services.Vault;

public static class VaultSecretSanitizer
{
    private static readonly HashSet<string> SensitiveKeyWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "key", "token", "secret", "password", "auth", "credential", "pat", "bearer", "apikey", "api_key"
    };

    private static readonly Regex SecretPatternRegex = new(
        @"(ghp_[a-zA-Z0-9]{36}|github_pat_[a-zA-Z0-9_]{82}|sk-[a-zA-Z0-9]{20,}|Bearer\s+[a-zA-Z0-9_\-\.]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Sanitizes an environment variable value for export.
    /// If the value is already in placeholder format (${VAR_NAME}), it is preserved.
    /// Otherwise, if the key name or value looks sensitive, it is converted to ${KEY_NAME}.
    /// </summary>
    public static string SanitizeEnvValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (value.StartsWith("${") && value.EndsWith("}")) return value;

        if (IsSensitiveKey(key) || SecretPatternRegex.IsMatch(value))
        {
            var placeholderVar = NormalizeEnvVarName(key);
            return $"${{{placeholderVar}}}";
        }

        return value;
    }

    /// <summary>
    /// Sanitizes a dictionary of environment variables.
    /// </summary>
    public static Dictionary<string, string> SanitizeEnvironment(Dictionary<string, string>? env)
    {
        var result = new Dictionary<string, string>();
        if (env == null) return result;

        foreach (var (k, v) in env)
        {
            result[k] = SanitizeEnvValue(k, v);
        }

        return result;
    }

    /// <summary>
    /// Sanitizes MCP server references for export.
    /// </summary>
    public static List<ProjectMcpServerRef> SanitizeMcpServers(List<ProjectMcpServerRef>? servers)
    {
        if (servers == null) return new();
        var sanitized = new List<ProjectMcpServerRef>();

        foreach (var s in servers)
        {
            var cleanArgs = new List<string>();
            foreach (var arg in s.Arguments)
            {
                cleanArgs.Add(SecretPatternRegex.Replace(arg, "${API_KEY}"));
            }

            sanitized.Add(s with
            {
                Arguments = cleanArgs,
                Environment = SanitizeEnvironment(s.Environment)
            });
        }

        return sanitized;
    }

    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        foreach (var word in SensitiveKeyWords)
        {
            if (key.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string NormalizeEnvVarName(string key)
    {
        var upper = key.ToUpperInvariant().Replace('-', '_').Replace('.', '_');
        return Regex.Replace(upper, @"[^A-Z0-9_]", "");
    }
}
