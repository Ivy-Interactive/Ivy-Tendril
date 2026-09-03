using System.Collections.Frozen;

namespace Ivy.Tendril.Agents.Helpers;

public enum ModelProviderKind
{
    Generic,
    Ivy,
    Anthropic,
    OpenAi,
    Google,
    Berget,
    OpenCode,
}

public enum ModelProfileKind
{
    Deep,
    Balanced,
    Quick,
}

public static class ModelProfilePriorities
{
    private static readonly FrozenDictionary<(ModelProviderKind, ModelProfileKind), IReadOnlyList<string>> PriorityMap =
        new Dictionary<(ModelProviderKind, ModelProfileKind), IReadOnlyList<string>>
        {
            // === Ivy Proxy ===
            [(ModelProviderKind.Ivy, ModelProfileKind.Deep)] =
            [
                "claude-fable-5", "claude-opus-5-1", "claude-opus-5", "claude-opus-4-8", "claude-opus-4-7", "claude-opus-4-6", "claude-opus-4-5",
                "claude-opus-4", "opus", "gpt-5.6-sol", "sol", "claude-sonnet-5", "gemini-3.7-flash"
            ],
            [(ModelProviderKind.Ivy, ModelProfileKind.Balanced)] =
            [
                "gemini-3.7-flash", "gemini-3.6-flash", "claude-sonnet-5-1", "claude-5.1", "claude-sonnet-5",
                "claude-sonnet-4-6", "gpt-5.6-terra", "terra", "gemini-2.5-flash"
            ],
            [(ModelProviderKind.Ivy, ModelProfileKind.Quick)] =
            [
                "gemini-3.7-flash", "gemini-3.6-flash", "gemini-2.5-flash", "claude-haiku-5-1", "claude-haiku-4-5",
                "claude-haiku-5", "gpt-5.6-luna", "luna", "gpt-4o-mini"
            ],

            // === Anthropic Direct ===
            [(ModelProviderKind.Anthropic, ModelProfileKind.Deep)] =
            [
                "claude-fable-5", "claude-opus-5-1", "claude-opus-5", "claude-opus-4-8", "claude-opus-4-7", "claude-opus-4-6", "claude-opus-4-5",
                "claude-opus-4", "opus", "claude-sonnet-5"
            ],
            [(ModelProviderKind.Anthropic, ModelProfileKind.Balanced)] =
            [
                "claude-sonnet-5-1", "claude-5.1", "claude-sonnet-5", "claude-sonnet-4-6", "claude-sonnet-4-5", "claude-sonnet-4", "sonnet", "claude-haiku-4-5"
            ],
            [(ModelProviderKind.Anthropic, ModelProfileKind.Quick)] =
            [
                "claude-haiku-5-1", "claude-haiku-4-5", "claude-haiku-5", "claude-3-5-haiku", "claude-haiku-4", "haiku"
            ],

            // === Google / Gemini ===
            [(ModelProviderKind.Google, ModelProfileKind.Deep)] =
            [
                "gemini-3.7-flash", "gemini-3.7-pro", "gemini-3.6-flash", "gemini-3.6-pro", "gemini-3.1-pro",
                "gemini-3-pro", "gemini-2.5-pro", "gemini-2.5-flash"
            ],
            [(ModelProviderKind.Google, ModelProfileKind.Balanced)] =
            [
                "gemini-3.7-flash", "gemini-3.6-flash", "gemini-2.5-flash", "gemini-2.0-flash"
            ],
            [(ModelProviderKind.Google, ModelProfileKind.Quick)] =
            [
                "gemini-3.7-flash", "gemini-3.6-flash", "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.0-flash"
            ],

            // === OpenAI Direct ===
            [(ModelProviderKind.OpenAi, ModelProfileKind.Deep)] =
            [
                "gpt-5.6-sol", "gpt-5.6", "gpt-5.5", "gpt-5", "sol", "o3", "o1", "gpt-4o"
            ],
            [(ModelProviderKind.OpenAi, ModelProfileKind.Balanced)] =
            [
                "gpt-5.6-terra", "gpt-5.6", "gpt-5", "terra", "gpt-4o"
            ],
            [(ModelProviderKind.OpenAi, ModelProfileKind.Quick)] =
            [
                "gpt-5.6-luna", "luna", "gpt-4o-mini", "gpt-4.1-mini"
            ],

            // === Berget AI ===
            [(ModelProviderKind.Berget, ModelProfileKind.Deep)] =
            [
                "moonshotai/Kimi-K3", "kimi-k3", "kimi"
            ],
            [(ModelProviderKind.Berget, ModelProfileKind.Balanced)] =
            [
                "moonshotai/Kimi-K3", "kimi-k3", "kimi"
            ],
            [(ModelProviderKind.Berget, ModelProfileKind.Quick)] =
            [
                "moonshotai/Kimi-K3", "kimi-k3", "kimi"
            ],

            // === OpenCode ===
            [(ModelProviderKind.OpenCode, ModelProfileKind.Deep)] =
            [
                "claude-fable-5", "claude-opus-5-1", "claude-opus-5", "gpt-5.6-sol", "gemini-3.7-flash", "claude-sonnet-5"
            ],
            [(ModelProviderKind.OpenCode, ModelProfileKind.Balanced)] =
            [
                "gemini-3.7-flash", "claude-sonnet-5-1", "claude-5.1", "claude-sonnet-5", "gpt-5.6-terra"
            ],
            [(ModelProviderKind.OpenCode, ModelProfileKind.Quick)] =
            [
                "gemini-3.7-flash", "claude-haiku-4-5", "gpt-5.6-luna"
            ],

            // === Generic / Fallback ===
            [(ModelProviderKind.Generic, ModelProfileKind.Deep)] =
            [
                "gpt-5.6-sol", "claude-fable-5", "claude-opus-5-1", "claude-opus-5", "gemini-3.7-flash", "claude-sonnet-5", "gpt-4o"
            ],
            [(ModelProviderKind.Generic, ModelProfileKind.Balanced)] =
            [
                "gpt-5.6-terra", "gemini-3.7-flash", "claude-sonnet-5-1", "claude-5.1", "claude-sonnet-5", "gpt-4o"
            ],
            [(ModelProviderKind.Generic, ModelProfileKind.Quick)] =
            [
                "gpt-5.6-luna", "gemini-3.7-flash", "claude-haiku-4-5", "gpt-4o-mini"
            ],
        }.ToFrozenDictionary();

    public static IReadOnlyList<string> GetPrioritizedCandidates(ModelProviderKind provider, ModelProfileKind profile)
    {
        if (PriorityMap.TryGetValue((provider, profile), out var list))
        {
            return list;
        }

        if (PriorityMap.TryGetValue((ModelProviderKind.Generic, profile), out var genericList))
        {
            return genericList;
        }

        return ["default"];
    }

    public static string GetDefaultModel(ModelProviderKind provider, ModelProfileKind profile) =>
        GetPrioritizedCandidates(provider, profile)[0];
}
