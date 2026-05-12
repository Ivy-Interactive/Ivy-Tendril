using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ivy.Tendril.Services.Agents;

public class OpenCodeOutputNormalizer : IOutputNormalizer
{
    private decimal _accumulatedCost;
    private bool _initEmitted;

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
            "step_start" => HandleStepStart(node),
            "tool_use" => HandleToolUse(node),
            "text" => HandleText(node),
            "step_finish" => HandleStepFinish(node),
            _ => []
        };
    }

    public IReadOnlyList<string> Flush() => [];

    private IReadOnlyList<string> HandleStepStart(JsonNode node)
    {
        if (_initEmitted) return [];
        _initEmitted = true;

        var sessionId = node["sessionID"]?.GetValue<string>() ?? "";
        var init = new JsonObject
        {
            ["type"] = "system",
            ["subtype"] = "init",
            ["session_id"] = sessionId,
            ["model"] = "opencode",
            ["tools"] = new JsonArray()
        };
        return [init.ToJsonString()];
    }

    private static IReadOnlyList<string> HandleToolUse(JsonNode node)
    {
        var part = node["part"];
        if (part is null) return [];

        var toolName = part["tool"]?.GetValue<string>() ?? "unknown";
        var callId = part["callID"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
        var state = part["state"];
        var input = state?["input"]?.DeepClone() ?? new JsonObject();

        var results = new List<string>();

        var toolUseEvent = new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["id"] = $"msg_{callId}",
                ["type"] = "message",
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = callId,
                        ["name"] = toolName,
                        ["input"] = input
                    }
                }
            }
        };
        results.Add(toolUseEvent.ToJsonString());

        if (state?["status"]?.GetValue<string>() == "completed")
        {
            var output = state["output"]?.GetValue<string>() ?? "";
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
                            ["tool_use_id"] = callId,
                            ["content"] = output
                        }
                    }
                }
            };
            results.Add(toolResultEvent.ToJsonString());
        }

        return results;
    }

    private static IReadOnlyList<string> HandleText(JsonNode node)
    {
        var part = node["part"];
        var text = part?["text"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(text)) return [];

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
        return [assistantEvent.ToJsonString()];
    }

    private IReadOnlyList<string> HandleStepFinish(JsonNode node)
    {
        var part = node["part"];
        var reason = part?["reason"]?.GetValue<string>();
        var cost = part?["cost"]?.GetValue<decimal>() ?? 0;
        _accumulatedCost += cost;

        if (reason != "stop") return [];

        var tokens = part?["tokens"];
        var inputTokens = tokens?["input"]?.GetValue<int>() ?? 0;
        var outputTokens = tokens?["output"]?.GetValue<int>() ?? 0;

        var resultEvent = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = false,
            ["duration_ms"] = 0,
            ["num_turns"] = 1,
            ["result"] = "",
            ["total_cost_usd"] = _accumulatedCost,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens
            }
        };
        return [resultEvent.ToJsonString()];
    }
}
