namespace Ivy.Tendril.Agents.Helpers;

public static class OpenCodeModelHelper
{
    public static string FormatModel(string? model, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            if (baseUrl != null && baseUrl.Contains("llmproxy.ivy.app")) return "anthropic/claude-opus-5";
            if (baseUrl != null && baseUrl.Contains("api.anthropic.com")) return "anthropic/claude-sonnet-5";
            if (baseUrl != null && (baseUrl.Contains("generativelanguage.googleapis.com") || baseUrl.Contains("gemini") || baseUrl.Contains("google"))) return "openai/gemini-3.7-flash";
            if (baseUrl != null && baseUrl.Contains("api.berget.ai")) return "moonshotai/Kimi-K3";
            return "openai/gpt-5.6-terra";
        }

        var trimmed = model.Trim();
        if (trimmed.Contains('/')) return trimmed;

        if (trimmed.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("anthropic.", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("haiku", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("sonnet", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("opus", StringComparison.OrdinalIgnoreCase))
        {
            return $"anthropic/{trimmed}";
        }

        if (trimmed.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("o1-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("o3-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("o4-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("codex", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase))
        {
            return $"openai/{trimmed}";
        }

        if (trimmed.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("google.", StringComparison.OrdinalIgnoreCase))
        {
            if (baseUrl != null && baseUrl.Contains("api.anthropic.com")) return $"anthropic/{trimmed}";
            return $"openai/{trimmed}";
        }

        if (baseUrl != null && baseUrl.Contains("api.anthropic.com"))
        {
            return $"anthropic/{trimmed}";
        }

        return $"openai/{trimmed}";
    }
}
