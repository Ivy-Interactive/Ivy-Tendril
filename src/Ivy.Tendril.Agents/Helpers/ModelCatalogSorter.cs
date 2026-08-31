using System.Text.RegularExpressions;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Helpers;

public static class ModelCatalogSorter
{
    private static readonly Regex ClaudeOpusSonnetHaikuMajorMinorRegex = new(@"(?:claude-)?(?:opus|sonnet|haiku)-(\d+)[.\-_](\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClaudeOpusSonnetHaikuMajorRegex = new(@"(?:claude-)?(?:opus|sonnet|haiku)-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClaudeMajorMinorRegex = new(@"claude-(\d+)[.\-_](\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClaudeMajorRegex = new(@"claude-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClaudeDisplayRegex = new(@"(?:Opus|Sonnet|Haiku)\s+(\d+)(?:\.(\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GptMajorMinorRegex = new(@"gpt-(\d+)[.\-_](\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GptMajorRegex = new(@"gpt-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OSeriesRegex = new(@"(?:^|[^a-z0-9])o(\d+)(?:[.\-_](\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GptDisplayRegex = new(@"GPT-(\d+)(?:\.(\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GeminiMajorMinorRegex = new(@"gemini-(\d+)[.\-_](\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GeminiMajorRegex = new(@"gemini-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GeminiDisplayRegex = new(@"Gemini\s+(\d+)(?:\.(\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex KimiRegex = new(@"(?:kimi|k)[.\-_]?k?(\d+)(?:[.\-_](\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DeepSeekRegex = new(@"deepseek-[vr](\d+)(?:[.\-_](\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex QwenRegex = new(@"qwen[.\-_]?(\d+)(?:[.\-_](\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GenericMajorMinorRegex = new(@"(?<!\d)(\d+)\.(\d+)(?!\d)", RegexOptions.Compiled);

    public static IReadOnlyList<ModelInfo> Sort(IEnumerable<ModelInfo>? models, bool preserveDefault = false)
    {
        if (models is null) return Array.Empty<ModelInfo>();
        var list = models.ToList();
        if (list.Count <= 1) return list;

        ModelInfo? defaultModel = null;
        if (preserveDefault)
        {
            defaultModel = list.FirstOrDefault(m => m.IsDefault || m.Id.Equals("default", StringComparison.OrdinalIgnoreCase));
        }

        var toSort = defaultModel is not null
            ? list.Where(m => !ReferenceEquals(m, defaultModel)).ToList()
            : list;

        // Group by provider while preserving appearance order of providers
        var providerGroups = new List<(string ProviderKey, List<ModelInfo> Models)>();
        var providerLookup = new Dictionary<string, List<ModelInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in toSort)
        {
            var key = GetProviderKey(model);
            if (!providerLookup.TryGetValue(key, out var group))
            {
                group = new List<ModelInfo>();
                providerLookup[key] = group;
                providerGroups.Add((key, group));
            }
            group.Add(model);
        }

        var result = new List<ModelInfo>(list.Count);
        if (defaultModel is not null)
        {
            result.Add(defaultModel);
        }

        foreach (var (_, group) in providerGroups)
        {
            group.Sort(CompareModels);
            result.AddRange(group);
        }

        return result;
    }

    public static int CompareModels(ModelInfo? x, ModelInfo? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        if (x.Id.Equals("default", StringComparison.OrdinalIgnoreCase)) return 1;
        if (y.Id.Equals("default", StringComparison.OrdinalIgnoreCase)) return -1;

        var rankX = GetModelRank(x);
        var rankY = GetModelRank(y);

        // 1. Provider priority
        var provComp = rankX.ProviderOrder.CompareTo(rankY.ProviderOrder);
        if (provComp != 0) return provComp;

        // 2. Family Tier (Opus (0) < Sonnet (1) < Haiku (2), etc.)
        var tierComp = rankX.Tier.CompareTo(rankY.Tier);
        if (tierComp != 0) return tierComp;

        // 3. Sub-tier (Sol (0) < Terra (1) < Luna (2))
        var subTierComp = rankX.SubTier.CompareTo(rankY.SubTier);
        if (subTierComp != 0) return subTierComp;

        // 4. Version descending (higher version comes first)
        var verComp = rankY.Version.CompareTo(rankX.Version);
        if (verComp != 0) return verComp;

        // 5. Variant priority (Full/Pro (0) < Flash (1) < Mini/Lite (2) < Nano (3) < Preview (4))
        var varComp = rankX.Variant.CompareTo(rankY.Variant);
        if (varComp != 0) return varComp;

        // 6. Name alphabetical
        return string.Compare(x.DisplayName ?? x.Id, y.DisplayName ?? y.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProviderKey(ModelInfo model)
    {
        var prov = model.Provider?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(prov))
        {
            if (prov.Contains("anthropic") || prov.Contains("claude")) return "anthropic";
            if (prov.Contains("openai") || prov.Contains("codex")) return "openai";
            if (prov.Contains("google") || prov.Contains("gemini")) return "google";
            if (prov.Contains("moonshot") || prov.Contains("kimi")) return "moonshot";
            if (prov.Contains("deepseek")) return "deepseek";
            if (prov.Contains("berget")) return "berget";
            if (prov.Contains("qwen")) return "qwen";
            if (prov != "custom" && prov != "opencode") return prov;
        }

        var id = model.Id.ToLowerInvariant();
        if (id.Contains("claude") || id.Contains("opus") || id.Contains("sonnet") || id.Contains("haiku")) return "anthropic";
        if (id.Contains("gpt") || id.StartsWith("o1") || id.StartsWith("o3") || id.StartsWith("o4") || id.Contains("codex")) return "openai";
        if (id.Contains("gemini")) return "google";
        if (id.Contains("kimi") || id.Contains("moonshot")) return "moonshot";
        if (id.Contains("deepseek")) return "deepseek";
        if (id.Contains("qwen")) return "qwen";

        return prov ?? "custom";
    }

    private record struct ModelRank(int ProviderOrder, int Tier, int SubTier, Version Version, int Variant);

    private static ModelRank GetModelRank(ModelInfo model)
    {
        var key = GetProviderKey(model);
        var id = model.Id.ToLowerInvariant();
        var name = (model.DisplayName ?? "").ToLowerInvariant();
        var combined = $"{id} {name}";

        return key switch
        {
            "anthropic" => GetAnthropicRank(id, name, combined),
            "openai" => GetOpenAiRank(id, name, combined),
            "google" => GetGoogleRank(id, name, combined),
            "moonshot" => GetMoonshotRank(id, name, combined),
            "deepseek" => GetDeepSeekRank(id, name, combined),
            "qwen" or "berget" => GetQwenRank(id, name, combined),
            _ => GetGenericRank(id, name, combined),
        };
    }

    private static ModelRank GetAnthropicRank(string id, string name, string combined)
    {
        // Anthropic Tiers:
        // Tier 0: Opus
        // Tier 1: Sonnet
        // Tier 2: Haiku
        // Tier 3: Other Anthropic
        int tier;
        if (combined.Contains("opus")) tier = 0;
        else if (combined.Contains("sonnet")) tier = 1;
        else if (combined.Contains("haiku")) tier = 2;
        else tier = 3;

        var version = ParseAnthropicVersion(id, name);
        int variant = 0;
        if (combined.Contains("alt")) variant = 1;

        return new ModelRank(ProviderOrder: 1, Tier: tier, SubTier: 0, Version: version, Variant: variant);
    }

    private static Version ParseAnthropicVersion(string id, string name)
    {
        var m = ClaudeOpusSonnetHaikuMajorMinorRegex.Match(id);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));

        m = ClaudeOpusSonnetHaikuMajorRegex.Match(id);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), 0);

        m = ClaudeMajorMinorRegex.Match(id);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));

        m = ClaudeMajorRegex.Match(id);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), 0);

        m = ClaudeDisplayRegex.Match(name);
        if (m.Success)
        {
            var major = int.Parse(m.Groups[1].Value);
            var minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            return new Version(major, minor);
        }

        m = GenericMajorMinorRegex.Match(name);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));

        return new Version(0, 0);
    }

    private static ModelRank GetOpenAiRank(string id, string name, string combined)
    {
        // OpenAI Tiers:
        // Tier 0: GPT-5 Flagships (Sol, Terra, Luna)
        // Tier 1: GPT-5.x
        // Tier 2: GPT-4.x
        // Tier 3: O-series (o4, o3, o1)
        // Tier 4: Other OpenAI
        int tier;
        int subTier = 0;
        int variant = 0;
        Version version;

        if (combined.Contains("sol"))
        {
            tier = 0;
            subTier = 0;
            version = ParseOpenAiVersion(id, name, fallbackMajor: 5, fallbackMinor: 6);
        }
        else if (combined.Contains("terra"))
        {
            tier = 0;
            subTier = 1;
            version = ParseOpenAiVersion(id, name, fallbackMajor: 5, fallbackMinor: 6);
        }
        else if (combined.Contains("luna"))
        {
            tier = 0;
            subTier = 2;
            version = ParseOpenAiVersion(id, name, fallbackMajor: 5, fallbackMinor: 6);
        }
        else if (combined.Contains("gpt-5") || combined.Contains("gpt 5"))
        {
            tier = 1;
            version = ParseOpenAiVersion(id, name, fallbackMajor: 5, fallbackMinor: 0);
            if (combined.Contains("mini")) variant = 1;
            else if (combined.Contains("codex")) variant = 0;
        }
        else if (combined.Contains("gpt-4") || combined.Contains("gpt 4"))
        {
            tier = 2;
            version = ParseOpenAiVersion(id, name, fallbackMajor: 4, fallbackMinor: 0);
            if (combined.Contains("nano")) variant = 2;
            else if (combined.Contains("mini")) variant = 1;
        }
        else if (OSeriesRegex.IsMatch(id) || combined.StartsWith("o1") || combined.StartsWith("o3") || combined.StartsWith("o4"))
        {
            tier = 3;
            var om = OSeriesRegex.Match(id);
            var major = om.Success ? int.Parse(om.Groups[1].Value) : 1;
            var minor = om.Success && om.Groups[2].Success ? int.Parse(om.Groups[2].Value) : 0;
            version = new Version(major, minor);
            if (combined.Contains("mini")) variant = 1;
            else if (combined.Contains("pro")) variant = 0;
        }
        else
        {
            tier = 4;
            version = ParseOpenAiVersion(id, name, fallbackMajor: 0, fallbackMinor: 0);
        }

        return new ModelRank(ProviderOrder: 2, Tier: tier, SubTier: subTier, Version: version, Variant: variant);
    }

    private static Version ParseOpenAiVersion(string id, string name, int fallbackMajor, int fallbackMinor)
    {
        var m = GptMajorMinorRegex.Match(id);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));

        m = GptMajorRegex.Match(id);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), 0);

        m = GptDisplayRegex.Match(name);
        if (m.Success)
        {
            var major = int.Parse(m.Groups[1].Value);
            var minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            return new Version(major, minor);
        }

        m = GenericMajorMinorRegex.Match(name);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));

        return new Version(fallbackMajor, fallbackMinor);
    }

    private static ModelRank GetGoogleRank(string id, string name, string combined)
    {
        // Google: Tier 0, versions 3.x > 2.x > 1.x
        var version = ParseGoogleVersion(id, name);
        int variant = 0;
        if (combined.Contains("pro")) variant = 0;
        else if (combined.Contains("flash-lite") || combined.Contains("flash lite")) variant = 2;
        else if (combined.Contains("flash")) variant = 1;
        else if (combined.Contains("preview")) variant = 3;

        return new ModelRank(ProviderOrder: 3, Tier: 0, SubTier: 0, Version: version, Variant: variant);
    }

    private static Version ParseGoogleVersion(string id, string name)
    {
        var m = GeminiMajorMinorRegex.Match(id);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));

        m = GeminiMajorRegex.Match(id);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), 0);

        m = GeminiDisplayRegex.Match(name);
        if (m.Success)
        {
            var major = int.Parse(m.Groups[1].Value);
            var minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            return new Version(major, minor);
        }

        m = GenericMajorMinorRegex.Match(name);
        if (m.Success) return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));

        return new Version(0, 0);
    }

    private static ModelRank GetMoonshotRank(string id, string name, string combined)
    {
        var m = KimiRegex.Match(combined);
        var major = m.Success ? int.Parse(m.Groups[1].Value) : 0;
        var minor = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return new ModelRank(ProviderOrder: 4, Tier: 0, SubTier: 0, Version: new Version(major, minor), Variant: 0);
    }

    private static ModelRank GetDeepSeekRank(string id, string name, string combined)
    {
        var m = DeepSeekRegex.Match(combined);
        var major = m.Success ? int.Parse(m.Groups[1].Value) : 0;
        var minor = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return new ModelRank(ProviderOrder: 5, Tier: 0, SubTier: 0, Version: new Version(major, minor), Variant: 0);
    }

    private static ModelRank GetQwenRank(string id, string name, string combined)
    {
        var m = QwenRegex.Match(combined);
        var major = m.Success ? int.Parse(m.Groups[1].Value) : 0;
        var minor = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return new ModelRank(ProviderOrder: 6, Tier: 0, SubTier: 0, Version: new Version(major, minor), Variant: 0);
    }

    private static ModelRank GetGenericRank(string id, string name, string combined)
    {
        var m = GenericMajorMinorRegex.Match(combined);
        var version = m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : new Version(0, 0);
        return new ModelRank(ProviderOrder: 10, Tier: 0, SubTier: 0, Version: version, Variant: 0);
    }
}
