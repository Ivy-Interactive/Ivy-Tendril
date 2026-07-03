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
    public void GetStaticModels_ReturnsEmpty()
    {
        var models = _catalog.GetStaticModels();
        Assert.Empty(models);
    }
}
