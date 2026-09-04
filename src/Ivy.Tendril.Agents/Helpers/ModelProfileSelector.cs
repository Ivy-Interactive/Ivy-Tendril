using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Helpers;

public static class ModelProfileSelector
{
    public static ModelProviderKind DetectProvider(
        string? baseUrl,
        bool isIvy = false,
        bool isAnthropic = false,
        bool isBerget = false,
        bool isGoogle = false,
        bool isOpenAi = false)
    {
        if (isIvy) return ModelProviderKind.Ivy;
        if (isAnthropic) return ModelProviderKind.Anthropic;
        if (isBerget) return ModelProviderKind.Berget;
        if (isGoogle) return ModelProviderKind.Google;
        if (isOpenAi) return ModelProviderKind.OpenAi;

        var url = baseUrl?.Trim() ?? "";
        if (url.Contains("llmproxy.ivy.app", StringComparison.OrdinalIgnoreCase) || url.Contains("ivy.app", StringComparison.OrdinalIgnoreCase))
            return ModelProviderKind.Ivy;
        if (url.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase))
            return ModelProviderKind.Anthropic;
        if (url.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) || url.Contains("gemini", StringComparison.OrdinalIgnoreCase))
            return ModelProviderKind.Google;
        if (url.Contains("api.berget.ai", StringComparison.OrdinalIgnoreCase))
            return ModelProviderKind.Berget;
        if (url.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase))
            return ModelProviderKind.OpenAi;

        return ModelProviderKind.Generic;
    }

    public static (string deep, string balanced, string quick) SelectDefaults(
        IReadOnlyList<ModelInfo>? availableModels,
        ModelProviderKind provider = ModelProviderKind.Generic)
    {
        var modelIds = availableModels?.Select(m => m.Id).ToList() ?? [];
        var deep = SelectModel(provider, ModelProfileKind.Deep, modelIds);
        var balanced = SelectModel(provider, ModelProfileKind.Balanced, modelIds);
        var quick = SelectModel(provider, ModelProfileKind.Quick, modelIds);
        return (deep, balanced, quick);
    }

    public static (string deep, string balanced, string quick) SelectDefaults(
        IReadOnlyList<ModelInfo>? availableModels,
        bool isIvy = false,
        bool isAnthropic = false,
        bool isBerget = false,
        bool isGoogle = false,
        bool isOpenAi = false)
    {
        var provider = DetectProvider(null, isIvy, isAnthropic, isBerget, isGoogle, isOpenAi);
        return SelectDefaults(availableModels, provider);
    }

    public static string SelectModel(
        ModelProviderKind provider,
        ModelProfileKind profile,
        IReadOnlyList<string> availableModelIds)
    {
        var candidates = ModelProfilePriorities.GetPrioritizedCandidates(provider, profile);

        if (availableModelIds == null || availableModelIds.Count == 0)
        {
            return ModelProfilePriorities.GetDefaultModel(provider, profile);
        }

        foreach (var candidate in candidates)
        {
            var match = availableModelIds.FirstOrDefault(id =>
                id.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                id.Contains(candidate, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }
        }

        var fallbackIndex = profile switch
        {
            ModelProfileKind.Deep => 0,
            ModelProfileKind.Balanced => 1,
            ModelProfileKind.Quick => 2,
            _ => 0
        };

        return availableModelIds.ElementAtOrDefault(fallbackIndex)
            ?? availableModelIds.FirstOrDefault()
            ?? ModelProfilePriorities.GetDefaultModel(provider, profile);
    }
}
