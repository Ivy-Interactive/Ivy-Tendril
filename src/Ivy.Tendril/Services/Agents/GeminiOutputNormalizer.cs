using System.Text;
using System.Text.Json.Nodes;

namespace Ivy.Tendril.Services.Agents;

public class GeminiOutputNormalizer : IOutputNormalizer
{
    private static readonly HashSet<string> SkipTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "update_topic"
    };

    private readonly StringBuilder _accumulatedText = new();
    private readonly HashSet<string> _skippedToolIds = new();

    public IReadOnlyList<string> Normalize(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("[stderr]"))
            return [rawLine];

        JsonNode? node;
        try { node = JsonNode.Parse(rawLine); }
        catch { return []; }

        if (node is null) return [];

        var type = node["type"]?.GetValue<string>();

        return type switch
        {
            "init" => HandleInit(node),
            "message" => HandleMessage(node),
            "tool_use" => HandleToolUse(node),
            "tool_result" => HandleToolResult(node),
            "result" => HandleResult(node),
            _ => []
        };
    }

    public IReadOnlyList<string> Flush()
    {
        var pending = FlushAccumulatedText();
        return pending is not null ? [pending] : [];
    }

    private static IReadOnlyList<string> HandleInit(JsonNode node)
    {
        var model = node["model"]?.GetValue<string>() ?? "gemini";
        var sessionId = node["session_id"]?.GetValue<string>() ?? "";

        var init = new JsonObject
        {
            ["type"] = "system",
            ["subtype"] = "init",
            ["session_id"] = sessionId,
            ["model"] = model,
            ["tools"] = new JsonArray()
        };
        return [init.ToJsonString()];
    }

    private IReadOnlyList<string> HandleMessage(JsonNode node)
    {
        var role = node["role"]?.GetValue<string>();
        if (role != "assistant") return [];

        var content = node["content"]?.GetValue<string>() ?? "";
        var isDelta = node["delta"]?.GetValue<bool>() ?? false;

        if (isDelta)
        {
            _accumulatedText.Append(content);
            return [];
        }

        var results = new List<string>();
        var flushed = FlushAccumulatedText();
        if (flushed is not null) results.Add(flushed);

        if (!string.IsNullOrEmpty(content))
            results.Add(BuildTextEvent(content));

        return results;
    }

    private IReadOnlyList<string> HandleToolUse(JsonNode node)
    {
        var toolName = node["tool_name"]?.GetValue<string>() ?? "unknown";

        if (SkipTools.Contains(toolName))
        {
            var skippedId = node["tool_id"]?.GetValue<string>();
            if (skippedId is not null) _skippedToolIds.Add(skippedId);
            return [];
        }

        var results = new List<string>();
        var flushed = FlushAccumulatedText();
        if (flushed is not null) results.Add(flushed);

        var toolId = node["tool_id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
        var parameters = node["parameters"]?.DeepClone() ?? new JsonObject();

        var toolUseEvent = new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["id"] = $"msg_{toolId}",
                ["type"] = "message",
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = toolId,
                        ["name"] = toolName,
                        ["input"] = parameters
                    }
                }
            }
        };
        results.Add(toolUseEvent.ToJsonString());
        return results;
    }

    private IReadOnlyList<string> HandleToolResult(JsonNode node)
    {
        var toolId = node["tool_id"]?.GetValue<string>() ?? "";

        if (_skippedToolIds.Remove(toolId)) return [];

        var output = node["output"]?.GetValue<string>() ?? "";

        var toolResultEvent = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = toolId,
                        ["content"] = output
                    }
                }
            }
        };
        return [toolResultEvent.ToJsonString()];
    }

    private IReadOnlyList<string> HandleResult(JsonNode node)
    {
        var results = new List<string>();
        var flushed = FlushAccumulatedText();
        if (flushed is not null) results.Add(flushed);

        var status = node["status"]?.GetValue<string>() ?? "success";
        var stats = node["stats"];
        var durationMs = stats?["duration_ms"]?.GetValue<int>() ?? 0;
        var toolCalls = stats?["tool_calls"]?.GetValue<int>() ?? 0;
        var inputTokens = stats?["input_tokens"]?.GetValue<int>() ?? 0;
        var outputTokens = stats?["output_tokens"]?.GetValue<int>() ?? 0;

        var resultEvent = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = status == "success" ? "success" : "error",
            ["is_error"] = status != "success",
            ["duration_ms"] = durationMs,
            ["num_turns"] = Math.Max(1, toolCalls),
            ["result"] = "",
            ["total_cost_usd"] = 0,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens
            }
        };
        results.Add(resultEvent.ToJsonString());
        return results;
    }

    private string? FlushAccumulatedText()
    {
        if (_accumulatedText.Length == 0) return null;
        var text = _accumulatedText.ToString();
        _accumulatedText.Clear();
        return BuildTextEvent(text);
    }

    private static string BuildTextEvent(string text)
    {
        var assistantEvent = new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["id"] = $"msg_{Guid.NewGuid():N}",
                ["type"] = "message",
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text
                    }
                }
            }
        };
        return assistantEvent.ToJsonString();
    }
}
