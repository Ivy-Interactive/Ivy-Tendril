using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

/// <summary>
/// The limits and per-million rates of one model, independent of which provider serves it. A model
/// has one context window and one price list, so exactly one <see cref="ModelSpec" /> describes it
/// and every catalog that lists it reads the same row.
/// </summary>
public sealed record ModelSpec(
    int ContextWindow,
    int MaxOutputTokens,
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal CacheReadPerMillion = 0m,
    decimal CacheWritePerMillion = 0m);

/// <summary>
/// The canonical model limits table. Before this existed, six catalogs and a private pricing table
/// each hardcoded their own numbers for overlapping model IDs and disagreed: the same model resolved
/// to a 1M window through one catalog and 200k through another, and <c>claude-opus-4-8</c> was priced
/// three times over because a declaration-order substring scan captured it with the
/// <c>claude-opus-4</c> row. Catalogs now declare identity only (ID, display name, capabilities,
/// efforts, provider) and take every number from here.
/// </summary>
public static class ModelSpecs
{
    /// <summary>
    /// One row per model ID any catalog declares, plus the IDs the CLIs emit that no catalog lists.
    /// Rows are keyed by <see cref="Normalize" />d ID, so a dotted and a dashed spelling of the same
    /// model (<c>claude-3.5-sonnet</c> / <c>claude-3-5-sonnet</c>) are one row, not two that can drift.
    /// </summary>
    private static readonly (string Id, ModelSpec Spec)[] Rows =
    [
        // ── Anthropic ────────────────────────────────────────────────────────────────────────────
        // The 4.6/4.7 rows resolve the 5x window and 2x price disagreement in favour of the
        // anthropic-native catalog, which is what the `ivy` provider already runs on.
        ("claude-fable-5",    new(1_000_000, 128_000, 10.00m, 50.00m, 1.00m, 12.50m)),
        ("claude-opus-5-1",   new(1_000_000, 128_000, 5.00m, 25.00m, 0.50m, 6.25m)),
        ("claude-opus-5",     new(1_000_000, 128_000, 5.00m, 25.00m, 0.50m, 6.25m)),
        ("claude-opus-4-8",   new(1_000_000, 128_000, 5.00m, 25.00m, 0.50m, 6.25m)),
        ("claude-opus-4-7",   new(1_000_000, 128_000, 5.00m, 25.00m, 0.50m, 6.25m)),
        ("claude-opus-4-6",   new(1_000_000, 128_000, 5.00m, 25.00m, 0.50m, 6.25m)),
        ("claude-opus-4-5",   new(200_000, 128_000, 5.00m, 25.00m, 0.50m, 6.25m)),
        ("claude-opus-4-1",   new(200_000, 128_000, 15.00m, 75.00m, 1.50m, 18.75m)),
        ("claude-opus-4",     new(200_000, 128_000, 15.00m, 75.00m, 1.50m, 18.75m)),
        ("claude-3-opus",     new(200_000, 64_000, 15.00m, 75.00m, 1.50m, 18.75m)),
        ("claude-sonnet-5-1", new(1_000_000, 128_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        ("claude-5.1",        new(1_000_000, 128_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        ("claude-sonnet-5",   new(1_000_000, 128_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        ("claude-sonnet-4-6", new(1_000_000, 128_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        ("claude-sonnet-4-5", new(200_000, 128_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        ("claude-sonnet-4",   new(200_000, 128_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        // The 3.x models publish a 200k window and a 64k output limit; the catalogs claimed 1M/128k.
        ("claude-3-7-sonnet", new(200_000, 64_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        ("claude-3-5-sonnet", new(200_000, 64_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        ("claude-haiku-5-1",  new(200_000, 64_000, 1.00m, 5.00m, 0.10m, 1.25m)),
        ("claude-haiku-4-5",  new(200_000, 64_000, 1.00m, 5.00m, 0.10m, 1.25m)),
        ("claude-3-5-haiku",  new(200_000, 64_000, 0.80m, 4.00m, 0.08m, 1.00m)),
        ("claude-3-haiku",    new(200_000, 64_000, 0.25m, 1.25m, 0.03m, 0.30m)),

        // Bare Claude CLI aliases. Each carries the spec of the model it resolves to in practice,
        // which is what removes the alias-vs-concrete price split (`sonnet` used to be 2/10 while
        // `claude-sonnet-5` was 3/15).
        ("opus",   new(1_000_000, 128_000, 5.00m, 25.00m, 0.50m, 6.25m)),
        ("sonnet", new(1_000_000, 128_000, 3.00m, 15.00m, 0.30m, 3.75m)),
        ("haiku",  new(200_000, 64_000, 1.00m, 5.00m, 0.10m, 1.25m)),

        // ── OpenAI ───────────────────────────────────────────────────────────────────────────────
        ("gpt-6-astra",   new(400_000, 32_000, 10.00m, 40.00m, 2.50m, 12.50m)),
        ("gpt-5.6-sol",   new(400_000, 32_000, 10.00m, 40.00m, 2.50m, 12.50m)),
        ("gpt-5.6-terra", new(272_000, 16_000, 1.10m, 4.40m, 0.275m, 1.375m)),
        ("gpt-5.6-luna",  new(272_000, 16_000, 1.50m, 6.00m, 0.375m, 1.875m)),
        ("gpt-5.5",       new(400_000, 32_000, 10.00m, 40.00m, 2.50m, 12.50m)),
        ("gpt-5.4",       new(400_000, 32_000, 10.00m, 40.00m, 2.50m, 12.50m)),
        ("gpt-5.4-mini",  new(400_000, 16_000, 1.10m, 4.40m, 0.275m, 1.375m)),
        ("gpt-5.3-codex", new(400_000, 16_000, 1.50m, 6.00m, 0.375m, 1.875m)),
        ("gpt-4.5",       new(128_000, 16_000, 75.00m, 150.00m, 37.50m, 93.75m)),
        ("gpt-4.1",       new(1_047_576, 32_000, 2.00m, 8.00m, 0.50m, 2.50m)),
        ("gpt-4.1-mini",  new(1_047_576, 16_000, 0.40m, 1.60m, 0.10m, 0.50m)),
        ("gpt-4.1-nano",  new(1_047_576, 16_000, 0.10m, 0.40m, 0.025m, 0.125m)),
        ("gpt-4o",        new(128_000, 16_000, 2.50m, 10.00m, 1.25m, 3.125m)),
        ("gpt-4o-mini",   new(128_000, 16_000, 0.15m, 0.60m, 0.075m, 0.1875m)),
        ("gpt-oss-120b",  new(128_000, 32_768, 0.15m, 0.60m)),
        ("codex-mini",    new(400_000, 16_000, 1.50m, 6.00m, 0.375m, 1.875m)),
        ("o4-mini",       new(200_000, 100_000, 1.10m, 4.40m, 0.275m, 1.375m)),
        ("o3-mini",       new(200_000, 100_000, 1.10m, 4.40m, 0.275m, 1.375m)),
        ("o3",            new(200_000, 100_000, 10.00m, 40.00m, 2.50m, 12.50m)),
        ("o1-mini",       new(128_000, 100_000, 1.10m, 4.40m, 0.275m, 1.375m)),
        ("o1-pro",        new(128_000, 100_000, 150.00m, 600.00m)),
        ("o1",            new(200_000, 100_000, 15.00m, 60.00m, 7.50m, 18.75m)),

        // ── Google ───────────────────────────────────────────────────────────────────────────────
        // The 3.x rows take the two native catalogs' 0.60 output rate over the pricing table's 3.50,
        // and their 1M window over the table's 1_048_576.
        ("gemini-3.8-flash",        new(1_000_000, 65_536, 0.15m, 0.60m, 0.0375m)),
        ("gemini-3.7-flash",        new(1_000_000, 65_536, 0.15m, 0.60m, 0.0375m)),
        ("gemini-3.6-flash",        new(1_000_000, 65_536, 0.15m, 0.60m, 0.0375m)),
        ("gemini-3-flash-preview",  new(1_000_000, 65_536, 0.15m, 0.60m, 0.0375m)),
        ("gemini-3.1-pro",          new(1_000_000, 65_536, 1.25m, 10.00m, 0.315m)),
        ("gemini-3-pro-preview",    new(1_000_000, 65_536, 1.25m, 10.00m, 0.315m)),
        ("gemini-2.5-flash",        new(1_048_576, 65_536, 0.15m, 3.50m, 0.0375m, 0.15m)),
        ("gemini-2.5",              new(1_048_576, 65_536, 1.25m, 10.00m, 0.3125m, 1.5625m)),
        ("gemini-2.0-flash",        new(1_048_576, 65_536, 0.10m, 0.40m, 0.025m, 0.125m)),

        // ── Open-weight models ───────────────────────────────────────────────────────────────────
        // Rates stay 0 because no catalog has ever declared any for these and inventing one would
        // start attributing per-token costs to runs that were previously reported as free.
        // Kimi-K3's limits are Berget's published figures (https://models.dev/api.json, provider
        // "berget"); kimi-k2/deepseek-v3/deepseek-r1 were removed from OpenCodeModelCatalog (plan
        // 00481) as unserved by any reachable provider, so their rows are gone too.
        ("Kimi-K3",                    new(327_680, 32_768, 0m, 0m)),
        ("Qwen2.5-Coder-32B-Instruct", new(128_000, 32_000, 0m, 0m)),
    ];

    private static readonly Dictionary<string, ModelSpec> Table =
        Rows.ToDictionary(r => Normalize(r.Id), r => r.Spec, StringComparer.Ordinal);

    /// <summary>
    /// Substring patterns tried longest first, not in declaration order. Declaration order is what
    /// let <c>claude-opus-4</c> capture <c>claude-opus-4-8</c>; longest-first cannot, because a
    /// longer pattern that matches is always more specific. Same reason
    /// <see cref="ModelPricingProvider" /> length-orders its keys.
    /// </summary>
    private static readonly string[] PatternsLongestFirst =
        [.. Table.Keys.OrderByDescending(k => k.Length)];

    /// <summary>
    /// Resolves <paramref name="modelId" /> to its canonical spec, or null when nothing matches.
    /// Tries the declared ID (exact, provider-prefix-stripped, dot/dash-insensitive) and then falls
    /// back to a longest-first substring match, which is how a dated or suffixed ID from a CLI
    /// (<c>anthropic/claude-opus-4-8-20260101</c>) still resolves.
    /// </summary>
    public static ModelSpec? Find(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;

        if (FindDeclared(modelId) is { } declared)
            return declared;

        var normalized = Normalize(StripProviderPrefix(modelId));
        foreach (var pattern in PatternsLongestFirst)
        {
            if (normalized.Contains(pattern, StringComparison.Ordinal))
                return Table[pattern];
        }

        return null;
    }

    /// <summary>
    /// The catalog-facing lookup: returns the spec for a model ID a catalog declares, and throws
    /// when the table has no row for it. Deliberately does not fall back to
    /// <see cref="Find" />'s substring match — a new catalog entry whose ID nobody has priced should
    /// break the build rather than silently inherit a shorter row's numbers.
    /// </summary>
    public static ModelSpec Require(string modelId) =>
        FindDeclared(modelId) ?? throw new InvalidOperationException(
            $"No canonical ModelSpec is declared for model id '{modelId}'. " +
            "Add a row to ModelSpecs.Rows so every catalog that lists this model reports the same limits.");

    /// <summary>
    /// Matches only IDs the table declares: exactly, or after stripping a provider prefix, or in the
    /// other of the two spellings (<c>claude-opus-5.1</c> for <c>claude-opus-5-1</c>).
    /// </summary>
    private static ModelSpec? FindDeclared(string modelId)
    {
        if (Table.TryGetValue(Normalize(modelId), out var exact))
            return exact;

        return Table.TryGetValue(Normalize(StripProviderPrefix(modelId)), out var stripped)
            ? stripped
            : null;
    }

    /// <summary>
    /// Folds the spellings that mean the same model into one key: case, and <c>.</c> versus <c>-</c>
    /// as the version separator. Catalog IDs use dashes (<c>claude-3-5-sonnet</c>) where the pricing
    /// table used dots (<c>claude-3.5-sonnet</c>), which is why those IDs used to resolve to nothing.
    /// </summary>
    internal static string Normalize(string modelId) =>
        modelId.Trim().Replace('.', '-').ToLowerInvariant();

    /// <summary>
    /// Drops the routing prefix a provider puts in front of a model name: everything up to the first
    /// <c>/</c>, then a <c>anthropic.</c> / <c>eu.anthropic.</c> / <c>global.anthropic.</c>-style
    /// vendor and region prefix.
    /// </summary>
    internal static string StripProviderPrefix(string modelId)
    {
        var name = modelId.Trim();

        var slash = name.IndexOf('/');
        if (slash > 0) name = name[(slash + 1)..];

        var dotIdx = name.IndexOf('.');
        if (dotIdx > 0 && name[..dotIdx] is "anthropic" or "au" or "eu" or "global" or "us")
        {
            var afterDot = name[(dotIdx + 1)..];
            dotIdx = afterDot.IndexOf('.');
            if (dotIdx > 0 && afterDot[..dotIdx] == "anthropic")
                name = afterDot[(dotIdx + 1)..];
            else if (afterDot.StartsWith("claude") || afterDot.StartsWith("gpt") || afterDot.StartsWith("o"))
                name = afterDot;
        }

        return name;
    }
}

public static class ModelSpecExtensions
{
    /// <summary>
    /// Fills a catalog entry's six numeric fields from the canonical table, so a catalog declares
    /// only what is genuinely its own: the ID, display name, capabilities, efforts and provider.
    /// </summary>
    /// <param name="maxOutputTokens">
    /// A provider-specific cap on the response length, for a harness that truncates below the
    /// model's own limit (the Antigravity CLI caps every anthropic model at 64k). Must not exceed
    /// the canonical limit: a provider may serve less of a model than it has, never more.
    /// </param>
    public static ModelInfo WithSpec(this ModelInfo model, int? maxOutputTokens = null)
    {
        var spec = ModelSpecs.Require(model.Id);

        if (maxOutputTokens > spec.MaxOutputTokens)
            throw new ArgumentOutOfRangeException(
                nameof(maxOutputTokens),
                maxOutputTokens,
                $"'{model.Id}' cannot declare a {maxOutputTokens} output limit above its canonical {spec.MaxOutputTokens}.");

        return model with
        {
            ContextWindow = spec.ContextWindow,
            MaxOutputTokens = maxOutputTokens ?? spec.MaxOutputTokens,
            InputPerMillion = spec.InputPerMillion,
            OutputPerMillion = spec.OutputPerMillion,
            CacheReadPerMillion = spec.CacheReadPerMillion,
            CacheWritePerMillion = spec.CacheWritePerMillion,
        };
    }
}
