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
