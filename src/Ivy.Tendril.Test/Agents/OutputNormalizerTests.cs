using System.Text.Json.Nodes;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Agents;

public class OutputNormalizerTests
{
    private static string FixturePath(string filename) =>
        Path.Combine(System.AppContext.BaseDirectory, "TestData", "AgentOutput", filename);

    private static IReadOnlyList<string> NormalizeAll(IOutputNormalizer normalizer, string fixture)
    {
        var lines = File.ReadAllLines(FixturePath(fixture));
        var results = new List<string>();
        foreach (var line in lines)
            results.AddRange(normalizer.Normalize(line));
        results.AddRange(normalizer.Flush());
        return results;
    }

    private static JsonNode? ParseLine(string line)
    {
        try { return JsonNode.Parse(line); }
        catch { return null; }
    }

    // ─── Claude (pass-through) ──────────────────────────────────────────────

    [Fact]
    public void Claude_PassesThrough_Unchanged()
    {
        var normalizer = new ClaudeOutputNormalizer();
        var input = File.ReadAllLines(FixturePath("claude-sample.jsonl"));
        var output = NormalizeAll(normalizer, "claude-sample.jsonl");
        Assert.Equal(input.Length, output.Count);
    }

    // ─── OpenCode ────────────────────────────────────────────────────────────

    [Fact]
    public void OpenCode_EmitsInitEvent()
    {
        var normalizer = new OpenCodeOutputNormalizer();
        var output = NormalizeAll(normalizer, "opencode-sample.jsonl");

        var initLine = output.FirstOrDefault(l => l.Contains("\"subtype\":\"init\""));
        Assert.NotNull(initLine);
        var node = ParseLine(initLine);
        Assert.Equal("system", node?["type"]?.GetValue<string>());
        Assert.Equal("opencode", node?["model"]?.GetValue<string>());
    }

    [Fact]
    public void OpenCode_PreservesOriginalToolNames()
    {
        var normalizer = new OpenCodeOutputNormalizer();
        var output = NormalizeAll(normalizer, "opencode-sample.jsonl");

        var toolUseLines = output.Where(l => l.Contains("\"type\":\"tool_use\"")).ToList();
        Assert.NotEmpty(toolUseLines);

        foreach (var line in toolUseLines)
        {
            var node = ParseLine(line);
            var content = node?["message"]?["content"]?[0];
            var name = content?["name"]?.GetValue<string>();
            Assert.NotNull(name);
            Assert.NotEmpty(name);
        }
    }

    [Fact]
    public void OpenCode_EmitsToolResult_ForCompletedTools()
    {
        var normalizer = new OpenCodeOutputNormalizer();
        var output = NormalizeAll(normalizer, "opencode-sample.jsonl");

        var resultLines = output.Where(l => l.Contains("\"type\":\"tool_result\"")).ToList();
        Assert.NotEmpty(resultLines);
    }

    [Fact]
    public void OpenCode_EmitsTextEvent()
    {
        var normalizer = new OpenCodeOutputNormalizer();
        var output = NormalizeAll(normalizer, "opencode-sample.jsonl");

        var textLines = output.Where(l =>
            l.Contains("\"type\":\"text\"") && l.Contains("\"type\":\"assistant\"")).ToList();
        Assert.NotEmpty(textLines);
    }

    [Fact]
    public void OpenCode_EmitsResultOnStop()
    {
        var normalizer = new OpenCodeOutputNormalizer();
        var output = NormalizeAll(normalizer, "opencode-sample.jsonl");

        var resultLine = output.LastOrDefault(l => l.Contains("\"type\":\"result\""));
        Assert.NotNull(resultLine);
        var node = ParseLine(resultLine);
        Assert.Equal("success", node?["subtype"]?.GetValue<string>());
    }

    // ─── Gemini ──────────────────────────────────────────────────────────────

    [Fact]
    public void Gemini_EmitsInitEvent()
    {
        var normalizer = new GeminiOutputNormalizer();
        var output = NormalizeAll(normalizer, "gemini-sample.jsonl");

        var initLine = output.FirstOrDefault(l => l.Contains("\"subtype\":\"init\""));
        Assert.NotNull(initLine);
        var node = ParseLine(initLine);
        Assert.Equal("system", node?["type"]?.GetValue<string>());
        Assert.Contains("gemini", node?["model"]?.GetValue<string>());
    }

    [Fact]
    public void Gemini_SkipsUpdateTopicTool()
    {
        var normalizer = new GeminiOutputNormalizer();
        var output = NormalizeAll(normalizer, "gemini-sample.jsonl");

        var hasUpdateTopic = output.Any(l => l.Contains("update_topic"));
        Assert.False(hasUpdateTopic, "update_topic meta-tool should be filtered out");
    }

    [Fact]
    public void Gemini_PreservesOriginalToolNames()
    {
        var normalizer = new GeminiOutputNormalizer();
        var output = NormalizeAll(normalizer, "gemini-sample.jsonl");

        var toolUseLines = output.Where(l => l.Contains("\"type\":\"tool_use\"")).ToList();
        Assert.NotEmpty(toolUseLines);

        foreach (var line in toolUseLines)
        {
            var node = ParseLine(line);
            var content = node?["message"]?["content"]?[0];
            var name = content?["name"]?.GetValue<string>();
            Assert.NotNull(name);
            Assert.NotEmpty(name);
        }
    }

    [Fact]
    public void Gemini_AccumulatesDeltaText()
    {
        var normalizer = new GeminiOutputNormalizer();
        var output = NormalizeAll(normalizer, "gemini-sample.jsonl");

        // Should have at least one text block (accumulated from deltas)
        var textLines = output.Where(l =>
            l.Contains("\"type\":\"text\"") && l.Contains("\"type\":\"assistant\"")).ToList();
        Assert.NotEmpty(textLines);
    }

    [Fact]
    public void Gemini_EmitsResult()
    {
        var normalizer = new GeminiOutputNormalizer();
        var output = NormalizeAll(normalizer, "gemini-sample.jsonl");

        var resultLine = output.LastOrDefault(l =>
            l.Contains("\"type\":\"result\"") && l.Contains("\"subtype\""));
        Assert.NotNull(resultLine);
    }

    // ─── Codex ───────────────────────────────────────────────────────────────

    [Fact]
    public void Codex_EmitsInitEvent()
    {
        var normalizer = new CodexOutputNormalizer();
        var output = NormalizeAll(normalizer, "codex-sample.jsonl");

        var initLine = output.FirstOrDefault(l => l.Contains("\"subtype\":\"init\""));
        Assert.NotNull(initLine);
        var node = ParseLine(initLine);
        Assert.Equal("system", node?["type"]?.GetValue<string>());
        Assert.Equal("codex", node?["model"]?.GetValue<string>());
    }

    [Fact]
    public void Codex_MapCommandExecution_ToBash()
    {
        var normalizer = new CodexOutputNormalizer();
        var output = NormalizeAll(normalizer, "codex-sample.jsonl");

        var toolUseLines = output.Where(l => l.Contains("\"type\":\"tool_use\"")).ToList();
        Assert.NotEmpty(toolUseLines);

        foreach (var line in toolUseLines)
        {
            var node = ParseLine(line);
            var content = node?["message"]?["content"]?[0];
            Assert.Equal("Bash", content?["name"]?.GetValue<string>());
            Assert.NotNull(content?["input"]?["command"]?.GetValue<string>());
        }
    }

    [Fact]
    public void Codex_EmitsToolResult_WithOutput()
    {
        var normalizer = new CodexOutputNormalizer();
        var output = NormalizeAll(normalizer, "codex-sample.jsonl");

        var resultLines = output.Where(l => l.Contains("\"type\":\"tool_result\"")).ToList();
        Assert.NotEmpty(resultLines);
    }

    [Fact]
    public void Codex_EmitsTextForAgentMessages()
    {
        var normalizer = new CodexOutputNormalizer();
        var output = NormalizeAll(normalizer, "codex-sample.jsonl");

        var textLines = output.Where(l =>
            l.Contains("\"type\":\"text\"") && l.Contains("\"type\":\"assistant\"")).ToList();
        Assert.NotEmpty(textLines);
    }

    // ─── Copilot ─────────────────────────────────────────────────────────────

    [Fact]
    public void Copilot_EmitsInitEvent()
    {
        var normalizer = new CopilotOutputNormalizer();
        var output = NormalizeAll(normalizer, "copilot-sample.jsonl");

        var initLine = output.FirstOrDefault(l => l.Contains("\"subtype\":\"init\""));
        Assert.NotNull(initLine);
        var node = ParseLine(initLine);
        Assert.Equal("system", node?["type"]?.GetValue<string>());
    }

    [Fact]
    public void Copilot_SkipsReportIntent()
    {
        var normalizer = new CopilotOutputNormalizer();
        var output = NormalizeAll(normalizer, "copilot-sample.jsonl");

        var hasReportIntent = output.Any(l => l.Contains("report_intent"));
        Assert.False(hasReportIntent, "report_intent meta-tool should be filtered out");
    }

    [Fact]
    public void Copilot_PreservesOriginalToolNames()
    {
        var normalizer = new CopilotOutputNormalizer();
        var output = NormalizeAll(normalizer, "copilot-sample.jsonl");

        var toolUseLines = output.Where(l => l.Contains("\"type\":\"tool_use\"")).ToList();
        Assert.NotEmpty(toolUseLines);

        foreach (var line in toolUseLines)
        {
            var node = ParseLine(line);
            var content = node?["message"]?["content"]?[0];
            var name = content?["name"]?.GetValue<string>();
            Assert.NotNull(name);
            Assert.NotEmpty(name);
        }
    }

    [Fact]
    public void Copilot_EmitsToolResults()
    {
        var normalizer = new CopilotOutputNormalizer();
        var output = NormalizeAll(normalizer, "copilot-sample.jsonl");

        var resultLines = output.Where(l => l.Contains("\"type\":\"tool_result\"")).ToList();
        Assert.NotEmpty(resultLines);
    }

    [Fact]
    public void Copilot_EmitsTextContent()
    {
        var normalizer = new CopilotOutputNormalizer();
        var output = NormalizeAll(normalizer, "copilot-sample.jsonl");

        var textLines = output.Where(l =>
            l.Contains("\"type\":\"text\"") && l.Contains("\"type\":\"assistant\"")).ToList();
        Assert.NotEmpty(textLines);
    }

    [Fact]
    public void Copilot_EmitsResult()
    {
        var normalizer = new CopilotOutputNormalizer();
        var output = NormalizeAll(normalizer, "copilot-sample.jsonl");

        var resultLine = output.LastOrDefault(l =>
            l.Contains("\"type\":\"result\"") && l.Contains("\"subtype\""));
        Assert.NotNull(resultLine);
    }

    // ─── Factory ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("claude", typeof(ClaudeOutputNormalizer))]
    [InlineData("gemini", typeof(GeminiOutputNormalizer))]
    [InlineData("codex", typeof(CodexOutputNormalizer))]
    [InlineData("opencode", typeof(OpenCodeOutputNormalizer))]
    [InlineData("copilot", typeof(CopilotOutputNormalizer))]
    [InlineData("unknown", typeof(ClaudeOutputNormalizer))]
    public void Factory_ReturnsCorrectNormalizer(string provider, Type expectedType)
    {
        var normalizer = OutputNormalizerFactory.Create(provider);
        Assert.IsType(expectedType, normalizer);
    }

    // ─── Edge cases ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("claude")]
    [InlineData("gemini")]
    [InlineData("codex")]
    [InlineData("opencode")]
    [InlineData("copilot")]
    public void AllNormalizers_HandleMalformedJson_Gracefully(string provider)
    {
        var normalizer = OutputNormalizerFactory.Create(provider);
        var result = normalizer.Normalize("this is not json {{{");
        // Should not throw, should return empty or pass through
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("gemini")]
    [InlineData("codex")]
    [InlineData("opencode")]
    [InlineData("copilot")]
    public void AllNormalizers_HandleStderrLines(string provider)
    {
        var normalizer = OutputNormalizerFactory.Create(provider);
        var result = normalizer.Normalize("[stderr] some error message");
        // stderr lines should pass through
        Assert.Contains("[stderr] some error message", result);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("gemini")]
    [InlineData("codex")]
    [InlineData("opencode")]
    [InlineData("copilot")]
    public void AllNormalizers_HandleEmptyLine(string provider)
    {
        var normalizer = OutputNormalizerFactory.Create(provider);
        var result = normalizer.Normalize("");
        Assert.NotNull(result);
    }
}
