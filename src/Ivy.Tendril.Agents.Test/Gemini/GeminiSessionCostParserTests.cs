using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiSessionCostParserTests : IDisposable
{
    private readonly GeminiSessionCostParser _parser = new();
    private readonly string _tempDir;

    public GeminiSessionCostParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gemini_cost_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AgentId_IsGemini()
    {
        Assert.Equal(AgentId.Gemini, _parser.AgentId);
    }

    [Fact]
    public void Parse_ValidJson_ExtractsModelAndTokens()
    {
        var file = CreateSessionFile("session1", """
            {
                "session_id": "ses-abc",
                "stats": {
                    "models": {
                        "gemini-2.5-pro": {
                            "tokens": {
                                "input": 500,
                                "candidates": 200,
                                "cached": 100
                            }
                        }
                    }
                }
            }
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("ses-abc", result.SessionId);
        Assert.Equal(AgentId.Gemini, result.AgentId);
        Assert.Equal("gemini-2.5-pro", result.Model);
        Assert.Equal(500, result.InputTokens);
        Assert.Equal(200, result.OutputTokens);
        Assert.Equal(100, result.CacheReadTokens);
    }

    [Fact]
    public void Parse_WithSessionId_UsesAsSessionId()
    {
        var file = CreateSessionFile("filename-id", """
            {
                "session_id": "override-id",
                "stats": { "models": {} }
            }
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("override-id", result.SessionId);
    }

    [Fact]
    public void Parse_NoSessionId_UsesFilenameAsSessionId()
    {
        var file = CreateSessionFile("my-file-name", """
            {
                "stats": { "models": {} }
            }
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("my-file-name", result.SessionId);
    }

    [Fact]
    public void Parse_EmptyFile_ReturnsDefaults()
    {
        var file = CreateSessionFile("empty", "");

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("empty", result.SessionId);
        Assert.Null(result.Model);
        Assert.Equal(0, result.InputTokens);
        Assert.Equal(0, result.OutputTokens);
        Assert.Equal(0, result.CacheReadTokens);
        Assert.Equal(0m, result.TotalCostUsd);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsPartialResult_NoCrash()
    {
        var file = CreateSessionFile("bad", "{not valid json!!");

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("bad", result.SessionId);
        Assert.Null(result.Model);
        Assert.Equal(0, result.InputTokens);
    }

    [Fact]
    public void Parse_MultipleModels_AggregatesTokens()
    {
        var file = CreateSessionFile("multi", """
            {
                "stats": {
                    "models": {
                        "gemini-2.5-pro": {
                            "tokens": { "input": 100, "candidates": 50, "cached": 10 }
                        },
                        "gemini-2.5-flash": {
                            "tokens": { "input": 200, "candidates": 75, "cached": 20 }
                        }
                    }
                }
            }
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(300, result.InputTokens);
        Assert.Equal(125, result.OutputTokens);
        Assert.Equal(30, result.CacheReadTokens);
        // Model should be the first one encountered
        Assert.Equal("gemini-2.5-pro", result.Model);
    }

    [Fact]
    public void Parse_UsesPricingProvider_ToCalculateCost()
    {
        var file = CreateSessionFile("costed", """
            {
                "stats": {
                    "models": {
                        "gemini-2.5-pro": {
                            "tokens": { "input": 1000, "candidates": 500 }
                        }
                    }
                }
            }
            """);

        var pricing = new FakePricingProvider(0.042m);
        var result = _parser.Parse(file, pricing);

        Assert.Equal(0.042m, result.TotalCostUsd);
    }

    [Fact]
    public void Parse_NullModel_DoesNotCallPricing()
    {
        var file = CreateSessionFile("nomodel", """
            {
                "stats": { "models": {} }
            }
            """);

        var result = _parser.Parse(file, new FakePricingProvider(99m));

        Assert.Equal(0m, result.TotalCostUsd);
    }

    [Fact]
    public void DiscoverSessionFiles_FindsJsonFiles()
    {
        CreateSessionFile("a", "{}");
        CreateSessionFile("b", "{}");

        var files = _parser.DiscoverSessionFiles(_tempDir);

        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.EndsWith(".json", f));
    }

    [Fact]
    public void DiscoverSessionFiles_DoesNotFindJsonlFiles()
    {
        // Gemini uses .json, not .jsonl
        File.WriteAllText(Path.Combine(_tempDir, "should-not-find.jsonl"), "{}");
        CreateSessionFile("should-find", "{}");

        var files = _parser.DiscoverSessionFiles(_tempDir);

        Assert.Single(files);
        Assert.Contains("should-find.json", files[0]);
    }

    [Fact]
    public void DiscoverSessionFiles_OrdersByLastWriteDesc()
    {
        var older = CreateSessionFile("older", "{}");
        Thread.Sleep(50);
        var newer = CreateSessionFile("newer", "{}");

        var files = _parser.DiscoverSessionFiles(_tempDir);

        Assert.Equal(newer, files[0]);
        Assert.Equal(older, files[1]);
    }

    [Fact]
    public void DiscoverSessionFiles_NonExistentPath_ReturnsEmpty()
    {
        var files = _parser.DiscoverSessionFiles(Path.Combine(_tempDir, "nonexistent"));
        Assert.Empty(files);
    }

    [Fact]
    public void DiscoverSessionFiles_FindsInSubdirectories()
    {
        var subDir = Path.Combine(_tempDir, "sub1", "sub2");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "deep.json"), "{}");

        var files = _parser.DiscoverSessionFiles(_tempDir);

        Assert.Single(files);
        Assert.Contains("deep.json", files[0]);
    }

    private string CreateSessionFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, $"{name}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class NullPricingProvider : IModelPricingProvider
    {
        public ModelPricing? GetPricing(string modelName) => null;
        public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0) => 0m;
    }

    private sealed class FakePricingProvider(decimal fixedCost) : IModelPricingProvider
    {
        public ModelPricing? GetPricing(string modelName) => new()
        {
            Model = modelName,
            InputPerMillion = 1m,
            OutputPerMillion = 1m,
        };

        public decimal CalculateCost(string modelName, int inputTokens, int outputTokens, int cacheReadTokens = 0, int cacheWriteTokens = 0) => fixedCost;
    }
}
