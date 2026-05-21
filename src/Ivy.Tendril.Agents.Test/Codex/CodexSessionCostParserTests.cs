using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Codex;

namespace Ivy.Tendril.Agents.Test.Codex;

public class CodexSessionCostParserTests : IDisposable
{
    private readonly CodexSessionCostParser _parser = new();
    private readonly string _tempDir;

    public CodexSessionCostParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"codex_cost_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AgentId_IsCodex()
    {
        Assert.Equal(AgentId.Codex, _parser.AgentId);
    }

    [Fact]
    public void Parse_ThreadStarted_SetsStartedAtAndExtractsSessionId()
    {
        var file = CreateSessionFile("sess1", """
            {"type":"thread.started","thread_id":"thread_xyz"}
            """);

        var before = DateTimeOffset.UtcNow;
        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("thread_xyz", result.SessionId);
        Assert.NotNull(result.StartedAt);
        Assert.True(result.StartedAt >= before.AddSeconds(-1));
    }

    [Fact]
    public void Parse_TurnCompleted_WithUsage_AccumulatesTokens()
    {
        var file = CreateSessionFile("sess2", """
            {"type":"thread.started","thread_id":"t1"}
            {"type":"turn.completed","usage":{"input_tokens":100,"output_tokens":50,"cached_input_tokens":25}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(100, result.InputTokens);
        Assert.Equal(50, result.OutputTokens);
        Assert.Equal(25, result.CacheReadTokens);
    }

    [Fact]
    public void Parse_MultipleTurnCompleted_AccumulatesUsage()
    {
        var file = CreateSessionFile("sess3", """
            {"type":"thread.started","thread_id":"t1"}
            {"type":"turn.completed","usage":{"input_tokens":100,"output_tokens":50,"cached_input_tokens":10}}
            {"type":"turn.completed","usage":{"input_tokens":200,"output_tokens":75,"cached_input_tokens":30}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(300, result.InputTokens);
        Assert.Equal(125, result.OutputTokens);
        Assert.Equal(40, result.CacheReadTokens);
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
    public void Parse_InvalidJson_SkipsLines()
    {
        var file = CreateSessionFile("partial", """
            not json at all
            {"type":"thread.started","thread_id":"t1"}
            {invalid json
            {"type":"turn.completed","usage":{"input_tokens":50,"output_tokens":20}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("t1", result.SessionId);
        Assert.Equal(50, result.InputTokens);
        Assert.Equal(20, result.OutputTokens);
    }

    [Fact]
    public void Parse_WithModel_UsesPricingProvider()
    {
        var file = CreateSessionFile("sess5", """
            {"type":"thread.started","thread_id":"t1"}
            {"type":"turn.completed","model":"o4-mini","usage":{"input_tokens":1000,"output_tokens":500}}
            """);

        var pricing = new FakePricingProvider(0.07m);
        var result = _parser.Parse(file, pricing);

        Assert.Equal("o4-mini", result.Model);
        Assert.Equal(0.07m, result.TotalCostUsd);
    }

    [Fact]
    public void Parse_NoModel_ZeroCost()
    {
        var file = CreateSessionFile("nomodel", """
            {"type":"thread.started","thread_id":"t1"}
            {"type":"turn.completed","usage":{"input_tokens":100,"output_tokens":50}}
            """);

        var result = _parser.Parse(file, new FakePricingProvider(1.00m));

        Assert.Null(result.Model);
        Assert.Equal(0m, result.TotalCostUsd);
    }

    [Fact]
    public void Parse_SetsCompletedAt_OnTurnCompleted()
    {
        var file = CreateSessionFile("completed", """
            {"type":"thread.started","thread_id":"t1"}
            {"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":5}}
            """);

        var before = DateTimeOffset.UtcNow;
        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.NotNull(result.CompletedAt);
        Assert.True(result.CompletedAt >= before.AddSeconds(-1));
    }

    [Fact]
    public void DiscoverSessionFiles_FindsJsonlFiles()
    {
        CreateSessionFile("a", "{}");
        CreateSessionFile("b", "{}");

        var files = _parser.DiscoverSessionFiles(_tempDir);

        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.EndsWith(".jsonl", f));
    }

    [Fact]
    public void DiscoverSessionFiles_NonExistentPath_ReturnsEmpty()
    {
        var files = _parser.DiscoverSessionFiles(Path.Combine(_tempDir, "nonexistent"));
        Assert.Empty(files);
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
    public void DiscoverSessionFiles_FindsInSubdirectories()
    {
        var subDir = Path.Combine(_tempDir, "sub1", "sub2");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "deep.jsonl"), "{}");

        var files = _parser.DiscoverSessionFiles(_tempDir);

        Assert.Single(files);
        Assert.Contains("deep.jsonl", files[0]);
    }

    [Fact]
    public void Parse_NoTypeProperty_SkipsLine()
    {
        var file = CreateSessionFile("notype", """
            {"foo":"bar"}
            {"type":"thread.started","thread_id":"t1"}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("t1", result.SessionId);
    }

    [Fact]
    public void Parse_TurnCompleted_PartialUsage_HandlesGracefully()
    {
        var file = CreateSessionFile("partial_usage", """
            {"type":"thread.started","thread_id":"t1"}
            {"type":"turn.completed","usage":{"input_tokens":100}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(100, result.InputTokens);
        Assert.Equal(0, result.OutputTokens);
        Assert.Equal(0, result.CacheReadTokens);
    }

    private string CreateSessionFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, $"{name}.jsonl");
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
