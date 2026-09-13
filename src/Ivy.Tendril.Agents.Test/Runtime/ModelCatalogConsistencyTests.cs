using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Gemini;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenAiProxy;
using Ivy.Tendril.Agents.Providers.OpenCode;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

/// <summary>
/// Guards the invariant that made <see cref="ModelSpecs" /> necessary: one model has one context
/// window, one output limit and one price list, whichever catalog you reach it through.
/// <see cref="Providers.Copilot.CopilotModelCatalog" /> is excluded because it deliberately declares
/// no numbers — Copilot is subscription-billed, so pricing its models would start attributing
/// per-token costs to runs that have none.
/// </summary>
public class ModelCatalogConsistencyTests
{
    /// <summary>The OpenCode default entry is a "whatever the CLI picks" placeholder with no model behind it.</summary>
    private const string PlaceholderId = "default";

    private static IReadOnlyList<(string Agent, ModelInfo Model)> AllEntries()
    {
        IModelCatalogProvider[] catalogs =
        [
            new ClaudeModelCatalog(),
            new AntigravityModelCatalog(),
            new CodexModelCatalog(),
            new GeminiModelCatalog(),
            new OpenCodeModelCatalog(),
            new IvyModelCatalog(),
            // The one endpoint whose model list contains an entry OpenAiProxy declares itself.
            new OpenAiProxyModelCatalog(() => "https://api.berget.ai/v1"),
        ];

        return
        [
            .. catalogs.SelectMany(c => c.GetStaticModels()
                .Where(m => m.Id != PlaceholderId)
                .Select(m => (c.AgentId, m)))
        ];
    }

    [Fact]
    public void EveryCatalogEntry_HasCanonicalSpec()
    {
        var orphans = AllEntries()
            .Where(e => ModelSpecs.Find(e.Model.Id) is null)
            .Select(e => $"{e.Agent}/{e.Model.Id}")
            .Distinct()
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryCatalogEntry_MatchesCanonicalWindow()
    {
        foreach (var (agent, model) in AllEntries())
        {
            var spec = ModelSpecs.Require(model.Id);
            Assert.True(
                model.ContextWindow == spec.ContextWindow,
                $"{agent}/{model.Id} declares a {model.ContextWindow} context window, canonical is {spec.ContextWindow}");
        }
    }

    [Fact]
    public void EveryCatalogEntry_DoesNotExceedCanonicalOutput()
    {
        // `<=`, not `==`: a provider whose harness truncates below the model's own limit (Antigravity
        // caps anthropic responses at 64k) is legitimate. Claiming more than the model can produce
        // is not.
        foreach (var (agent, model) in AllEntries())
        {
            var spec = ModelSpecs.Require(model.Id);
            Assert.NotNull(model.MaxOutputTokens);
            Assert.True(
                model.MaxOutputTokens <= spec.MaxOutputTokens,
                $"{agent}/{model.Id} declares a {model.MaxOutputTokens} output limit above its canonical {spec.MaxOutputTokens}");
        }
    }

    [Fact]
    public void EveryCatalogEntry_MatchesCanonicalRates()
    {
        foreach (var (agent, model) in AllEntries())
        {
            var spec = ModelSpecs.Require(model.Id);

            Assert.Equal(spec.InputPerMillion, model.InputPerMillion);
            Assert.Equal(spec.OutputPerMillion, model.OutputPerMillion);
            Assert.Equal(spec.CacheReadPerMillion, model.CacheReadPerMillion);
            Assert.Equal(spec.CacheWritePerMillion, model.CacheWritePerMillion);
        }
    }

    [Fact]
    public void SharedModelIds_AgreeAcrossCatalogs()
    {
        var shared = AllEntries()
            .GroupBy(e => e.Model.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(e => e.Agent).Distinct().Count() > 1);

        foreach (var group in shared)
        {
            Assert.Single(group.Select(e => e.Model.ContextWindow).Distinct());
            Assert.Single(group.Select(e => e.Model.InputPerMillion).Distinct());
            Assert.Single(group.Select(e => e.Model.OutputPerMillion).Distinct());
            Assert.Single(group.Select(e => e.Model.CacheReadPerMillion).Distinct());
            Assert.Single(group.Select(e => e.Model.CacheWritePerMillion).Distinct());

            // Output limits may differ only downwards, and only per provider cap.
            var canonical = ModelSpecs.Require(group.Key).MaxOutputTokens;
            Assert.All(group, e => Assert.True(e.Model.MaxOutputTokens <= canonical));
        }
    }

    [Fact]
    public void DottedAndDashedSpellingsOfTheSameModel_ReportTheSameLimits()
    {
        var entries = AllEntries();
        var dashed = entries.Single(e => e.Agent == AgentId.Claude && e.Model.Id == "claude-3-7-sonnet").Model;
        var dotted = entries.Single(e => e.Agent == AgentId.Claude && e.Model.Id == "claude-3.7-sonnet").Model;

        Assert.Equal(dashed.ContextWindow, dotted.ContextWindow);
        Assert.Equal(dashed.MaxOutputTokens, dotted.MaxOutputTokens);
        Assert.Equal(dashed.OutputPerMillion, dotted.OutputPerMillion);
    }

    [Fact]
    public void ClaudeSonnetAlias_IsPricedLikeTheModelItResolvesTo()
    {
        var claude = new ClaudeModelCatalog().GetStaticModels();
        var alias = claude.Single(m => m.Id == "sonnet");
        var concrete = claude.Single(m => m.Id == "claude-sonnet-5");

        Assert.Equal(concrete.InputPerMillion, alias.InputPerMillion);
        Assert.Equal(concrete.OutputPerMillion, alias.OutputPerMillion);
    }

    [Fact]
    public void AntigravityAnthropicModels_KeepTheirHarnessOutputCap()
    {
        var anthropic = new AntigravityModelCatalog().GetStaticModels()
            .Where(m => m.Provider == "anthropic")
            .ToList();

        Assert.NotEmpty(anthropic);
        Assert.All(anthropic, m => Assert.Equal(65_536, m.MaxOutputTokens));
        // ...while still reporting the model's real context window.
        Assert.All(anthropic, m => Assert.Equal(1_000_000, m.ContextWindow));
    }

    [Fact]
    public void TryGetLimits_ForOpus46_ReturnsOneMillionContext()
    {
        // The launch-path bug: `anthropic/claude-opus-4-6` has no OpenCode static row, so the private
        // pricing table answered with 200k and OpenCodeCli wrote that into the process's
        // `limit.context` — one fifth of the model's real window.
        var limits = OpenCodeModelCatalog.TryGetLimits("anthropic/claude-opus-4-6");

        Assert.NotNull(limits);
        Assert.Equal(1_000_000, limits.Value.Context);
        Assert.Equal(128_000, limits.Value.Output);
    }

    [Fact]
    public void TryGetLimits_ForLegacyDashedClaudeId_Resolves()
    {
        // Catalog IDs use dashes, the old table used dots, so these resolved to nothing at all.
        var limits = OpenCodeModelCatalog.TryGetLimits("anthropic/claude-3-5-sonnet");

        Assert.NotNull(limits);
        Assert.Equal(200_000, limits.Value.Context);
        Assert.Equal(64_000, limits.Value.Output);
    }

    [Fact]
    public async Task ParseModelsListAsync_ForOpus48_UsesItsOwnRateNotOpus4s()
    {
        // Declaration-order matching charged a discovered `claude-opus-4-8` at the `claude-opus-4`
        // rate, inflating its cost threefold.
        var models = await OpenCodeModelCatalog.ParseModelsListAsync("anthropic/claude-opus-4-8");

        Assert.NotNull(models);
        var model = Assert.Single(models);
        Assert.Equal(5.00m, model.InputPerMillion);
        Assert.Equal(25.00m, model.OutputPerMillion);
        Assert.Equal(1_000_000, model.ContextWindow);
    }

    [Fact]
    public async Task ParseModelsListAsync_ForLegacyDashedClaudeId_HasLimitsAndRates()
    {
        var models = await OpenCodeModelCatalog.ParseModelsListAsync("claude-3-5-sonnet");

        Assert.NotNull(models);
        var model = Assert.Single(models);
        Assert.Equal(200_000, model.ContextWindow);
        Assert.Equal(64_000, model.MaxOutputTokens);
        Assert.Equal(3.00m, model.InputPerMillion);
        Assert.Equal(15.00m, model.OutputPerMillion);
    }
}
