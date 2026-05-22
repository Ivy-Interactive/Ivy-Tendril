using Ivy.Tendril.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public record CostCalculation
{
    public int TotalTokens { get; init; }
    public double TotalCost { get; init; }
}

public class ModelPricingService
{
    private readonly ILogger<ModelPricingService> _logger;
    private readonly IAgentRunner _agentRunner;
    private readonly IModelPricingProvider _pricingProvider;

    public ModelPricingService(ILogger<ModelPricingService> logger, IAgentRunner agentRunner, IModelPricingProvider pricingProvider)
    {
        _logger = logger;
        _agentRunner = agentRunner;
        _pricingProvider = pricingProvider;
    }

    public CostCalculation CalculateSessionCost(string sessionId, string provider)
    {
        var costParser = _agentRunner.GetCostParser(provider);
        if (costParser == null)
        {
            _logger.LogDebug("No cost parser registered for provider '{Provider}'", provider);
            return new CostCalculation();
        }

        var sessionFiles = costParser.DiscoverSessionFiles();
        var sessionFile = sessionFiles.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f)
                .Contains(sessionId, StringComparison.OrdinalIgnoreCase));

        if (sessionFile == null)
        {
            _logger.LogDebug("Session file not found for session '{SessionId}' (provider: {Provider})", sessionId, provider);
            return new CostCalculation();
        }

        var result = costParser.Parse(sessionFile, _pricingProvider);

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
                    var subResult = costParser.Parse(subFile, _pricingProvider);
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
