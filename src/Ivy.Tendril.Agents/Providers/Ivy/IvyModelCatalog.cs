using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvyModelCatalog : IModelCatalogProvider
{
    private static readonly ClaudeModelCatalog ClaudeCatalog = new();
    private static readonly GeminiModelCatalog GeminiCatalog = new();
    private static readonly CodexModelCatalog CodexCatalog = new();

    public string AgentId => Abstractions.AgentId.Ivy;

    public IReadOnlyList<ModelInfo> GetStaticModels() =>
    [
        .. ClaudeCatalog.GetStaticModels(),
        .. GeminiCatalog.GetStaticModels(),
        .. CodexCatalog.GetStaticModels(),
    ];

    public Task<ModelCatalogResult> GetModelsAsync(CancellationToken ct = default)
    {
        var staticModels = GetStaticModels();
        return Task.FromResult(new ModelCatalogResult
        {
            AgentId = AgentId,
            Models = staticModels,
            Source = ModelCatalogSource.Static,
            RetrievedAt = DateTimeOffset.UtcNow,
        });
    }
}
