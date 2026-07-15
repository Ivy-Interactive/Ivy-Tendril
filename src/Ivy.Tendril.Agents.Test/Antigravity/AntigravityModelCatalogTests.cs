using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravityModelCatalogTests
{
    private readonly AntigravityModelCatalog _catalog = new();

    [Fact]
    public void AgentId_IsAntigravity()
    {
        Assert.Equal(AgentId.Antigravity, _catalog.AgentId);
    }

    [Fact]
    public void GetStaticModels_ReturnsNonEmpty()
    {
        var models = _catalog.GetStaticModels();
        Assert.NotEmpty(models);
    }

    [Fact]
    public void GetStaticModels_HasExactlyOneDefault()
    {
        var models = _catalog.GetStaticModels();
        Assert.Single(models, m => m.IsDefault);
    }

    [Fact]
    public void GetStaticModels_AllHaveProvider()
    {
        var models = _catalog.GetStaticModels();
        Assert.All(models, m => Assert.False(string.IsNullOrEmpty(m.Provider)));
    }

    [Fact]
    public void GetStaticModels_IdsAreUnique()
    {
        var ids = _catalog.GetStaticModels().Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
