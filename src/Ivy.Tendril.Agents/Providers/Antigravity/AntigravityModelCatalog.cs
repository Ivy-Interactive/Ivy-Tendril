using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityModelCatalog : CachedModelCatalogProvider
{
    public override string AgentId => Abstractions.AgentId.Antigravity;

    public override IReadOnlyList<ModelInfo> GetStaticModels() => [];
}
