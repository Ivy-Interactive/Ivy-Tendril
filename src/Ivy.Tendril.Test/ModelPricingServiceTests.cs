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

    [Fact]
    public void CalculateSessionCost_MatchingFile_SurfacesTokenBucketsAndModel()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"session-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(tempFile, "");

        try
        {
            var costResult = new SessionCostResult
            {
                SessionId = "abc123",
                AgentId = "claude",
                Model = "claude-opus-5",
                InputTokens = 1000,
                OutputTokens = 500,
                CacheReadTokens = 90_000,
                CacheWriteTokens = 300,
                TotalCostUsd = 0.025m,
            };
            var parser = new FakeCostParser(files: [tempFile], parseResult: costResult);
            var service = new ModelPricingService(
                NullLogger<ModelPricingService>.Instance,
                new FakeAgentRunner(costParser: parser),
                new FakePricingProvider());

            var result = service.CalculateSessionCost(
                Path.GetFileNameWithoutExtension(tempFile), "claude");

            Assert.Equal(1000, result.InputTokens);
            Assert.Equal(500, result.OutputTokens);
            Assert.Equal(90_000, result.CacheReadTokens);
            Assert.Equal(300, result.CacheWriteTokens);
            Assert.Equal("claude-opus-5", result.Model);
            // TotalTokens stays input + output — cache traffic is reported but not counted here.
            Assert.Equal(1500, result.TotalTokens);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CalculateSessionCost_ClaudeSubagents_SumsBucketsAcrossFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tendril-cost-{Guid.NewGuid():N}");
        var sessionName = "session-subagents";
        var sessionFile = Path.Combine(root, $"{sessionName}.jsonl");
        var subagentDir = Path.Combine(root, sessionName, "subagents");
        Directory.CreateDirectory(subagentDir);
        File.WriteAllText(sessionFile, "");

        var sub1 = Path.Combine(subagentDir, "sub-1.jsonl");
        var sub2 = Path.Combine(subagentDir, "sub-2.jsonl");
        File.WriteAllText(sub1, "");
        File.WriteAllText(sub2, "");

        try
        {
            var perFile = new Dictionary<string, SessionCostResult>
            {
                [$"{sessionName}.jsonl"] = new()
                {
                    SessionId = sessionName,
                    AgentId = "claude",
                    Model = "claude-opus-5",
                    InputTokens = 1000,
                    OutputTokens = 500,
                    CacheReadTokens = 10_000,
                    CacheWriteTokens = 100,
                    TotalCostUsd = 0.02m,
                },
                ["sub-1.jsonl"] = new()
                {
                    SessionId = "sub-1",
                    AgentId = "claude",
                    InputTokens = 200,
                    OutputTokens = 50,
                    CacheReadTokens = 5_000,
                    CacheWriteTokens = 20,
                    TotalCostUsd = 0.005m,
                },
                ["sub-2.jsonl"] = new()
                {
                    SessionId = "sub-2",
                    AgentId = "claude",
                    InputTokens = 300,
                    OutputTokens = 70,
                    CacheReadTokens = 7_000,
                    CacheWriteTokens = 30,
                    TotalCostUsd = 0.007m,
                },
            };

            var parser = new FakeCostParser(files: [sessionFile], perFile: perFile);
            var service = new ModelPricingService(
                NullLogger<ModelPricingService>.Instance,
                new FakeAgentRunner(costParser: parser),
                new FakePricingProvider());

            var result = service.CalculateSessionCost(sessionName, "claude");

            Assert.Equal(1500, result.InputTokens);
            Assert.Equal(620, result.OutputTokens);
            Assert.Equal(22_000, result.CacheReadTokens);
            Assert.Equal(150, result.CacheWriteTokens);
            Assert.Equal(2120, result.TotalTokens);
            Assert.Equal(0.032, result.TotalCost, 6);
            Assert.Equal("claude-opus-5", result.Model);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
        public IModelCatalogProvider? GetModelCatalog(string agentId) => null;
        public IEnumerable<IModelCatalogProvider> ModelCatalogs => [];
        public Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task StopAllAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private class FakeCostParser(
        IReadOnlyList<string>? files = null,
        SessionCostResult? parseResult = null,
        IReadOnlyDictionary<string, SessionCostResult>? perFile = null) : ISessionCostParser
    {
        public string AgentId => "claude";

        public IReadOnlyList<string> DiscoverSessionFiles(string? projectPath = null)
            => files ?? [];

        public SessionCostResult Parse(string filePath, IModelPricingProvider pricing)
            => perFile is not null && perFile.TryGetValue(Path.GetFileName(filePath), out var result)
                ? result
                : parseResult ?? new SessionCostResult { SessionId = "", AgentId = "claude" };
    }

    private class FakePricingProvider : IModelPricingProvider
    {
        public ModelPricing? GetPricing(string modelName) => null;
        public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0)
            => 0m;
    }
}
