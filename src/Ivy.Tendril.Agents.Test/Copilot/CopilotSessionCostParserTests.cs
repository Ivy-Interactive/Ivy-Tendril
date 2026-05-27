using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Copilot;

namespace Ivy.Tendril.Agents.Test.Copilot;

public class CopilotSessionCostParserTests : IDisposable
{
    private readonly CopilotSessionCostParser _parser = new();
    private readonly string _tempDir;

    public CopilotSessionCostParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"copilot_cost_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AgentId_IsCopilot()
    {
        Assert.Equal(AgentId.Copilot, _parser.AgentId);
    }

    [Fact]
    public void Parse_SessionToolsUpdated_SetsModelAndStartedAt()
    {
        var file = CreateSessionFile("session1", """
            {"type":"session.tools_updated","timestamp":"2025-05-20T10:00:00Z","data":{"model":"gpt-4o","tools":["view","apply_patch"]}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("session1", result.SessionId);
        Assert.Equal(AgentId.Copilot, result.AgentId);
        Assert.Equal("gpt-4o", result.Model);
        Assert.NotNull(result.StartedAt);
        Assert.Equal(2025, result.StartedAt!.Value.Year);
        Assert.Equal(5, result.StartedAt!.Value.Month);
        Assert.Equal(20, result.StartedAt!.Value.Day);
    }

    [Fact]
    public void Parse_AssistantMessage_WithOutputTokens_Accumulates()
    {
        var file = CreateSessionFile("session2", """
            {"type":"session.tools_updated","data":{"model":"gpt-4o"}}
            {"type":"assistant.message","data":{"content":"hello","outputTokens":50}}
            {"type":"assistant.message","data":{"content":"world","outputTokens":30}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(80, result.OutputTokens);
    }

    [Fact]
    public void Parse_Result_WithPremiumRequestsAndDuration()
    {
        var file = CreateSessionFile("session3", """
            {"type":"session.tools_updated","data":{"model":"gpt-4o"}}
            {"type":"result","timestamp":"2025-05-20T10:05:00Z","usage":{"premiumRequests":7,"sessionDurationMs":30000}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public void Parse_EphemeralLines_AreSkipped()
    {
        var file = CreateSessionFile("session4", """
            {"type":"assistant.message","ephemeral":true,"data":{"content":"streaming","outputTokens":100}}
            {"type":"session.tools_updated","data":{"model":"gpt-4o"}}
            {"type":"assistant.message","data":{"content":"final","outputTokens":20}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(20, result.OutputTokens);
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
        Assert.Null(result.StartedAt);
        Assert.Null(result.CompletedAt);
    }

    [Fact]
    public void Parse_TotalCostUsd_AlwaysZero()
    {
        var file = CreateSessionFile("cost", """
            {"type":"session.tools_updated","data":{"model":"gpt-4o"}}
            {"type":"assistant.message","data":{"outputTokens":5000}}
            {"type":"result","usage":{"premiumRequests":20,"sessionDurationMs":60000}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal(0m, result.TotalCostUsd);
    }

    [Fact]
    public void Parse_InvalidJson_SkipsLines()
    {
        var file = CreateSessionFile("partial", """
            not json at all
            {"type":"session.tools_updated","data":{"model":"gpt-4o"}}
            {invalid json
            {"type":"assistant.message","data":{"outputTokens":10}}
            """);

        var result = _parser.Parse(file, new NullPricingProvider());

        Assert.Equal("gpt-4o", result.Model);
        Assert.Equal(10, result.OutputTokens);
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
