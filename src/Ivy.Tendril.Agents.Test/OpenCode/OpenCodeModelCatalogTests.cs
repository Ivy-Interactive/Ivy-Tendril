using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.OpenCode;

public class OpenCodeModelCatalogTests
{
    private readonly OpenCodeModelCatalog _catalog = new();

    [Fact]
    public void AgentId_IsOpenCode()
    {
        Assert.Equal("opencode", _catalog.AgentId);
    }

    [Fact]
    public void GetStaticModels_ReturnsDefault()
    {
        var models = _catalog.GetStaticModels();
        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.Id == "moonshotai/Kimi-K3" && m.IsDefault);
    }

    [Fact]
    public async Task GetModelsAsync_WhenDiscoveryFails_FallsBackToStatic()
    {
        var result = await _catalog.GetModelsAsync();

        Assert.NotNull(result);
        Assert.Equal("opencode", result.AgentId);
        Assert.NotEmpty(result.Models);
    }

    [Fact]
    public async Task ParseModelsListAsync_ReturnsDiscoveredModelsSortedDescending()
    {
        var output = """
            claude-3-5-sonnet
            claude-opus-4-5
            claude-opus-5
            claude-sonnet-5
            gpt-4.1
            gpt-5.5
            gpt-5.6-terra
            """;

        var models = await OpenCodeModelCatalog.ParseModelsListAsync(output);

        Assert.NotNull(models);
        var ids = models.Select(m => m.Id).ToArray();
        Assert.Equal([
            "claude-opus-5",
            "claude-opus-4-5",
            "claude-sonnet-5",
            "claude-3-5-sonnet",
            "gpt-5.6-terra",
            "gpt-5.5",
            "gpt-4.1",
        ], ids);
    }

    [Fact]
    public void GetStaticModels_DeclaresOutputLimits()
    {
        var models = _catalog.GetStaticModels();

        foreach (var model in models.Where(m => m.Id != "default"))
        {
            Assert.True(model.MaxOutputTokens.HasValue, $"{model.Id} is missing MaxOutputTokens");
            Assert.True(model.ContextWindow.HasValue, $"{model.Id} is missing ContextWindow");
        }

        Assert.Equal(128_000, models.Single(m => m.Id == "claude-opus-5").MaxOutputTokens);
    }

    [Fact]
    public async Task ParseModelsListAsync_PopulatesMaxOutputTokens()
    {
        var models = await OpenCodeModelCatalog.ParseModelsListAsync("claude-opus-5");

        Assert.NotNull(models);
        var model = Assert.Single(models);
        Assert.Equal(128_000, model.MaxOutputTokens);
    }
}
