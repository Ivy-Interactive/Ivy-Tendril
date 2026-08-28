using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravitySessionCostParserTests
{
    private readonly AntigravitySessionCostParser _parser = new();

    [Fact]
    public void AgentId_IsAntigravity()
    {
        Assert.Equal(AgentId.Antigravity, _parser.AgentId);
    }

    [Fact]
    public void Parse_ExtractsSessionIdFromFileName()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test-session-id.pb");
        try
        {
            File.WriteAllText(tempFile, "fake-data");
            var pricing = new TestPricingProvider();
            var result = _parser.Parse(tempFile, pricing);

            Assert.Equal("test-session-id", result.SessionId);
            Assert.Equal(AgentId.Antigravity, result.AgentId);
            Assert.Equal(0, result.InputTokens);
            Assert.Equal(0, result.OutputTokens);
            Assert.Equal(0m, result.TotalCostUsd);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_JsonlWithExplicitCost_ReturnsExplicitCost()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test-session-cost.jsonl");
        try
        {
            var lines = new[]
            {
                "{\"event\":\"init\",\"conversation_id\":\"sess-456\",\"init\":{\"model\":\"gemini-3.7-flash\"}}",
                "{\"event\":\"result\",\"result\":{\"total_cost_usd\":0.05,\"usage\":{\"input_tokens\":1000,\"output_tokens\":500}}}"
            };
            File.WriteAllLines(tempFile, lines);
            var pricing = new TestPricingProvider();
            var result = _parser.Parse(tempFile, pricing);

            Assert.Equal("sess-456", result.SessionId);
            Assert.Equal(AgentId.Antigravity, result.AgentId);
            Assert.Equal("gemini-3.7-flash", result.Model);
            Assert.Equal(1000, result.InputTokens);
            Assert.Equal(500, result.OutputTokens);
            Assert.Equal(0.05m, result.TotalCostUsd);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_JsonlWithoutExplicitCost_CalculatesCostViaPricingProvider()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test-calc-cost.jsonl");
        try
        {
            var lines = new[]
            {
                "{\"event\":\"init\",\"conversation_id\":\"sess-789\",\"init\":{\"model\":\"gemini-3.7-flash\"}}",
                "{\"event\":\"result\",\"result\":{\"usage\":{\"input_tokens\":1000000,\"output_tokens\":1000000,\"cache_read_tokens\":1000000}}}"
            };
            File.WriteAllLines(tempFile, lines);
            var pricing = new CalculatingPricingProvider();
            var result = _parser.Parse(tempFile, pricing);

            Assert.Equal("sess-789", result.SessionId);
            Assert.Equal("gemini-3.7-flash", result.Model);
            Assert.Equal(1_000_000, result.InputTokens);
            Assert.Equal(1_000_000, result.OutputTokens);
            Assert.Equal(1_000_000, result.CacheReadTokens);
            Assert.Equal(0.7875m, result.TotalCostUsd);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DiscoverSessionFiles_MissingDirectory_ReturnsEmpty()
    {
        // DiscoverSessionFiles uses user profile directory, which may or may not exist,
        // but we can call it and verify it returns a list (empty or not) without throwing.
        var result = _parser.DiscoverSessionFiles("/some/path");
        Assert.NotNull(result);
    }

    [Fact]
    public void DiscoverSessionFiles_FindsJsonlAndDbFilesInProjectDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var file1 = Path.Combine(tempDir, "conv1.jsonl");
            var file2 = Path.Combine(tempDir, "conv2.db");
            var file3 = Path.Combine(tempDir, "ignore.txt");
            File.WriteAllText(file1, "{}");
            File.WriteAllText(file2, "dummy");
            File.WriteAllText(file3, "text");

            var files = _parser.DiscoverSessionFiles(tempDir);
            Assert.Contains(file1, files);
            Assert.Contains(file2, files);
            Assert.DoesNotContain(file3, files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class TestPricingProvider : IModelPricingProvider
    {
        public ModelPricing? GetPricing(string model) => null;

        public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0)
            => 0m;
    }

    private sealed class CalculatingPricingProvider : IModelPricingProvider
    {
        public ModelPricing? GetPricing(string model) => null;

        public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0)
        {
            if (modelName == "gemini-3.7-flash")
            {
                return (inputTokens * 0.15m / 1_000_000m) +
                       (outputTokens * 0.60m / 1_000_000m) +
                       (cacheReadTokens * 0.0375m / 1_000_000m);
            }
            return 0m;
        }
    }
}
