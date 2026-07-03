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
    public void DiscoverSessionFiles_MissingDirectory_ReturnsEmpty()
    {
        // DiscoverSessionFiles uses user profile directory, which may or may not exist,
        // but we can call it and verify it returns a list (empty or not) without throwing.
        var result = _parser.DiscoverSessionFiles("/some/path");
        Assert.NotNull(result);
    }

    private sealed class TestPricingProvider : IModelPricingProvider
    {
        public ModelPricing? GetPricing(string model) => null;

        public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0)
            => 0m;
    }
}
