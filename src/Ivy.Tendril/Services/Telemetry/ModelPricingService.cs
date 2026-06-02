using Ivy.Tendril.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Telemetry;

public record CostCalculation
{
    public int TotalTokens { get; init; }
    public double TotalCost { get; init; }
}

public class ModelPricingService(
    ILogger<ModelPricingService> logger,
    IAgentRunner agentRunner,
    IModelPricingProvider pricingProvider)
{
    public CostCalculation CalculateSessionCost(string sessionId, string provider)
    {
        var costParser = agentRunner.GetCostParser(provider);
        if (costParser == null)
        {
            logger.LogDebug("No cost parser registered for provider '{Provider}'", provider);
            return new CostCalculation();
        }

        var sessionFiles = costParser.DiscoverSessionFiles();
        var sessionFile = sessionFiles.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f)
                .Contains(sessionId, StringComparison.OrdinalIgnoreCase));

        if (sessionFile == null)
        {
            logger.LogDebug("Session file not found for session '{SessionId}' (provider: {Provider})", sessionId, provider);
            return new CostCalculation();
        }

        var result = costParser.Parse(sessionFile, pricingProvider);

        // For Claude, also parse subagent sessions
        if (provider.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            var subagentDir = Path.Combine(
                Path.GetDirectoryName(sessionFile)!,
                Path.GetFileNameWithoutExtension(sessionFile),
                "subagents");

            if (Directory.Exists(subagentDir))
            {
                foreach (var subFile in Directory.GetFiles(subagentDir, "*.jsonl"))
                {
                    var subResult = costParser.Parse(subFile, pricingProvider);
                    result = result with
                    {
                        InputTokens = result.InputTokens + subResult.InputTokens,
                        OutputTokens = result.OutputTokens + subResult.OutputTokens,
                        CacheReadTokens = result.CacheReadTokens + subResult.CacheReadTokens,
                        CacheWriteTokens = result.CacheWriteTokens + subResult.CacheWriteTokens,
                        TotalCostUsd = result.TotalCostUsd + subResult.TotalCostUsd,
                    };
                }
            }
        }

        return new CostCalculation
        {
            TotalTokens = result.InputTokens + result.OutputTokens,
            TotalCost = (double)result.TotalCostUsd,
        };
    }
}
