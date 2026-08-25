using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiModelCatalogTests
{
    private readonly GeminiModelCatalog _catalog = new();

    [Fact]
    public void AgentId_IsGemini()
    {
        Assert.Equal(AgentId.Gemini, _catalog.AgentId);
    }

    [Fact]
    public void GetStaticModels_ReturnsExpectedModels()
    {
        var models = _catalog.GetStaticModels();
        Assert.True(models.Count >= 5);
    }

    [Fact]
    public void GetStaticModels_ContainsGemini37Flash()
    {
        var models = _catalog.GetStaticModels();
        var flash = models.FirstOrDefault(m => m.Id == "gemini-3.7-flash");
        Assert.NotNull(flash);
        Assert.Equal("Gemini 3.7 Flash", flash!.DisplayName);
        Assert.Equal("google", flash.Provider);
    }

    [Fact]
    public void GetStaticModels_ContainsGemini36Flash()
    {
        var models = _catalog.GetStaticModels();
        var flash = models.FirstOrDefault(m => m.Id == "gemini-3.6-flash");
        Assert.NotNull(flash);
        Assert.Equal("Gemini 3.6 Flash", flash!.DisplayName);
    }

    [Fact]
    public void GetStaticModels_ContainsGemini35Flash()
    {
        var models = _catalog.GetStaticModels();
        var flash = models.FirstOrDefault(m => m.Id == "gemini-3.5-flash");
        Assert.NotNull(flash);
        Assert.Equal("Gemini 3.5 Flash", flash!.DisplayName);
    }

    [Fact]
    public void GetStaticModels_ContainsGemini31Pro()
    {
        var models = _catalog.GetStaticModels();
        var pro = models.FirstOrDefault(m => m.Id == "gemini-3.1-pro");
        Assert.NotNull(pro);
        Assert.Equal("Gemini 3.1 Pro", pro!.DisplayName);
        Assert.Equal("google", pro.Provider);
    }

    [Fact]
    public void GetStaticModels_DefaultModelIsGemini37Flash()
    {
        var models = _catalog.GetStaticModels();
        var defaults = models.Where(m => m.IsDefault).ToList();
        Assert.Single(defaults);
        Assert.Equal("gemini-3.7-flash", defaults[0].Id);
    }

    [Fact]
    public void GetStaticModels_AllHavePositivePricing()
    {
        var models = _catalog.GetStaticModels();
        foreach (var model in models)
        {
            Assert.True(model.InputPerMillion > 0, $"{model.Id} should have positive InputPerMillion");
            Assert.True(model.OutputPerMillion > 0, $"{model.Id} should have positive OutputPerMillion");
        }
    }

    [Fact]
    public void GetStaticModels_AllHaveContextWindow()
    {
        var models = _catalog.GetStaticModels();
        foreach (var model in models)
        {
            Assert.True(model.ContextWindow > 0, $"{model.Id} should have positive ContextWindow");
        }
    }
}
