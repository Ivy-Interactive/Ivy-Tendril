using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeModelCatalog : CachedModelCatalogProvider
{
    private static readonly ModelCapabilities DefaultCaps =
        ModelCapabilities.CodeGeneration | ModelCapabilities.ToolUse | ModelCapabilities.Streaming;

    public override string AgentId => Abstractions.AgentId.OpenCode;

    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new()
        {
            Id = "default", DisplayName = "OpenCode Default",
            Capabilities = DefaultCaps,
            Provider = "opencode", IsDefault = true,
        },
    ];

    protected override async Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "opencode", ["models"], TimeSpan.FromSeconds(15), ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;

        return await ParseModelsListAsync(stdout, ct);
    }

    private static Task<IReadOnlyList<ModelInfo>?> ParseModelsListAsync(string output, CancellationToken ct)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return Task.FromResult<IReadOnlyList<ModelInfo>?>(null);

        var results = new List<ModelInfo>();
        var first = true;

        foreach (var id in lines)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;

            var provider = ExtractProvider(id);
            var pricing = LookupPricing(id);

            results.Add(new ModelInfo
            {
                Id = id,
                DisplayName = id,
                Capabilities = DefaultCaps,
                Provider = provider,
                IsDefault = first,
                ContextWindow = pricing?.ContextWindow,
                InputPerMillion = pricing?.Input ?? 0m,
                OutputPerMillion = pricing?.Output ?? 0m,
                CacheReadPerMillion = pricing?.CacheRead ?? 0m,
                CacheWritePerMillion = pricing?.CacheWrite ?? 0m,
            });
            first = false;
        }

        return Task.FromResult<IReadOnlyList<ModelInfo>?>(results.Count > 0 ? results : null);
    }

    private static string ExtractProvider(string modelId)
    {
        var slash = modelId.IndexOf('/');
        return slash > 0 ? modelId[..slash] : "opencode";
    }

    private record KnownPricing(decimal Input, decimal Output, decimal CacheRead = 0m, decimal CacheWrite = 0m, int? ContextWindow = null);

    private static KnownPricing? LookupPricing(string modelId)
    {
        var name = modelId;
        var slash = modelId.IndexOf('/');
        if (slash > 0) name = modelId[(slash + 1)..];

        // Strip common prefixes (anthropic., eu.anthropic., global.anthropic., etc.)
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

        foreach (var (pattern, pricing) in KnownPricingTable)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return pricing;
        }
        return null;
    }

    private static readonly (string Pattern, KnownPricing Pricing)[] KnownPricingTable =
    [
        // Anthropic
        ("claude-opus-4-7",   new(10.00m, 50.00m, 1.00m, 12.50m, 200_000)),
        ("claude-opus-4-6",   new(5.00m, 25.00m, 0.50m, 6.25m, 200_000)),
        ("claude-opus-4-5",   new(5.00m, 25.00m, 0.50m, 6.25m, 200_000)),
        ("claude-opus-4-1",   new(15.00m, 75.00m, 1.50m, 18.75m, 200_000)),
        ("claude-opus-4",     new(15.00m, 75.00m, 1.50m, 18.75m, 200_000)),
        ("claude-sonnet-4-6", new(3.00m, 15.00m, 0.30m, 3.75m, 200_000)),
        ("claude-sonnet-4-5", new(3.00m, 15.00m, 0.30m, 3.75m, 200_000)),
        ("claude-sonnet-4",   new(3.00m, 15.00m, 0.30m, 3.75m, 200_000)),
        ("claude-3.7-sonnet", new(3.00m, 15.00m, 0.30m, 3.75m, 200_000)),
        ("claude-3.5-sonnet", new(3.00m, 15.00m, 0.30m, 3.75m, 200_000)),
        ("claude-3.5-haiku",  new(0.80m, 4.00m, 0.08m, 1.00m, 200_000)),
        ("claude-3-haiku",    new(0.25m, 1.25m, 0.03m, 0.30m, 200_000)),
        ("claude-3-opus",     new(15.00m, 75.00m, 1.50m, 18.75m, 200_000)),
        // OpenAI
        ("gpt-5.5",       new(10.00m, 40.00m, 2.50m, 12.50m, 400_000)),
        ("gpt-5.4",       new(10.00m, 40.00m, 2.50m, 12.50m, 400_000)),
        ("gpt-5.4-mini",  new(1.10m, 4.40m, 0.275m, 1.375m, 400_000)),
        ("gpt-4.5",       new(75.00m, 150.00m, 37.50m, 93.75m, 128_000)),
        ("gpt-4.1-mini",  new(0.40m, 1.60m, 0.10m, 0.50m, 1_047_576)),
        ("gpt-4.1-nano",  new(0.10m, 0.40m, 0.025m, 0.125m, 1_047_576)),
        ("gpt-4.1",       new(2.00m, 8.00m, 0.50m, 2.50m, 1_047_576)),
        ("gpt-4o-mini",   new(0.15m, 0.60m, 0.075m, 0.1875m, 128_000)),
        ("gpt-4o",        new(2.50m, 10.00m, 1.25m, 3.125m, 128_000)),
        ("o4-mini",       new(1.10m, 4.40m, 0.275m, 1.375m, 200_000)),
        ("o3-mini",       new(1.10m, 4.40m, 0.275m, 1.375m, 200_000)),
        ("o3",            new(10.00m, 40.00m, 2.50m, 12.50m, 200_000)),
        ("o1-mini",       new(1.10m, 4.40m, 0.275m, 1.375m, 128_000)),
        ("o1-pro",        new(150.00m, 600.00m, 0m, 0m, 128_000)),
        ("o1",            new(15.00m, 60.00m, 7.50m, 18.75m, 200_000)),
        // Google
        ("gemini-2.5-flash", new(0.15m, 3.50m, 0.0375m, 0.15m, 1_048_576)),
        ("gemini-2.5",       new(1.25m, 10.00m, 0.3125m, 1.5625m, 1_048_576)),
        ("gemini-2.0-flash", new(0.10m, 0.40m, 0.025m, 0.125m, 1_048_576)),
    ];
}
