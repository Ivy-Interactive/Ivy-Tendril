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
    public void GetStaticModels_ContainsGemini25Pro()
    {
        var models = _catalog.GetStaticModels();
        var pro = models.FirstOrDefault(m => m.Id == "gemini-2.5-pro");
        Assert.NotNull(pro);
        Assert.Equal("Gemini 2.5 Pro", pro!.DisplayName);
        Assert.Equal("google", pro.Provider);
    }

    [Fact]
    public void GetStaticModels_ContainsGemini25Flash()
    {
        var models = _catalog.GetStaticModels();
        var flash = models.FirstOrDefault(m => m.Id == "gemini-2.5-flash");
        Assert.NotNull(flash);
    }

    [Fact]
    public void GetStaticModels_ContainsGemini25FlashLite()
    {
        var models = _catalog.GetStaticModels();
        var lite = models.FirstOrDefault(m => m.Id == "gemini-2.5-flash-lite");
        Assert.NotNull(lite);
    }

    [Fact]
    public void GetStaticModels_DefaultModelIsGemini25Pro()
    {
        var models = _catalog.GetStaticModels();
        var defaults = models.Where(m => m.IsDefault).ToList();
        Assert.Single(defaults);
        Assert.Equal("gemini-2.5-pro", defaults[0].Id);
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
