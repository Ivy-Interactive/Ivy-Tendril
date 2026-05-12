namespace Ivy.Tendril.Services.Agents;

public static class OutputNormalizerFactory
{
    public static IOutputNormalizer Create(string provider) => provider.ToLowerInvariant() switch
    {
        "claude" => new ClaudeOutputNormalizer(),
        "gemini" => new GeminiOutputNormalizer(),
        "codex" => new CodexOutputNormalizer(),
        "opencode" => new OpenCodeOutputNormalizer(),
        "copilot" => new CopilotOutputNormalizer(),
        _ => new ClaudeOutputNormalizer()
    };
}
