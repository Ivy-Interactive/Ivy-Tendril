using System.Text.Json.Nodes;

namespace Ivy.Tendril.Services.Agents;

public class CodexOutputNormalizer : IOutputNormalizer
{
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
            "thread.started" => HandleThreadStarted(node),
            "item.completed" => HandleItemCompleted(node),
            "item.started" => [], // wait for completed
            "turn.started" => [],
            "turn.completed" => HandleTurnCompleted(node),
            _ => []
        };
    }

    public IReadOnlyList<string> Flush() => [];

    private IReadOnlyList<string> HandleThreadStarted(JsonNode node)
    {
        if (_initEmitted) return [];
        _initEmitted = true;

        var threadId = node["thread_id"]?.GetValue<string>() ?? "";
        var init = new JsonObject
        {
            ["type"] = "system",
            ["subtype"] = "init",
            ["session_id"] = threadId,
            ["model"] = "codex",
            ["tools"] = new JsonArray()
        };
        return [init.ToJsonString()];
    }

    private static IReadOnlyList<string> HandleItemCompleted(JsonNode node)
    {
        var item = node["item"];
        if (item is null) return [];

        var itemType = item["type"]?.GetValue<string>();

        return itemType switch
        {
            "agent_message" => HandleAgentMessage(item),
            "command_execution" => HandleCommandExecution(item),
            _ => []
        };
    }

    private static IReadOnlyList<string> HandleAgentMessage(JsonNode item)
    {
        var text = item["text"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(text)) return [];

        var assistantEvent = new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["id"] = $"msg_{item["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString()}",
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

    private static IReadOnlyList<string> HandleCommandExecution(JsonNode item)
    {
        var itemId = item["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
        var command = item["command"]?.GetValue<string>() ?? "";
        var output = item["aggregated_output"]?.GetValue<string>() ?? "";

        var results = new List<string>();

        // Emit tool_use (synthetic Bash)
        var toolUseEvent = new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["id"] = $"msg_{itemId}",
                ["type"] = "message",
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = itemId,
                        ["name"] = "Bash",
                        ["input"] = new JsonObject
                        {
                            ["command"] = command
                        }
                    }
                }
            }
        };
        results.Add(toolUseEvent.ToJsonString());

        // Emit tool_result
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
                        ["tool_use_id"] = itemId,
                        ["content"] = output
                    }
                }
            }
        };
        results.Add(toolResultEvent.ToJsonString());

        return results;
    }

    private static IReadOnlyList<string> HandleTurnCompleted(JsonNode node)
    {
        var usage = node["usage"];
        var inputTokens = usage?["input_tokens"]?.GetValue<int>() ?? 0;
        var outputTokens = usage?["output_tokens"]?.GetValue<int>() ?? 0;

        var resultEvent = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = false,
            ["duration_ms"] = 0,
            ["num_turns"] = 1,
            ["result"] = "",
            ["total_cost_usd"] = 0,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens
            }
        };
        return [resultEvent.ToJsonString()];
    }
}
