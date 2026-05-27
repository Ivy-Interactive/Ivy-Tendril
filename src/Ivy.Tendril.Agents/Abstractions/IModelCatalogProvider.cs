namespace Ivy.Tendril.Agents.Abstractions;

public interface IModelCatalogProvider
{
    string AgentId { get; }
    Task<ModelCatalogResult> GetModelsAsync(CancellationToken ct = default);
    IReadOnlyList<ModelInfo> GetStaticModels();
}
