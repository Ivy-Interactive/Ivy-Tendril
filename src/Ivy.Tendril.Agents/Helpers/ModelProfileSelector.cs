using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Helpers;

public static class ModelProfileSelector
{
    public static (string deep, string balanced, string quick) SelectDefaults(
        IReadOnlyList<ModelInfo>? availableModels,
        bool isIvy = false,
        bool isAnthropic = false,
        bool isBerget = false,
        bool isGoogle = false,
        bool isOpenAi = false)
    {
        if (availableModels == null || availableModels.Count == 0)
        {
            var fallbackDeep = isIvy || isAnthropic ? "claude-opus-5" : (isBerget ? "moonshotai/Kimi-K3" : (isGoogle ? "gemini-3.7-flash" : "gpt-5.6-sol"));
            var fallbackBalanced = isIvy || isGoogle ? "gemini-3.7-flash" : (isAnthropic ? "claude-sonnet-5" : (isBerget ? "moonshotai/Kimi-K3" : "gpt-5.6-terra"));
            var fallbackQuick = isIvy || isGoogle ? "gemini-3.7-flash" : (isAnthropic ? "claude-haiku-5" : (isBerget ? "moonshotai/Kimi-K3" : "gpt-5.6-luna"));
            return (fallbackDeep, fallbackBalanced, fallbackQuick);
        }

        var modelIds = availableModels.Select(m => m.Id).ToList();

        // 1. Deep Profile
        // Prioritize Opus 5, other Opus versions, Sol / flagship reasoning models, Sonnet 5, etc.
        var deep = FindFirstMatchingModel(modelIds,
            // Opus 5 & variations
            id => MatchesModel(id, "opus-5") || MatchesModel(id, "opus5"),
            id => MatchesModel(id, "opus-4-8") || MatchesModel(id, "opus-4.8"),
            id => MatchesModel(id, "opus-4-7") || MatchesModel(id, "opus-4.7"),
            id => MatchesModel(id, "opus-4-6") || MatchesModel(id, "opus-4.6"),
            id => MatchesModel(id, "opus-4-5") || MatchesModel(id, "opus-4.5"),
            id => MatchesModel(id, "opus-4") || MatchesModel(id, "opus4"),
            id => MatchesModel(id, "opus"),
            // Sol / OpenAI Flagship
            id => MatchesModel(id, "gpt-5.6-sol") || MatchesModel(id, "sol"),
            id => MatchesModel(id, "gpt-5.6"),
            id => MatchesModel(id, "gpt-5.5"),
            id => MatchesModel(id, "gpt-5"),
            id => MatchesModel(id, "o3"),
            id => MatchesModel(id, "o1"),
            // Sonnet 5
            id => MatchesModel(id, "sonnet-5") || MatchesModel(id, "sonnet5"),
            id => MatchesModel(id, "sonnet"),
            // Moonshot Kimi K3
            id => MatchesModel(id, "kimi-k3") || MatchesModel(id, "kimi"),
            // Gemini Pro Flagships
            id => MatchesModel(id, "gemini-3.7-pro") || MatchesModel(id, "gemini-3.6-pro") || MatchesModel(id, "gemini-3.1-pro"),
            id => MatchesModel(id, "gpt-4o") && !MatchesModel(id, "mini")
        ) ?? modelIds.FirstOrDefault() ?? "";

        // 2. Balanced Profile
        // Prioritize Gemini 3.7 (flash/pro), Gemini 3.6 (flash/pro), Sonnet 5, Terra, Gemini 3.5, etc.
        var balanced = FindFirstMatchingModel(modelIds,
            // Gemini 3.7
            id => MatchesModel(id, "gemini-3.7-flash") || MatchesModel(id, "gemini-3-7-flash") || MatchesModel(id, "gemini-3.7") || MatchesModel(id, "gemini-3-7"),
            // Gemini 3.6
            id => MatchesModel(id, "gemini-3.6-flash") || MatchesModel(id, "gemini-3-6-flash") || MatchesModel(id, "gemini-3.6") || MatchesModel(id, "gemini-3-6"),
            // Gemini 3.5 / 3.0 / other 3.x
            id => (MatchesModel(id, "gemini-3") || MatchesModel(id, "gemini-3.")) && MatchesModel(id, "flash"),
            // Sonnet 5 & variations
            id => MatchesModel(id, "sonnet-5") || MatchesModel(id, "sonnet5"),
            id => MatchesModel(id, "sonnet-4-5") || MatchesModel(id, "sonnet-4.5") || MatchesModel(id, "sonnet-4") || MatchesModel(id, "sonnet"),
            // Terra
            id => MatchesModel(id, "gpt-5.6-terra") || MatchesModel(id, "terra"),
            id => MatchesModel(id, "gpt-5.6"),
            // Gemini 2.5 (prefer standard Flash over Flash-Lite)
            id => MatchesModel(id, "gemini-2.5-flash") && !MatchesModel(id, "lite"),
            id => MatchesModel(id, "gemini-2.5"),
            id => MatchesModel(id, "gemini-2.0") || MatchesModel(id, "gemini-2-0"),
            id => MatchesModel(id, "gemini") && MatchesModel(id, "flash"),
            // Moonshot Kimi
            id => MatchesModel(id, "kimi"),
            // GPT-4o
            id => MatchesModel(id, "gpt-4o") && !MatchesModel(id, "mini")
        ) ?? modelIds.ElementAtOrDefault(1) ?? modelIds.FirstOrDefault() ?? "";

        // 3. Quick Profile
        // Prioritize Gemini 3.7 / 3.6 Flash / Flash-Lite, Gemini 2.5 / 2.0 Flash, Haiku 5, Luna, Mini
        var quick = FindFirstMatchingModel(modelIds,
            // Gemini 3.7 / 3.6 Flash & Flash-Lite
            id => (MatchesModel(id, "gemini-3.7") || MatchesModel(id, "gemini-3-7")) && MatchesModel(id, "flash"),
            id => (MatchesModel(id, "gemini-3.6") || MatchesModel(id, "gemini-3-6")) && MatchesModel(id, "flash"),
            id => MatchesModel(id, "gemini-3.7") || MatchesModel(id, "gemini-3.6"),
            // Gemini 2.5 / 2.0 Flash & Lite
            id => MatchesModel(id, "gemini-2.5-flash") || MatchesModel(id, "gemini-2.0-flash"),
            id => MatchesModel(id, "gemini") && MatchesModel(id, "flash"),
            id => MatchesModel(id, "gemini") && MatchesModel(id, "lite"),
            // Claude Haiku 5 & variations
            id => MatchesModel(id, "haiku-5") || MatchesModel(id, "haiku5"),
            id => MatchesModel(id, "haiku-4-5") || MatchesModel(id, "haiku-4") || MatchesModel(id, "haiku"),
            // OpenAI Luna & Mini
            id => MatchesModel(id, "gpt-5.6-luna") || MatchesModel(id, "luna"),
            id => MatchesModel(id, "gpt-4o-mini") || MatchesModel(id, "gpt-4.1-mini") || MatchesModel(id, "mini")
        ) ?? modelIds.ElementAtOrDefault(2) ?? modelIds.ElementAtOrDefault(1) ?? modelIds.FirstOrDefault() ?? "";

        return (deep, balanced, quick);
    }

    private static bool MatchesModel(string id, string pattern) =>
        id.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    private static string? FindFirstMatchingModel(IEnumerable<string> models, params Func<string, bool>[] predicates)
    {
        foreach (var predicate in predicates)
        {
            var match = models.FirstOrDefault(predicate);
            if (match != null)
                return match;
        }
        return null;
    }
}
