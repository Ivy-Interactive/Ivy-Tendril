using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiSessionCostParserTests
{
    private readonly GeminiSessionCostParser _parser = new();

    [Fact]
    public void AgentId_IsGemini()
    {
        Assert.Equal(AgentId.Gemini, _parser.AgentId);
    }

    [Fact]
    public void DiscoverSessionFiles_MissingDirectory_ReturnsEmpty()
    {
        var files = _parser.DiscoverSessionFiles("/nonexistent/path");
        Assert.Empty(files);
    }

    [Fact]
    public void Parse_ValidJson_ExtractsTokens()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "stats": {
                    "models": [
                        {"model": "gemini-2.5-pro", "prompt": 1500, "candidates": 800, "cacheRead": 200}
                    ]
                }
            }
            """);

            var pricing = new TestPricingProvider();
            var result = _parser.Parse(tempFile, pricing);

            Assert.Equal(1500, result.InputTokens);
            Assert.Equal(800, result.OutputTokens);
            Assert.Equal(200, result.CacheReadTokens);
            Assert.Equal("gemini-2.5-pro", result.Model);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_MultipleModelEntries_AggregatesTokens()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "stats": {
                    "models": [
                        {"model": "gemini-2.5-pro", "prompt": 500, "candidates": 200, "cacheRead": 100},
                        {"model": "gemini-2.5-pro", "prompt": 300, "candidates": 150, "cacheRead": 50}
                    ]
                }
            }
            """);

            var pricing = new TestPricingProvider();
            var result = _parser.Parse(tempFile, pricing);

            Assert.Equal(800, result.InputTokens);
            Assert.Equal(350, result.OutputTokens);
            Assert.Equal(150, result.CacheReadTokens);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsPartialResult()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not valid json at all");

            var pricing = new TestPricingProvider();
            var result = _parser.Parse(tempFile, pricing);

            Assert.Equal(Path.GetFileNameWithoutExtension(tempFile), result.SessionId);
            Assert.Equal(AgentId.Gemini, result.AgentId);
            Assert.Equal(0, result.InputTokens);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_MissingStats_ReturnsPartialResult()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """{"response": "hello"}""");

            var pricing = new TestPricingProvider();
            var result = _parser.Parse(tempFile, pricing);

            Assert.Equal(0, result.InputTokens);
            Assert.Null(result.Model);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_SessionIdFromFileName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "my-session-id.json");
        try
        {
            File.WriteAllText(tempFile, """{"stats":{"models":[]}}""");

            var pricing = new TestPricingProvider();
            var result = _parser.Parse(tempFile, pricing);

            Assert.Equal("my-session-id", result.SessionId);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir);
        }
    }

    private sealed class TestPricingProvider : IModelPricingProvider
    {
        public ModelPricing? GetPricing(string model) => new()
        {
            Model = model,
            InputPerMillion = 1.25m,
            OutputPerMillion = 10.00m,
            CacheReadPerMillion = 0.315m,
        };

        public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0)
        {
            var p = GetPricing(modelName);
            if (p is null) return 0;
            return (inputTokens * p.InputPerMillion / 1_000_000m) +
                   (outputTokens * p.OutputPerMillion / 1_000_000m) +
                   (cacheReadTokens * p.CacheReadPerMillion / 1_000_000m);
        }
    }
}
