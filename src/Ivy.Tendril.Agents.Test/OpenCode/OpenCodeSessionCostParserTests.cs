using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.OpenCode;

public class OpenCodeSessionCostParserTests : IDisposable
{
    private readonly OpenCodeSessionCostParser _parser = new();
    private readonly string _tempDir;

    public OpenCodeSessionCostParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"opencode_cost_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AgentId_IsOpenCode()
    {
        Assert.Equal(AgentId.OpenCode, _parser.AgentId);
    }

    [Fact]
    public void Parse_StepStart_SetsStartedAt()
    {
        var file = CreateSessionFile("session1", """
            {"type":"step_start","sessionID":"sess-1"}
            """);

        var before = DateTimeOffset.UtcNow;
        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.NotNull(result.StartedAt);
        Assert.True(result.StartedAt >= before.AddSeconds(-1));
    }

    [Fact]
    public void Parse_StepFinish_WithCostAndTokens_Accumulates()
    {
        var file = CreateSessionFile("session2", """
            {"type":"step_start","sessionID":"sess-2"}
            {"type":"step_finish","part":{"reason":"stop","cost":0.0042,"tokens":{"input":1000,"output":500}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("session2", result.SessionId);
        Assert.Equal(AgentId.OpenCode, result.AgentId);
        Assert.Equal(0.0042m, result.TotalCostUsd);
        Assert.Equal(1000, result.InputTokens);
        Assert.Equal(500, result.OutputTokens);
        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public void Parse_MultipleStepFinish_AccumulatesCostAndTokens()
    {
        var file = CreateSessionFile("session3", """
            {"type":"step_start","sessionID":"sess-3"}
            {"type":"step_finish","part":{"reason":"stop","cost":0.01,"tokens":{"input":1000,"output":200}}}
            {"type":"step_finish","part":{"reason":"stop","cost":0.02,"tokens":{"input":2000,"output":300}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(0.03m, result.TotalCostUsd);
        Assert.Equal(3000, result.InputTokens);
        Assert.Equal(500, result.OutputTokens);
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
        Assert.Equal(0, result.CacheWriteTokens);
        Assert.Equal(0m, result.TotalCostUsd);
        Assert.Null(result.StartedAt);
        Assert.Null(result.CompletedAt);
    }

    [Fact]
    public void Parse_InvalidJson_SkipsLines()
    {
        var file = CreateSessionFile("partial", """
            not json at all
            {invalid json
            {"type":"step_start","sessionID":"x"}
            {"type":"step_finish","part":{"reason":"stop","cost":0.005,"tokens":{"input":100,"output":50}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(0.005m, result.TotalCostUsd);
        Assert.Equal(100, result.InputTokens);
        Assert.Equal(50, result.OutputTokens);
    }

    [Fact]
    public void Parse_NoTypeProperty_SkipsLine()
    {
        var file = CreateSessionFile("notype", """
            {"foo":"bar"}
            {"type":"step_finish","part":{"reason":"stop","cost":0.001,"tokens":{"input":10,"output":5}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(0.001m, result.TotalCostUsd);
    }

    [Fact]
    public void Parse_SessionIdFromFileName()
    {
        var file = CreateSessionFile("my-session-id", """
            {"type":"step_start","sessionID":"sess-xyz"}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("my-session-id", result.SessionId);
    }

    [Fact]
    public void Parse_CostIsZero_ModelIsNull_NoPricingFallback()
    {
        // model is never set from JSONL in current impl, so pricing fallback won't trigger
        var file = CreateSessionFile("nocost", """
            {"type":"step_start","sessionID":"x"}
            {"type":"step_finish","part":{"reason":"stop","tokens":{"input":100,"output":50}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(0m, result.TotalCostUsd);
        Assert.Null(result.Model);
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
}
