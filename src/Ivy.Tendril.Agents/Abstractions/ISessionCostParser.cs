namespace Ivy.Tendril.Agents.Abstractions;

public interface ISessionCostParser
{
    string AgentId { get; }
    SessionCostResult Parse(string filePath, IModelPricingProvider pricing);
    IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null);
}

public interface IModelPricingProvider
{
    ModelPricing? GetPricing(string modelName);
    decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0);
}

/// <summary>
/// Refreshes a pricing provider's rates from models.dev. Separate from
/// <see cref="IModelPricingProvider" /> because reading a rate must stay synchronous — the
/// provider is constructed from the hardcoded catalogs and warmed up afterwards, never on the
/// hot path of a cost calculation.
/// </summary>
public interface IModelPricingRefresher
{
    /// <summary>Returns how many pricing entries changed.</summary>
    Task<int> RefreshFromModelsDevAsync(CancellationToken ct = default);
}
