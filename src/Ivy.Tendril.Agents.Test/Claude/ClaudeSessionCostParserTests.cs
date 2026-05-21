using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Claude;

public class ClaudeSessionCostParserTests : IDisposable
{
    private readonly ClaudeSessionCostParser _parser = new();
    private readonly string _tempDir;

    public ClaudeSessionCostParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"claude_cost_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AgentId_IsClaude()
    {
        Assert.Equal(AgentId.Claude, _parser.AgentId);
    }

    [Fact]
    public void Parse_ResultWithCost_ExtractsCost()
    {
        var file = CreateSessionFile("session1", """
            {"type":"system","subtype":"init","model":"claude-sonnet-4-5-20250514"}
            {"type":"result","total_cost_usd":0.0042,"usage":{"input_tokens":100,"output_tokens":50}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("session1", result.SessionId);
        Assert.Equal(AgentId.Claude, result.AgentId);
        Assert.Equal("claude-sonnet-4-5-20250514", result.Model);
        Assert.Equal(0.0042m, result.TotalCostUsd);
        Assert.Equal(100, result.InputTokens);
        Assert.Equal(50, result.OutputTokens);
    }

    [Fact]
    public void Parse_ResultWithCacheTokens_ExtractsCacheTokens()
    {
        var file = CreateSessionFile("session2", """
            {"type":"system","subtype":"init","model":"claude-opus-4-20250514"}
            {"type":"result","total_cost_usd":0.15,"usage":{"input_tokens":1000,"output_tokens":500,"cache_read_input_tokens":200,"cache_creation_input_tokens":300}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(200, result.CacheReadTokens);
        Assert.Equal(300, result.CacheWriteTokens);
    }

    [Fact]
    public void Parse_AssistantMessages_AccumulatesUsage()
    {
        var file = CreateSessionFile("session3", """
            {"type":"system","subtype":"init","model":"claude-sonnet-4-5-20250514"}
            {"type":"assistant","message":{"id":"msg_1","usage":{"input_tokens":100,"output_tokens":50},"model":"claude-sonnet-4-5-20250514"}}
            {"type":"assistant","message":{"id":"msg_2","usage":{"input_tokens":200,"output_tokens":75}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(300, result.InputTokens);
        Assert.Equal(125, result.OutputTokens);
    }

    [Fact]
    public void Parse_DuplicateMessageIds_DeduplicatesUsage()
    {
        // Claude outputs one JSONL line per content block, each with the same message ID and usage
        var file = CreateSessionFile("dedup", """
            {"type":"system","subtype":"init","model":"claude-sonnet-4-5-20250514"}
            {"type":"assistant","message":{"id":"msg_abc","content":[{"type":"text","text":"hello"}],"usage":{"input_tokens":100,"output_tokens":50}}}
            {"type":"assistant","message":{"id":"msg_abc","content":[{"type":"tool_use","id":"tu_1","name":"Read"}],"usage":{"input_tokens":100,"output_tokens":50}}}
            {"type":"assistant","message":{"id":"msg_abc","content":[{"type":"tool_result","tool_use_id":"tu_1"}],"usage":{"input_tokens":100,"output_tokens":50}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        // Should count only once, not 3x
        Assert.Equal(100, result.InputTokens);
        Assert.Equal(50, result.OutputTokens);
    }

    [Fact]
    public void Parse_CacheCreationNestedFormat_ExtractsCacheWriteTokens()
    {
        var file = CreateSessionFile("cache_nested", """
            {"type":"system","subtype":"init","model":"claude-opus-4-20250514"}
            {"type":"assistant","message":{"id":"msg_1","usage":{"input_tokens":500,"output_tokens":100,"cache_read_input_tokens":200,"cache_creation":{"ephemeral_5m_input_tokens":150,"ephemeral_1h_input_tokens":80}}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(200, result.CacheReadTokens);
        Assert.Equal(230, result.CacheWriteTokens); // 150 + 80
    }

    [Fact]
    public void Parse_CacheCreationFlatFormat_ExtractsCacheWriteTokens()
    {
        var file = CreateSessionFile("cache_flat", """
            {"type":"system","subtype":"init","model":"claude-opus-4-20250514"}
            {"type":"assistant","message":{"id":"msg_1","usage":{"input_tokens":500,"output_tokens":100,"cache_creation_input_tokens":300}}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(300, result.CacheWriteTokens);
    }

    [Fact]
    public void Parse_ResultOverridesAccumulated_WhenPresent()
    {
        var file = CreateSessionFile("session4", """
            {"type":"system","subtype":"init","model":"claude-sonnet-4-5-20250514"}
            {"type":"assistant","message":{"usage":{"input_tokens":100,"output_tokens":50}}}
            {"type":"result","total_cost_usd":0.01,"usage":{"input_tokens":500,"output_tokens":250}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(500, result.InputTokens);
        Assert.Equal(250, result.OutputTokens);
    }

    [Fact]
    public void Parse_NoCost_UsesPricingProvider()
    {
        var file = CreateSessionFile("session5", """
            {"type":"system","subtype":"init","model":"test-model"}
            {"type":"assistant","message":{"usage":{"input_tokens":1000,"output_tokens":500},"model":"test-model"}}
            """);

        var pricing = new FakePricingProvider(0.05m);
        var result = _parser.Parse(file, pricing);

        Assert.Equal(0.05m, result.TotalCostUsd);
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
        Assert.Equal(0m, result.TotalCostUsd);
    }

    [Fact]
    public void Parse_InvalidJson_SkipsLines()
    {
        var file = CreateSessionFile("partial", """
            not json at all
            {"type":"system","subtype":"init","model":"claude-sonnet-4-5-20250514"}
            {invalid json
            {"type":"result","total_cost_usd":0.01}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("claude-sonnet-4-5-20250514", result.Model);
        Assert.Equal(0.01m, result.TotalCostUsd);
    }

    [Fact]
    public void Parse_NoTypeProperty_SkipsLine()
    {
        var file = CreateSessionFile("notype", """
            {"foo":"bar"}
            {"type":"system","subtype":"init","model":"test-model"}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("test-model", result.Model);
    }

    [Fact]
    public void Parse_ModelFromAssistantMessage_WhenNoInit()
    {
        var file = CreateSessionFile("noInit", """
            {"type":"assistant","message":{"usage":{"input_tokens":50,"output_tokens":25},"model":"claude-haiku-3"}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("claude-haiku-3", result.Model);
    }

    [Fact]
    public void Parse_SetsStartedAt_OnInit()
    {
        var file = CreateSessionFile("timed", """
            {"type":"system","subtype":"init","model":"test"}
            """);

        var before = DateTimeOffset.UtcNow;
        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.NotNull(result.StartedAt);
        Assert.True(result.StartedAt >= before.AddSeconds(-1));
    }

    [Fact]
    public void Parse_SetsCompletedAt_OnResult()
    {
        var file = CreateSessionFile("completed", """
            {"type":"system","subtype":"init","model":"test"}
            {"type":"result","total_cost_usd":0.001}
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
