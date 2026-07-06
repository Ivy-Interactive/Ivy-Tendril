using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityModelCatalog : CachedModelCatalogProvider
{
    private static readonly ModelCapabilities FullCaps =
        ModelCapabilities.Reasoning |
        ModelCapabilities.ImageInput |
        ModelCapabilities.CodeGeneration |
        ModelCapabilities.ToolUse |
        ModelCapabilities.Streaming;

    private static readonly ModelCapabilities MidCaps =
        ModelCapabilities.CodeGeneration |
        ModelCapabilities.ToolUse |
        ModelCapabilities.Streaming;

    public override string AgentId => Abstractions.AgentId.Antigravity;

    public override IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        new()
        {
            Id = "gemini-3.5-flash-high", DisplayName = "Gemini 3.5 Flash (High)",
            Capabilities = MidCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google", IsDefault = true,
        },
        new()
        {
            Id = "gemini-3.5-flash-medium", DisplayName = "Gemini 3.5 Flash (Medium)",
            Capabilities = MidCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
        new()
        {
            Id = "gemini-3.5-flash-low", DisplayName = "Gemini 3.5 Flash (Low)",
            Capabilities = MidCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
        new()
        {
            Id = "gemini-3.1-pro-high", DisplayName = "Gemini 3.1 Pro (High)",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
        new()
        {
            Id = "gemini-3.1-pro-low", DisplayName = "Gemini 3.1 Pro (Low)",
            Capabilities = FullCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
        new()
        {
            Id = "gemini-3-flash", DisplayName = "Gemini 3 Flash",
            Capabilities = MidCaps,
            ContextWindow = 1_000_000, MaxOutputTokens = 65_536,
            Provider = "google",
        },
    ];

    protected override async Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "agy", ["models"], TimeSpan.FromSeconds(5), ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return null;

        var results = new List<ModelInfo>();
        var first = true;

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            if (line.StartsWith('>'))
                line = line[1..].Trim();
            
            var currentIdx = line.IndexOf("(current)", StringComparison.OrdinalIgnoreCase);
            if (currentIdx >= 0)
                line = line[..currentIdx].Trim();

            if (string.IsNullOrWhiteSpace(line)) continue;

            var id = NormalizeModelId(line);
            var isPro = line.Contains("pro", StringComparison.OrdinalIgnoreCase);
            var caps = isPro ? FullCaps : MidCaps;

            results.Add(new ModelInfo
            {
                Id = id,
                DisplayName = line,
                Capabilities = caps,
                Provider = "google",
                IsDefault = first,
                ContextWindow = 1_000_000,
                MaxOutputTokens = 65_536,
            });
            first = false;
        }

        return results;
    }

    private static string NormalizeModelId(string name)
    {
        return name.ToLowerInvariant()
            .Replace(" (", "-")
            .Replace(")", "")
            .Replace(" ", "-");
    }
}
