using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class ModelPricingServiceTests
{
    [Fact]
    public void CalculateSessionCost_NoParser_ReturnsEmpty()
    {
        var runner = new FakeAgentRunner(costParser: null);
        var service = new ModelPricingService(
            NullLogger<ModelPricingService>.Instance, runner, new FakePricingProvider());

        var result = service.CalculateSessionCost("session-123", "unknown");

        Assert.Equal(0, result.TotalTokens);
        Assert.Equal(0.0, result.TotalCost);
    }

    [Fact]
    public void CalculateSessionCost_NoMatchingSessionFile_ReturnsEmpty()
    {
        var parser = new FakeCostParser(files: ["other-session.jsonl"]);
        var runner = new FakeAgentRunner(costParser: parser);
        var service = new ModelPricingService(
            NullLogger<ModelPricingService>.Instance, runner, new FakePricingProvider());

        var result = service.CalculateSessionCost("session-123", "claude");

        Assert.Equal(0, result.TotalTokens);
        Assert.Equal(0.0, result.TotalCost);
    }

    [Fact]
    public void CalculateSessionCost_MatchingFile_ReturnsParsedCost()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "session-abc123.jsonl");
        File.WriteAllText(tempFile, "");

        try
        {
            var costResult = new SessionCostResult
            {
                SessionId = "abc123",
                AgentId = "claude",
                InputTokens = 1000,
                OutputTokens = 500,
                CacheReadTokens = 200,
                TotalCostUsd = 0.025m,
            };
            var parser = new FakeCostParser(files: [tempFile], parseResult: costResult);
            var runner = new FakeAgentRunner(costParser: parser);
            var service = new ModelPricingService(
                NullLogger<ModelPricingService>.Instance, runner, new FakePricingProvider());

            var result = service.CalculateSessionCost("abc123", "claude");

            Assert.Equal(1500, result.TotalTokens);
            Assert.Equal(0.025, result.TotalCost);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private class FakeAgentRunner(ISessionCostParser? costParser) : IAgentRunner
    {
        public IReadOnlyList<string> RegisteredAgents => [];
        public IReadOnlyList<IAgentSession> ActiveSessions => [];
        public IObservable<IAgentSession> Sessions => throw new NotImplementedException();

        public IAgentCli GetCli(string agentId) => throw new NotImplementedException();
        public IEventParser GetParser(string agentId) => throw new NotImplementedException();
        public IAgentHealthCheck GetHealthCheck(string agentId) => throw new NotImplementedException();
        public IAgentDescriptor GetDescriptor(string agentId) => throw new NotImplementedException();
        public IFailureAnalyzer? GetFailureAnalyzer(string agentId) => null;
        public ISessionCostParser? GetCostParser(string agentId) => costParser;
        public IAgentPty? GetPty(string agentId) => null;
        public Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task StopAllAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private class FakeCostParser(IReadOnlyList<string>? files = null, SessionCostResult? parseResult = null) : ISessionCostParser
    {
        public string AgentId => "claude";

        public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
            => files ?? [];

        public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
            => parseResult ?? new SessionCostResult { SessionId = "", AgentId = "claude" };
    }

    private class FakePricingProvider : IModelPricingProvider
    {
        public ModelPricing? GetPricing(string modelName) => null;
        public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0)
            => 0m;
    }
}
